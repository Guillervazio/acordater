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

/// <summary>Alarms do not survive a reboot or an app update, so they are recreated here.</summary>
[BroadcastReceiver(Name = "com.acordater.app.BootReceiver", Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public sealed class BootReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent) =>
		ReminderIntents.RunAsync(this, service => service.RescheduleAllAsync());
}
