using Acordater.App.Voice;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Acordater.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	/// <summary>True while the app is on screen, so the wake word can start capture directly.</summary>
	public static bool IsResumed { get; private set; }

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		CaptureRequests.Ended += LeaveLockScreen;
		if (savedInstanceState is null) // not when recreated, or it would listen again
			CaptureIntents.Handle(this, Intent);
	}

	protected override void OnNewIntent(Intent? intent)
	{
		base.OnNewIntent(intent);
		CaptureIntents.Handle(this, intent);
	}

	protected override void OnResume()
	{
		base.OnResume();
		IsResumed = true;
	}

	protected override void OnPause()
	{
		IsResumed = false;
		base.OnPause();
	}

	protected override void OnStop()
	{
		LeaveLockScreen();
		base.OnStop();
	}

	protected override void OnDestroy()
	{
		CaptureRequests.Ended -= LeaveLockScreen;
		base.OnDestroy();
	}

	// A capture from the wake word may show over the lock screen; the rest of the app must not.
	void LeaveLockScreen()
	{
		SetShowWhenLocked(false);
		SetTurnScreenOn(false);
	}
}
