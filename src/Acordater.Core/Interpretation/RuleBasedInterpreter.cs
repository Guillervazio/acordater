using System.Globalization;
using System.Text.RegularExpressions;

namespace Acordater.Core.Interpretation;

/// <summary>
/// Offline Spanish/English interpreter. Extracts at most one time expression (relative such as "en 2 horas",
/// or absolute such as "mañana a las 9") and returns the rest of the utterance as the reminder text.
/// Hour disambiguation rules are in docs/spec.md, section 4.1.
/// </summary>
public sealed class RuleBasedInterpreter(TimeProvider time) : IReminderInterpreter
{
    enum Period { Morning, Afternoon, Night }

    const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Dictionary<string, int> Numbers = new()
    {
        ["un"] = 1, ["una"] = 1, ["uno"] = 1, ["a"] = 1, ["an"] = 1, ["one"] = 1,
        ["dos"] = 2, ["two"] = 2, ["tres"] = 3, ["three"] = 3, ["cuatro"] = 4, ["four"] = 4,
        ["cinco"] = 5, ["five"] = 5, ["seis"] = 6, ["six"] = 6, ["siete"] = 7, ["seven"] = 7,
        ["ocho"] = 8, ["eight"] = 8, ["nueve"] = 9, ["nine"] = 9, ["diez"] = 10, ["ten"] = 10,
        ["once"] = 11, ["eleven"] = 11, ["doce"] = 12, ["twelve"] = 12,
        ["quince"] = 15, ["fifteen"] = 15, ["veinte"] = 20, ["twenty"] = 20,
        ["veinticinco"] = 25, ["twenty five"] = 25, ["treinta"] = 30, ["thirty"] = 30,
        ["cuarenta"] = 40, ["forty"] = 40, ["cuarenta y cinco"] = 45, ["forty five"] = 45,
        ["cincuenta"] = 50, ["fifty"] = 50, ["noventa"] = 90, ["ninety"] = 90,
    };

    // Articles count as "1" in durations ("in an hour") but never as an hour of the day ("look at a book").
    static readonly HashSet<string> NotHours = ["un", "a", "an"];

    static readonly string Number = "\\d+|" + WordAlternation(Numbers.Keys);
    static readonly string Hour = "\\d{1,2}|" + WordAlternation(Numbers.Where(n => n.Value <= 12 && !NotHours.Contains(n.Key)).Select(n => n.Key));

    static readonly Regex Trigger = new(
        @"^\s*(?:(?:hey|oye|ok)\s+acordater[\s,]*)?(?:por\s+favor[\s,]*)?(?:recu[eé]rdame|recordame|record[aá]rme|acu[eé]rdame|acordame|remind\s+me)\b[\s,]*",
        Options);

    static readonly Regex Relative = new(
        $@"\b(?:en|dentro\s+de|in|within)\s+(?:" +
        $@"(?<half>media\s+hora|half\s+an?\s+hour)" +
        $@"|(?<quarter>(?:un\s+)?cuarto\s+de\s+hora|(?:a\s+)?quarter\s+(?:of\s+an\s+)?hour)" +
        $@"|(?<n>{Number})\s+(?<unit>minutos?|minutes?|mins?|horas?|hours?|hrs?|hs?)\b(?<andHalf>\s+y\s+media|\s+and\s+a\s+half)?" +
        ")",
        Options);

    const string Day =
        @"(?<day>pasado\s+ma[nñ]ana|the\s+day\s+after\s+tomorrow|(?<!\b(?:la|el)\s)ma[nñ]ana|hoy|esta\s+(?:noche|tarde)|tomorrow|today|tonight)";

    const string PeriodPattern =
        @"(?:(?:de|a|por)\s+la\s+(?<period>ma[nñ]ana|tarde|noche|madrugada)" +
        @"|in\s+the\s+(?<period>morning|afternoon|evening)" +
        @"|at\s+(?<period>night)" +
        @"|(?<=\b(?:tomorrow|today)\s)(?<period>morning|afternoon|evening|night))";

    static readonly string TimeOfDay =
        $@"(?:a\s+las?|at)\s+(?<h>{Hour})" +
        @"(?:[:.](?<m>\d{2})|\s+y\s+(?<frac>media|cuarto)|\s+y\s+(?<m>\d{1,2})\b)?" +
        @"(?:\s*(?<ampm>[ap])\.?\s?m\b\.?)?" +
        @"(?:\s*(?:hs|horas|o'?clock|en\s+punto)\b)?";

    static readonly Regex Absolute = new(
        $@"\b(?:{Day}\s+)?(?:{PeriodPattern}\s+)?{TimeOfDay}(?:\s+{PeriodPattern})?(?:\s+{Day})?",
        Options);

    static readonly Regex DayOrPeriod = new($@"\b(?:{Day}(?:\s+{PeriodPattern})?|{PeriodPattern}(?:\s+{Day})?)", Options);

    static readonly Regex LeadingConnector = new(
        @"^(?:que\s+(?:tengo|tenemos|hay|debo|debemos)\s+que|que|de|to|that\s+i\s+(?:need|have)\s+to|that)\s+",
        Options);

    static readonly Regex TrailingConnector = new(@"\s+(?:para|for)$", Options);

    public Task<InterpretedReminder> InterpretAsync(string utterance, CancellationToken cancellationToken = default) =>
        Task.FromResult(Interpret(utterance));

