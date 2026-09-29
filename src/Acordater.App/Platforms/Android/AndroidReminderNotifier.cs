using Acordater.App.Resources.Strings;
using Acordater.Core;
using Acordater.Core.Alerts;
using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.Media;

namespace Acordater.App;

public sealed class AndroidReminderNotifier : IReminderNotifier
{
	// Channel settings are immutable once created; bump the id to change sound or importance.
	const string ChannelId = "reminders_v1";

	// Notifications are keyed by tag (the reminder id); the numeric id is constant.
	const int NotificationId = 1;

	static Context Context => global::Android.App.Application.Context;

	static NotificationManager NotificationManager => (NotificationManager)Context.GetSystemService(Context.NotificationService)!;

	public void Show(Reminder reminder)
	{
		EnsureChannel();

		var icon = Icon.CreateWithResource(Context, Resource.Drawable.ic_notification);
		var notification = new Notification.Builder(Context, ChannelId);
		notification.SetSmallIcon(icon);
		notification.SetContentTitle(reminder.Text);
		notification.SetContentText(AppResources.NotificationBody);
		notification.SetCategory(Notification.CategoryReminder);
		notification.SetContentIntent(ReminderIntents.OpenApp(Context));
		notification.AddAction(new Notification.Action.Builder(icon, AppResources.Done,
			ReminderIntents.Broadcast<ReminderActionReceiver>(Context, ReminderIntents.ActionDone, reminder.Id)).Build());
		notification.AddAction(new Notification.Action.Builder(icon, AppResources.Snooze,
			ReminderIntents.Broadcast<ReminderActionReceiver>(Context, ReminderIntents.ActionSnooze, reminder.Id)).Build());

		NotificationManager.Notify(reminder.Id.ToString(), NotificationId, notification.Build());
	}

	public void Dismiss(Guid reminderId) => NotificationManager.Cancel(reminderId.ToString(), NotificationId);

	static void EnsureChannel()
	{
		var channel = new NotificationChannel(ChannelId, AppResources.ChannelName, NotificationImportance.High);
		channel.EnableVibration(true);
		channel.SetSound(
			RingtoneManager.GetDefaultUri(RingtoneType.Notification),
			new AudioAttributes.Builder().SetUsage(AudioUsageKind.Notification)!.Build());
		NotificationManager.CreateNotificationChannel(channel);
	}
}
