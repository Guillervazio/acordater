using Acordater.Core;
using Acordater.Core.Alerts;

namespace Acordater.App;

/// <summary>On Android the alert is the ringing alarm; see <see cref="AlarmRingingService"/>.</summary>
public sealed class AndroidReminderNotifier : IReminderNotifier
{
	public void Show(Reminder reminder) =>
		AlarmRingingService.Ring(global::Android.App.Application.Context, reminder.Id, reminder.Text);

	public void Dismiss(Guid reminderId) =>
		AlarmRingingService.Silence(global::Android.App.Application.Context, reminderId);
}
