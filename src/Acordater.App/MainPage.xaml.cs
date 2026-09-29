using Acordater.App.ViewModels;

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
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await Permissions.RequestAsync<Permissions.PostNotifications>();
		await viewModel.LoadCommand.ExecuteAsync(null);
	}
}
