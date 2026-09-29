using System.Collections.ObjectModel;
using System.Globalization;
using Acordater.App.Resources.Strings;
using Acordater.Core.Alerts;
using Acordater.Core.Interpretation;
using Acordater.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Acordater.App.ViewModels;

public sealed record ReminderItem(Guid Id, string Text, string When);

public sealed partial class MainViewModel(
	ReminderService reminders,
	ReminderScheduler scheduler,
	IReminderInterpreter interpreter,
	ReminderTimeFormatter formatter) : ObservableObject
{
	[ObservableProperty]
	public partial string NewReminderText { get; set; } = "";

	public ObservableCollection<ReminderItem> Reminders { get; } = [];

	[RelayCommand]
	async Task LoadAsync()
	{
		var pending = await reminders.GetPendingAsync();
		Reminders.Clear();
		foreach (var reminder in pending)
			Reminders.Add(new ReminderItem(reminder.Id, reminder.Text, formatter.Format(reminder.NextReminderAt)));
	}

	[RelayCommand]
	async Task AddAsync()
	{
		var utterance = NewReminderText.Trim();
		if (utterance.Length == 0) return;

		var interpreted = await interpreter.InterpretAsync(utterance);
		var reminder = scheduler.Create(interpreted.Text, interpreted.RequestedAt);

		var message = string.Format(CultureInfo.CurrentCulture, AppResources.ConfirmMessage, reminder.Text, formatter.Format(reminder.NextReminderAt));
		if (!await Shell.Current.DisplayAlertAsync(AppResources.ConfirmTitle, message, AppResources.Save, AppResources.Cancel))
			return;

		await reminders.AddAsync(reminder);
		NewReminderText = "";
		await LoadAsync();
	}

	[RelayCommand]
	async Task CompleteAsync(ReminderItem item)
	{
		await reminders.CompleteAsync(item.Id);
		Reminders.Remove(item);
	}
}
