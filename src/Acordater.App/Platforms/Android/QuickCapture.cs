using Acordater.App.Voice;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Service.QuickSettings;
using Android.Widget;

namespace Acordater.App;

// Quick ways to capture a reminder by voice: both open the app already listening (docs/spec.md, phase 4).
// Explicit Java names keep placed widgets and added tiles valid across builds.

static class CaptureIntents
{
	public const string ActionCapture = "com.acordater.app.action.CAPTURE";

	public static PendingIntent OpenCapture(Context context)
	{
		var intent = new Intent(context, typeof(MainActivity))
			.SetAction(ActionCapture)
			.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
		return PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
	}

	/// <summary>Called by <see cref="MainActivity"/> for the intent that started or resumed it.</summary>
	public static void Handle(Intent? intent)
	{
		if (intent?.Action == ActionCapture) CaptureRequests.Raise();
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
