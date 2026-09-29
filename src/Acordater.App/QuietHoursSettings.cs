using Acordater.Core.Scheduling;

namespace Acordater.App;

/// <summary>The user's quiet hours, stored in app preferences as minutes after midnight.</summary>
public sealed class QuietHoursSettings(IPreferences preferences) : IQuietHoursProvider
{
	const string StartKey = "quiet_hours_start";
	const string EndKey = "quiet_hours_end";

	/// <summary>False until the user saves quiet hours for the first time (first-run setup).</summary>
	public bool IsConfigured => preferences.ContainsKey(StartKey);

	public QuietHours Current => new(Read(StartKey, QuietHours.Default.Start), Read(EndKey, QuietHours.Default.End));

	public void Save(QuietHours quietHours)
	{
		preferences.Set(StartKey, ToMinutes(quietHours.Start));
		preferences.Set(EndKey, ToMinutes(quietHours.End));
	}

	TimeOnly Read(string key, TimeOnly fallback) =>
		preferences.ContainsKey(key) ? TimeOnly.MinValue.AddMinutes(preferences.Get(key, 0)) : fallback;

	static int ToMinutes(TimeOnly time) => time.Hour * 60 + time.Minute;
}
