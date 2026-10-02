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

	bool openedOverLockScreen;

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		CaptureRequests.Ended += OnCaptureEnded;
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
		CaptureRequests.Ended -= OnCaptureEnded;
		base.OnDestroy();
	}

	/// <summary>A capture from the wake word, opened with the phone locked: shows over the lock screen until it ends.</summary>
	public void ShowOverLockScreen()
	{
		SetShowWhenLocked(true);
		SetTurnScreenOn(true);
		openedOverLockScreen = IsLocked;
	}

	// The capture is over. If it was opened over the lock screen and the phone is still locked, the app steps aside, so
	// unlocking shows what was there before; if the user unlocked meanwhile (e.g. to correct it), it stays.
	void OnCaptureEnded()
	{
		var stepAside = openedOverLockScreen && IsLocked;
		LeaveLockScreen();
		if (stepAside) MoveTaskToBack(true);
	}

	// A capture from the wake word may show over the lock screen; the rest of the app must not.
	void LeaveLockScreen()
	{
		openedOverLockScreen = false;
		SetShowWhenLocked(false);
		SetTurnScreenOn(false);
	}

	bool IsLocked => ((KeyguardManager)GetSystemService(KeyguardService)!).IsKeyguardLocked;
}
