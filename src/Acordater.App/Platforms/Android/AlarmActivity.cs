using Acordater.App.Resources.Strings;
using Acordater.Core.Alerts;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using AButton = Android.Widget.Button;

namespace Acordater.App;

/// <summary>
/// Full-screen alarm shown over the lock screen for the reminder that is ringing.
/// A plain Android activity (not MAUI) so it opens fast and independently of the main app.
/// </summary>
[Activity(
	Name = "com.acordater.app.AlarmActivity",
	Exported = false,
	ExcludeFromRecents = true,
	LaunchMode = global::Android.Content.PM.LaunchMode.SingleInstance,
	TaskAffinity = "",
	Theme = "@android:style/Theme.DeviceDefault.NoActionBar")]
public sealed class AlarmActivity : Activity
{
	public static PendingIntent PendingIntentFor(Context context)
	{
		var intent = new Intent(context, typeof(AlarmActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.NoUserAction);
		return PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
	}

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		SetShowWhenLocked(true);
		SetTurnScreenOn(true);
		Render();
	}

	protected override void OnNewIntent(Intent? intent)
	{
		base.OnNewIntent(intent);
		Render();
	}

	void Render()
	{
		if (AlarmRingingService.Current is not { } ringing)
		{
			Finish();
			return;
		}

		var padding = Dp(32);
		var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
		layout.SetGravity(GravityFlags.Center);
		layout.SetPadding(padding, padding, padding, padding);

		var text = new TextView(this) { Text = ringing.Text, TextSize = 34, Gravity = GravityFlags.Center };
		layout.AddView(text, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = Dp(48) });

		layout.AddView(ActionButton(AppResources.Done, service => service.CompleteAsync(ringing.Id)), ButtonLayout());
		layout.AddView(ActionButton(AppResources.Snooze, service => service.SnoozeAsync(ringing.Id)), ButtonLayout());

		SetContentView(layout);
	}

	AButton ActionButton(string label, Func<ReminderService, Task> action)
	{
		var button = new AButton(this) { Text = label, TextSize = 22 };
		button.Click += async (_, _) =>
		{
			button.Enabled = false;
			try
			{
				var service = IPlatformApplication.Current!.Services.GetRequiredService<ReminderService>();
				await Task.Run(() => action(service));
			}
			catch (Exception ex)
			{
				global::Android.Util.Log.Error("Acordater", ex.ToString());
			}
			Render(); // next ringing reminder, or close
		};
		return button;
	}

	LinearLayout.LayoutParams ButtonLayout() =>
		new(ViewGroup.LayoutParams.MatchParent, Dp(72)) { BottomMargin = Dp(16) };

	int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);
}
