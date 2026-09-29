using Acordater.App.Resources.Strings;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;

namespace Acordater.App;

/// <summary>Permissions the alarm needs beyond those granted at install time.</summary>
static class AlarmPermissions
{
	static bool askedForFullScreen;

	public static async Task RequestAsync(Page page)
	{
		await Permissions.RequestAsync<Permissions.PostNotifications>();
		await RequestFullScreenAsync(page);
	}

	// Android 14+ may deny full-screen intents to sideloaded apps; then the alarm only shows as a notification.
	static async Task RequestFullScreenAsync(Page page)
	{
		if (askedForFullScreen) return;

		var context = global::Android.App.Application.Context;
		var notifications = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
		if (notifications.CanUseFullScreenIntent()) return;

		askedForFullScreen = true;
		if (!await page.DisplayAlertAsync(AppResources.FullScreenTitle, AppResources.FullScreenMessage, AppResources.OpenSettings, AppResources.NotNow))
			return;

		var intent = new Intent(Settings.ActionManageAppUseFullScreenIntent, global::Android.Net.Uri.Parse($"package:{context.PackageName}"));
		intent.AddFlags(ActivityFlags.NewTask);
		context.StartActivity(intent);
	}
}
