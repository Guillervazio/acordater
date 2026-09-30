using Acordater.App.ViewModels;
using Acordater.App.Voice;

namespace Acordater.App;

public partial class MainPage : ContentPage
{
	readonly MainViewModel viewModel;

	public MainPage(MainViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = this.viewModel = viewModel;

		// Reminders can change while the app is in the background (Done / Snooze from the notification).
		Loaded += (_, _) => Window.Resumed += async (_, _) => await viewModel.LoadCommand.ExecuteAsync(null);

		// The root page lives as long as its window, also while other pages are pushed on top.
		CaptureRequests.Raised += OnCaptureRequested;
	}

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
	}

	// Widget or quick settings tile while the app is already running.
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
			await viewModel.ListenCommand.ExecuteAsync(null);
	}
}