    public InterpretedReminder Interpret(string utterance)
    {
        var now = time.GetLocalNow();
        var rest = Trigger.Replace(utterance, "");
        DateTimeOffset? requestedAt = null;

        if (Relative.Match(rest) is { Success: true } relative && ResolveRelative(relative) is { } delay)
        {
            requestedAt = now + delay;
            rest = Remove(rest, relative);
        }
        else if (Absolute.Match(rest) is { Success: true } absolute && ResolveAbsolute(absolute, now) is { } at)
        {
            requestedAt = at;
            rest = Remove(rest, absolute);
        }
        else if (DayOrPeriod.Match(rest) is { Success: true } dayOrPeriod && ResolveAbsolute(dayOrPeriod, now) is { } dayAt)
        {
            requestedAt = dayAt;
            rest = Remove(rest, dayOrPeriod);
        }

        var text = Clean(rest);
        return new InterpretedReminder(text.Length > 0 ? text : utterance.Trim(), requestedAt);
    }

    static TimeSpan? ResolveRelative(Match m)
    {
        if (m.Groups["half"].Success) return TimeSpan.FromMinutes(30);
        if (m.Groups["quarter"].Success) return TimeSpan.FromMinutes(15);

        if (ParseNumber(m.Groups["n"].Value) is not int n || n <= 0) return null;

        var isMinutes = m.Groups["unit"].Value.StartsWith('m') || m.Groups["unit"].Value.StartsWith('M');
        var delay = isMinutes ? TimeSpan.FromMinutes(n) : TimeSpan.FromHours(n);
        if (!isMinutes && m.Groups["andHalf"].Success) delay += TimeSpan.FromMinutes(30);
        return delay;
    }

    DateTimeOffset? ResolveAbsolute(Match m, DateTimeOffset now)
    {
        int? dayOffset = null;
        Period? period = null;
        if (m.Groups["day"].Success) (dayOffset, period) = ParseDay(m.Groups["day"].Value);
        if (m.Groups["period"].Success) period = ParsePeriod(m.Groups["period"].Value);

        var explicitHour = m.Groups["h"].Success;
        var hasAmPm = m.Groups["ampm"].Success;
        int hour, minute = 0;

        if (!explicitHour)
        {
            hour = period switch { Period.Afternoon => 15, Period.Night => 20, _ => 9 };
        }
        else
        {
            if (ParseNumber(m.Groups["h"].Value) is not { } h) return null;
            hour = h;
            if (m.Groups["m"].Success) minute = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
            else if (m.Groups["frac"].Success) minute = m.Groups["frac"].Value.Equals("media", StringComparison.OrdinalIgnoreCase) ? 30 : 15;
            if (hour > 23 || minute > 59) return null;

            if (hasAmPm)
            {
                if (hour > 12) return null;
                var pm = m.Groups["ampm"].Value.Equals("p", StringComparison.OrdinalIgnoreCase);
                hour = hour % 12 + (pm ? 12 : 0);
            }
            else if (period is Period.Afternoon or Period.Night && hour < 12)
            {
                hour += 12;
            }
        }

        var today = DateOnly.FromDateTime(now.DateTime);
        var timeOfDay = new TimeOnly(hour, minute);

        if (dayOffset is int offset and > 0)
            return LocalTime.At(time.LocalTimeZone, today.AddDays(offset), timeOfDay);

        // No day means the next occurrence; an explicit "today" in the past is left for the scheduler to reject.
        var at = LocalTime.At(time.LocalTimeZone, today, timeOfDay);
        return at > now || dayOffset is 0 ? at : LocalTime.At(time.LocalTimeZone, today.AddDays(1), timeOfDay);
    }

    static (int Offset, Period? Period) ParseDay(string day)
    {
        var d = Normalize(day);
        if (d.StartsWith("pasado", StringComparison.Ordinal) || d.StartsWith("the day after", StringComparison.Ordinal)) return (2, null);
        if (d is "manana" or "tomorrow") return (1, null);
        if (d is "tonight" or "esta noche") return (0, Period.Night);
        if (d is "esta tarde") return (0, Period.Afternoon);
        return (0, null);
    }

    static Period ParsePeriod(string period) => Normalize(period) switch
    {
        "tarde" or "afternoon" => Period.Afternoon,
        "noche" or "evening" or "night" => Period.Night,
        _ => Period.Morning,
    };

    static int? ParseNumber(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return n;
        return Numbers.TryGetValue(Regex.Replace(value.ToLowerInvariant(), @"[\s-]+", " "), out var word) ? word : null;
    }

    static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"\s+", " ").Replace('ñ', 'n');

    static string Remove(string text, Match m) => text[..m.Index] + " " + text[(m.Index + m.Length)..];

    static string Clean(string text)
    {
        const string edgeChars = " ,.;:!?¡¿";
        text = Regex.Replace(text, @"\s+", " ").Trim(edgeChars.ToCharArray());
        text = LeadingConnector.Replace(text, "");
        text = TrailingConnector.Replace(text, "");
        return text.Trim(edgeChars.ToCharArray());
    }

    // Longest first so that e.g. "cuarenta y cinco" wins over "cuarenta"; spaces also accept hyphens ("forty-five").
    static string WordAlternation(IEnumerable<string> words) =>
        string.Join('|', words.OrderByDescending(w => w.Length).Select(w => Regex.Escape(w).Replace("\\ ", @"[\s-]+")));
}
