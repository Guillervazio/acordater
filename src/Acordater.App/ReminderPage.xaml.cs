using Acordater.App.ViewModels;

namespace Acordater.App;

public partial class ReminderPage : ContentPage
{
	readonly ReminderViewModel viewModel;

	public ReminderPage(ReminderViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = this.viewModel = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await viewModel.StartAsync();
	}

	protected override void OnDisappearing()
	{
		viewModel.StopAutoSave();
		base.OnDisappearing();
	}

	void OnFieldFocused(object? sender, FocusEventArgs e) => viewModel.StopAutoSave();
}
