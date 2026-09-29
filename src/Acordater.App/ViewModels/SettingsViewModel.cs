using Acordater.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Acordater.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
	readonly QuietHoursSettings settings;

	public SettingsViewModel(QuietHoursSettings settings)
	{
		this.settings = settings;
		var quietHours = settings.Current;
		QuietStart = quietHours.Start.ToTimeSpan();
		QuietEnd = quietHours.End.ToTimeSpan();
		IsFirstRun = !settings.IsConfigured;
	}

	[ObservableProperty]
	public partial TimeSpan? QuietStart { get; set; }

	[ObservableProperty]
	public partial TimeSpan? QuietEnd { get; set; }

	public bool IsFirstRun { get; }

	[RelayCommand]
	async Task SaveAsync()
	{
		settings.Save(new QuietHours(TimeOnly.FromTimeSpan(QuietStart ?? default), TimeOnly.FromTimeSpan(QuietEnd ?? default)));
		await Shell.Current.GoToAsync("..");
	}
}
