namespace Acordater.App;

public partial class AppShell : Shell
{
	public const string SettingsRoute = "settings";
	public const string ReminderRoute = "reminder";

	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(SettingsRoute, typeof(SettingsPage));
		Routing.RegisterRoute(ReminderRoute, typeof(ReminderPage));
	}
}
