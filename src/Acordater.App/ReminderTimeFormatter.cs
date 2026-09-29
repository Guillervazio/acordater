using System.Globalization;
using Acordater.App.Resources.Strings;

namespace Acordater.App;

/// <summary>Human-readable "when" for a reminder, always on a 24-hour clock.</summary>
public sealed class ReminderTimeFormatter(TimeProvider time)
{
	public string Format(DateTimeOffset at)
	{
		var now = time.GetLocalNow();
		var local = TimeZoneInfo.ConvertTime(at, time.LocalTimeZone);
		var hhmm = local.ToString("HH:mm", CultureInfo.InvariantCulture);
		var days = DateOnly.FromDateTime(local.DateTime).DayNumber - DateOnly.FromDateTime(now.DateTime).DayNumber;

		return days switch
		{
			0 => string.Format(CultureInfo.CurrentCulture, AppResources.WhenToday, hhmm),
			1 => string.Format(CultureInfo.CurrentCulture, AppResources.WhenTomorrow, hhmm),
			_ => string.Format(CultureInfo.CurrentCulture, AppResources.WhenDate, local.ToString("d MMM", CultureInfo.CurrentCulture), hhmm),
		};
	}
}
