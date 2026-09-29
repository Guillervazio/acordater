using Acordater.Core.Alerts;
using Android.App;
using Android.Content;

namespace Acordater.App;

/// <summary>
/// Uses alarm-clock alarms: the most reliable kind (exempt from Doze), shown by the system as an upcoming alarm.
/// Requires USE_EXACT_ALARM, granted at install time.
/// </summary>
public sealed class AndroidAlarmScheduler : IAlarmScheduler
{
	static Context Context => global::Android.App.Application.Context;

	static AlarmManager AlarmManager => (AlarmManager)Context.GetSystemService(Context.AlarmService)!;

	public void Schedule(Guid reminderId, DateTimeOffset at)
	{
		var info = new AlarmManager.AlarmClockInfo(at.ToUnixTimeMilliseconds(), ReminderIntents.OpenApp(Context));
		AlarmManager.SetAlarmClock(info, AlarmIntent(reminderId));
	}

	public void Cancel(Guid reminderId) => AlarmManager.Cancel(AlarmIntent(reminderId));

	static PendingIntent AlarmIntent(Guid reminderId) =>
		ReminderIntents.Broadcast<ReminderAlarmReceiver>(Context, ReminderIntents.ActionAlarm, reminderId);
}
