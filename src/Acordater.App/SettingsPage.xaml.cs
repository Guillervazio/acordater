using Acordater.App.ViewModels;

namespace Acordater.App;

public partial class SettingsPage : ContentPage
{
	readonly SettingsViewModel viewModel;

	public SettingsPage(SettingsViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = this.viewModel = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await viewModel.LoadAsync();
	}
}
