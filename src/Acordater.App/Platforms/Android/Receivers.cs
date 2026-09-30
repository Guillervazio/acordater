using Acordater.App.Resources.Strings;
using Acordater.App.Voice;
using Android.App;
using Android.Content;

namespace Acordater.App;

// Explicit Java names keep already-scheduled alarms and notifications valid across builds.

[BroadcastReceiver(Name = "com.acordater.app.ReminderAlarmReceiver", Enabled = true, Exported = false)]
public sealed class ReminderAlarmReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		if (ReminderIntents.ReminderId(intent) is { } id)
			ReminderIntents.RunAsync(this, service => service.AlertAsync(id));
	}
}

[BroadcastReceiver(Name = "com.acordater.app.ReminderActionReceiver", Enabled = true, Exported = false)]
public sealed class ReminderActionReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		if (ReminderIntents.ReminderId(intent) is not { } id) return;

		switch (intent?.Action)
		{
			case ReminderIntents.ActionDone:
				ReminderIntents.RunAsync(this, service => service.CompleteAsync(id));
				break;
			case ReminderIntents.ActionSnooze:
				ReminderIntents.RunAsync(this, service => service.SnoozeAsync(id));
				break;
		}
	}
}

/// <summary>
/// Alarms do not survive a reboot or an app update, so they are recreated here. The wake word service does not survive
/// either, and Android 14+ does not let a microphone foreground service start from here, so the user is asked to reactivate it.
/// </summary>
[BroadcastReceiver(Name = "com.acordater.app.BootReceiver", Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public sealed class BootReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		if (context is not null && IPlatformApplication.Current!.Services.GetRequiredService<WakeWordSettings>().Enabled)
			WakeWordService.NotifyStopped(context, AppResources.WakeWordAfterRestart);

		ReminderIntents.RunAsync(this, service => service.RescheduleAllAsync());
	}
}
