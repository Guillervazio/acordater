using Acordater.Core.Alerts;
using Android.App;
using Android.Content;

namespace Acordater.App;

/// <summary>Intents that carry a reminder id, plus the glue that lets broadcast receivers call <see cref="ReminderService"/>.</summary>
static class ReminderIntents
{
	public const string ActionAlarm = "com.acordater.app.action.ALARM";
	public const string ActionDone = "com.acordater.app.action.DONE";
	public const string ActionSnooze = "com.acordater.app.action.SNOOZE";

	// The id travels in the data URI, not in extras, so each reminder gets its own PendingIntent identity.
	public static PendingIntent Broadcast<TReceiver>(Context context, string action, Guid reminderId)
		where TReceiver : BroadcastReceiver
	{
		var intent = new Intent(context, typeof(TReceiver))
			.SetAction(action)
			.SetData(global::Android.Net.Uri.Parse($"acordater://reminder/{reminderId}"));
		return PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
	}

	public static PendingIntent OpenApp(Context context)
	{
		var intent = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!)!;
		intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
		return PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
	}

	public static Guid? ReminderId(Intent? intent) =>
		Guid.TryParse(intent?.Data?.LastPathSegment, out var id) ? id : null;

	/// <summary>Runs async work from a receiver, keeping the process alive until it finishes.</summary>
	public static void RunAsync(BroadcastReceiver receiver, Func<ReminderService, Task> work)
	{
		var pendingResult = receiver.GoAsync();
		Task.Run(async () =>
		{
			try
			{
				await work(IPlatformApplication.Current!.Services.GetRequiredService<ReminderService>());
			}
			catch (Exception ex)
			{
				global::Android.Util.Log.Error("Acordater", ex.ToString());
			}
			finally
			{
				pendingResult?.Finish();
			}
		});
	}
}
