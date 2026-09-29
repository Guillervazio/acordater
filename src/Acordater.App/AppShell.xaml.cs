namespace Acordater.App;

public partial class AppShell : Shell
{
	public const string SettingsRoute = "settings";

	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(SettingsRoute, typeof(SettingsPage));
	}
}
