using Acordater.App.ViewModels;

namespace Acordater.App;

public partial class MainPage : ContentPage
{
	readonly MainViewModel viewModel;

	public MainPage(MainViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = this.viewModel = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await viewModel.LoadCommand.ExecuteAsync(null);
	}
}
