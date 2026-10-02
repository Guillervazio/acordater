using Acordater.App.ViewModels;
using Acordater.App.Voice;
using Acordater.Core.Alerts;

namespace Acordater.App;

public partial class MainPage : ContentPage
{
	readonly MainViewModel viewModel;
	readonly ReminderService reminders;
	Window? window;

	public MainPage(MainViewModel viewModel, ReminderService reminders)
	{
		InitializeComponent();
		BindingContext = this.viewModel = viewModel;
		this.reminders = reminders;
		Loaded += OnLoaded;
	}

	// The root page lives as long as its window, also while other pages are pushed on top. Its subscriptions end with
	// the window: the process (kept alive by the wake word service) outlives it, and a stale page would otherwise take
	// the capture requests meant for the new one.
	void OnLoaded(object? sender, EventArgs e)
	{
		if (window is not null || Window is null) return; // Loaded fires again when returning from another page
		window = Window;
		window.Resumed += OnWindowResumed;
		window.Destroying += OnWindowDestroying;
		CaptureRequests.Raised += OnCaptureRequested;
		reminders.Changed += OnRemindersChanged;
	}

	void OnWindowDestroying(object? sender, EventArgs e)
	{
		viewModel.Abandon();
		CaptureRequests.Raised -= OnCaptureRequested;
		reminders.Changed -= OnRemindersChanged;
		if (window is null) return;
		window.Resumed -= OnWindowResumed;
		window.Destroying -= OnWindowDestroying;
	}

	// Back from the background: the wake word may have been stopped by the system meanwhile.
	async void OnWindowResumed(object? sender, EventArgs e)
	{
		await viewModel.LoadCommand.ExecuteAsync(null);
		await viewModel.EnsureWakeWordAsync();
	}

	// Done / Snooze from the alarm or its notification, also while the list is on screen.
	void OnRemindersChanged() => Dispatcher.Dispatch(async () => await viewModel.LoadCommand.ExecuteAsync(null));

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (viewModel.NeedsSetup)
		{
			await viewModel.OpenSettingsCommand.ExecuteAsync(null);
			return;
		}

		await AlarmPermissions.RequestAsync(this);
		await viewModel.LoadCommand.ExecuteAsync(null);
		await ListenIfRequestedAsync();
		await viewModel.EnsureWakeWordAsync();
	}

	// Widget, quick settings tile or wake word while the app is already running.
	void OnCaptureRequested() => Dispatcher.Dispatch(async () =>
	{
		if (Window is null) return; // a page of a window that was closed

		if (Navigation.NavigationStack.Count > 1)
		{
			await Navigation.PopToRootAsync(); // OnAppearing takes the request
			return;
		}
		await ListenIfRequestedAsync();
	});

	async Task ListenIfRequestedAsync()
	{
		if (CaptureRequests.TryTake() && !viewModel.IsListening)
			await viewModel.ListenOnRequestAsync();
	}
}
