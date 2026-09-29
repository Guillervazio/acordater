using Acordater.Core.Alerts;

namespace Acordater.App;

public partial class App : Application
{
	readonly ReminderService reminders;

	public App(ReminderService reminders)
	{
		InitializeComponent();
		this.reminders = reminders;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}

	// Alarms can be lost (force stop, reinstall while developing); recreating them is idempotent.
	protected override async void OnStart()
	{
		base.OnStart();
		await reminders.RescheduleAllAsync();
	}
}
