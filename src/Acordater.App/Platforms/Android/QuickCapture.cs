using Acordater.App.Resources.Strings;
using Acordater.App.Voice;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Service.QuickSettings;
using Android.Widget;

namespace Acordater.App;

// Quick ways to capture a reminder by voice: all open the app already listening (docs/spec.md, phases 4 and 5).
// Explicit Java names keep placed widgets and added tiles valid across builds.

static class CaptureIntents
{
	public const string ActionCapture = "com.acordater.app.action.CAPTURE";

	/// <summary>Capture started by the wake word: may open over the lock screen (docs/spec.md, section 4.7).</summary>
	public const string ActionWakeWord = "com.acordater.app.action.WAKE_WORD_CAPTURE";

	// Channel settings are immutable once created; bump the id to change them.
	const string WakeWordChannelId = "wake_word_capture_v1";
	const int WakeWordNotificationId = 3;
	static readonly TimeSpan WakeWordNotificationTimeout = TimeSpan.FromSeconds(30);
	// An untapped heads-up turns into the full-screen capture when the screen goes off, maybe long after the detection.
	static readonly TimeSpan WakeWordFullScreenMaxAge = TimeSpan.FromSeconds(10);
	const string DetectedAtExtra = "detected_at";

	public static PendingIntent OpenCapture(Context context) => Open(context, ActionCapture, requestCode: 0);

	/// <summary>
	/// The wake word was heard. With the app on screen, capture starts right away. Otherwise Android 10+ blocks
	/// starting an activity from the background, so, like the alarm, a full-screen notification opens the capture
	/// (over the lock screen, turning the screen on); while the phone is in use it shows as a heads-up to tap instead.
	/// </summary>
	public static void OnWakeWord(Context context)
	{
		if (MainActivity.IsResumed)
		{
			CaptureRequests.Raise();
			return;
		}

		var notifications = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
		var channel = new NotificationChannel(WakeWordChannelId, AppResources.WakeWordCaptureChannel, NotificationImportance.High);
		channel.SetSound(null, null); // the microphone is about to listen
		notifications.CreateNotificationChannel(channel);

		var open = Open(context, ActionWakeWord, requestCode: 1);
		var fullScreen = Open(context, ActionWakeWord, requestCode: 3, detectedAt: SystemClock.ElapsedRealtime());
		var notification = new Notification.Builder(context, WakeWordChannelId)
			.SetSmallIcon(Android.Graphics.Drawables.Icon.CreateWithResource(context, Resource.Drawable.ic_mic))!
			.SetContentTitle(AppResources.WakeWordDetectedTitle)!
			.SetContentText(AppResources.WakeWordDetectedText)!
			.SetCategory(Notification.CategoryReminder)!
			.SetFullScreenIntent(fullScreen, true)!
			.SetContentIntent(open)!
			.SetAutoCancel(true)!
			.SetTimeoutAfter((long)WakeWordNotificationTimeout.TotalMilliseconds)!
			.Build();
		notifications.Notify(WakeWordNotificationId, notification);
	}

	/// <summary>Called by <see cref="MainActivity"/> for the intent that started or resumed it.</summary>
	public static void Handle(MainActivity activity, Intent? intent)
	{
		switch (intent?.Action)
		{
			case ActionCapture:
				CaptureRequests.Raise();
				break;
			case ActionWakeWord when IsStale(intent!):
				// Opened by the system when the screen went off, long after the detection: talking now would be a surprise.
				((NotificationManager)activity.GetSystemService(Context.NotificationService)!).Cancel(WakeWordNotificationId);
				CaptureRequests.End(); // the wake word listens again
				activity.MoveTaskToBack(true);
				break;
			case ActionWakeWord:
				// Over the lock screen only for this capture; MainActivity undoes it when the capture ends.
				activity.ShowOverLockScreen();
				((NotificationManager)activity.GetSystemService(Context.NotificationService)!).Cancel(WakeWordNotificationId);
				CaptureRequests.Raise();
				break;
		}
	}

	// Only the full-screen intent carries the detection time: a tap on the heads-up is always deliberate.
	static bool IsStale(Intent intent) =>
		intent.GetLongExtra(DetectedAtExtra, 0) is var detectedAt and > 0
		&& SystemClock.ElapsedRealtime() - detectedAt > WakeWordFullScreenMaxAge.TotalMilliseconds;

	static PendingIntent Open(Context context, string action, int requestCode, long detectedAt = 0)
	{
		var intent = new Intent(context, typeof(MainActivity))
			.SetAction(action)
			.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
		if (detectedAt > 0) intent.PutExtra(DetectedAtExtra, detectedAt);
		return PendingIntent.GetActivity(context, requestCode, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
	}
}

[Service(
	Name = "com.acordater.app.CaptureTileService",
	Exported = true,
	Label = "@string/capture_tile_label",
	Icon = "@drawable/ic_mic",
	Permission = "android.permission.BIND_QUICK_SETTINGS_TILE")]
[IntentFilter([TileService.ActionQsTile])]
public sealed class CaptureTileService : TileService
{
	public override void OnStartListening()
	{
		base.OnStartListening();
		if (QsTile is not { } tile) return;
		tile.State = TileState.Inactive; // an action, not an on/off toggle
		tile.UpdateTile();
	}

	public override void OnClick()
	{
		base.OnClick();
		var open = CaptureIntents.OpenCapture(this);
		if (IsLocked)
			UnlockAndRun(new Java.Lang.Runnable(() => StartActivityAndCollapse(open)));
		else
			StartActivityAndCollapse(open);
	}
}

[BroadcastReceiver(Name = "com.acordater.app.CaptureWidget", Exported = true, Label = "@string/capture_widget_label")]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/capture_widget")]
public sealed class CaptureWidget : AppWidgetProvider
{
	public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
	{
		if (context is null || appWidgetManager is null || appWidgetIds is null) return;

		var views = new RemoteViews(context.PackageName, Resource.Layout.capture_widget);
		views.SetOnClickPendingIntent(global::Android.Resource.Id.Background, CaptureIntents.OpenCapture(context));
		appWidgetManager.UpdateAppWidget(appWidgetIds, views);
	}
}
