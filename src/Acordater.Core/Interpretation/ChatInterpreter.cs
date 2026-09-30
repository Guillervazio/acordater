using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Acordater.Core.Interpretation;

/// <summary>
/// Interprets with an LLM behind <see cref="IChatClient"/> (Claude, OpenAI, Gemini... with the user's own API key).
/// Every provider gets the same prompt: the current local date and time, the time zone and the language as context,
/// and the rules of docs/spec.md section 4.1. The answer must be strict JSON and is validated here.
/// When the provider fails, is unreachable, takes longer than the timeout or answers something invalid,
/// the <see cref="RuleBasedInterpreter"/> answers instead and the result says why (<see cref="InterpretedReminder.Failure"/>).
/// </summary>
public sealed class ChatInterpreter : IReminderInterpreter
{
    /// <summary>Short, because the voice flow waits for the answer.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(7);

    const int MaxDetailLength = 300;

    static readonly string[] WhenFormats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];

    // Stable across requests (a cacheable prefix); everything that changes goes in the user message.
    internal const string SystemPrompt = """
        You extract reminders for a reminder app. The user dictated one sentence, in Spanish or English, asking to be
        reminded of something, optionally saying when.

        Reply with only a JSON object, without markdown or any other text, with exactly these keys:
        {"text": string, "when": string or null}

        "text": what to remember, with the user's words and in the language of the sentence. Never translate it: a
        Spanish sentence gives Spanish text even when the app language is English. Remove the trigger words
        ("recordame", "recuérdame", "acordame", "remind me", "hey Cordie", "por favor", "please"), the time expression
        and leading connectors ("que", "que tengo que", "de", "to"). It is never empty.

        "when": the local date and time of the first reminder as "yyyy-MM-ddTHH:mm", or null when the sentence says
        neither a time nor a day. Rules:
        - Relative times count from now: "en 2 horas" / "in 2 hours" is now + 2 h, "en media hora" is now + 30 min.
        - 24-hour clock: a spoken hour is literal. "a las 9" is 09:00, "a las 3" is 03:00, "a las 21" is 21:00.
          Only a period of the day or am/pm changes it: "a las 6 de la tarde", "a la tarde a las 6" and "at 6 pm"
          are 18:00; "a las 9 de la noche" and "tonight at 9" are 21:00; "a las 8 de la mañana" is 08:00.
        - A time without a day is its next occurrence: today if it is still ahead, otherwise tomorrow.
        - A day or period without an hour: "mañana" / "tomorrow" is tomorrow at 09:00; "a la mañana" /
          "in the morning" is 09:00; "a la tarde" / "in the afternoon" is 15:00; "a la noche", "esta noche" and
          "tonight" are 20:00 (next occurrence).
        - A weekday ("el viernes", "on Friday") is the next such day after today; a date ("el 5 de octubre") is its
          next occurrence.
        """;

    readonly IChatClient chat;
    readonly string provider;
    readonly TimeProvider time;
    readonly CultureInfo culture;
    readonly TimeSpan timeout;
    readonly ChatOptions? requestOptions;
    readonly RuleBasedInterpreter fallback;

    /// <param name="provider">Name shown to the user, e.g. "Claude".</param>
    /// <param name="culture">Language of the user (sent as context).</param>
    /// <param name="requestOptions">Provider-specific request settings (model, JSON mode...), cloned for every request.</param>
    public ChatInterpreter(IChatClient chat, string provider, TimeProvider time, CultureInfo culture,
        ChatOptions? requestOptions = null, TimeSpan? timeout = null)
    {
        this.chat = chat;
        this.provider = provider;
        this.time = time;
        this.culture = culture;
        this.requestOptions = requestOptions;
        this.timeout = timeout ?? DefaultTimeout;
        fallback = new RuleBasedInterpreter(time);
    }

    public async Task<InterpretedReminder> InterpretAsync(string utterance, CancellationToken cancellationToken = default)
    {
        var now = time.GetLocalNow();
        using var deadline = new CancellationTokenSource(timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        string answer;
        try
        {
            var response = await chat.GetResponseAsync(
                [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, UserMessage(utterance, now))],
                requestOptions?.Clone(),
                linked.Token);
            answer = response.Text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Our deadline, or the HTTP client's own timeout.
            return Fallback(utterance, InterpretationFailureKind.Timeout, deadline.IsCancellationRequested ? $"> {timeout.TotalSeconds:0} s" : ex.Message);
        }
        catch (Exception ex)
        {
            return Fallback(utterance, IsNetwork(ex) ? InterpretationFailureKind.Network : InterpretationFailureKind.ProviderError, ex.Message);
        }

        return Parse(answer, now) is { } result
            ? result with { Interpreter = provider }
            : Fallback(utterance, InterpretationFailureKind.InvalidResponse, answer);
    }

    string UserMessage(string utterance, DateTimeOffset now)
    {
        var zone = time.LocalTimeZone;
        var offset = now.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return string.Create(CultureInfo.InvariantCulture,
            $"""
            Now: {now:yyyy-MM-dd'T'HH:mm} ({now.DayOfWeek}), time zone {zone.Id} (UTC{sign}{offset:hh\:mm}), app language {culture.Name}.
            Sentence: {utterance.Trim()}
            """);
    }

    /// <summary>The validated answer, or null when it is not the JSON object the prompt asks for.</summary>
    InterpretedReminder? Parse(string answer, DateTimeOffset now)
    {
        // Some models wrap JSON in markdown fences or add a sentence around it.
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end < start) return null;

        try
        {
            using var json = JsonDocument.Parse(answer.AsMemory(start, end - start + 1));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String) return null;
            var text = textElement.GetString()!.Trim();
            if (text.Length == 0) return null;

            DateTimeOffset? requestedAt = null;
            if (root.TryGetProperty("when", out var when) && when.ValueKind != JsonValueKind.Null)
            {
                if (when.ValueKind != JsonValueKind.String ||
                    !DateTime.TryParseExact(when.GetString(), WhenFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                    return null;

                var at = LocalTime.At(time.LocalTimeZone, DateOnly.FromDateTime(local), new TimeOnly(local.Hour, local.Minute));
                // A moment already past means the default rule (docs/spec.md, 4.1.1).
                requestedAt = at > now ? at : null;
            }

            return new InterpretedReminder(text, requestedAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    InterpretedReminder Fallback(string utterance, InterpretationFailureKind kind, string detail) =>
        fallback.Interpret(utterance) with { Failure = new InterpretationFailure(provider, kind, Shorten(detail)) };

    static bool IsNetwork(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SocketException or HttpRequestException { StatusCode: null } or IOException) return true;
        }
        return false;
    }

    static string Shorten(string detail)
    {
        detail = detail.Trim();
        return detail.Length <= MaxDetailLength ? detail : detail[..MaxDetailLength] + "…";
    }
}
