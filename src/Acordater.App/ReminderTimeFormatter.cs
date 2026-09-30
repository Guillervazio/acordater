using System.Globalization;
using Acordater.App.Resources.Strings;

namespace Acordater.App;

/// <summary>Human-readable "when" for a reminder, always on a 24-hour clock.</summary>
public sealed class ReminderTimeFormatter(TimeProvider time)
{
	/// <param name="culture">Language of the text; the app's by default (a spoken text may use another).</param>
	public string Format(DateTimeOffset at, CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentUICulture;
		var now = time.GetLocalNow();
		var local = TimeZoneInfo.ConvertTime(at, time.LocalTimeZone);
		var hhmm = local.ToString("HH:mm", CultureInfo.InvariantCulture);
		var days = DateOnly.FromDateTime(local.DateTime).DayNumber - DateOnly.FromDateTime(now.DateTime).DayNumber;

		return days switch
		{
			0 => string.Format(culture, Text(nameof(AppResources.WhenToday), culture), hhmm),
			1 => string.Format(culture, Text(nameof(AppResources.WhenTomorrow), culture), hhmm),
			_ => string.Format(culture, Text(nameof(AppResources.WhenDate), culture), local.ToString("d MMM", culture), hhmm),
		};
	}

	/// <summary>A localized string in a given language, which may differ from the app's.</summary>
	public static string Text(string name, CultureInfo culture) => AppResources.ResourceManager.GetString(name, culture)!;
}
