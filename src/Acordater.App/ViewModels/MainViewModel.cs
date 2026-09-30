using System.Collections.ObjectModel;
using Acordater.App.Resources.Strings;
using Acordater.App.Voice;
using Acordater.Core;
using Acordater.Core.Alerts;
using Acordater.Core.Interpretation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Acordater.App.ViewModels;

public sealed record ReminderItem(Reminder Reminder, string When)
{
	public string Text => Reminder.Text;
}

public sealed partial class MainViewModel(
	ReminderService reminders,
	IReminderInterpreter interpreter,
	ISpeechRecognizer speech,
	ReminderTimeFormatter formatter,
	QuietHoursSettings quietHours) : ObservableObject
{
	CancellationTokenSource? listening;

	/// <summary>First run: quiet hours must be chosen before using the app.</summary>
	public bool NeedsSetup => !quietHours.IsConfigured;

	[ObservableProperty]
	public partial string NewReminderText { get; set; } = "";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ListenLabel))]
	public partial bool IsListening { get; set; }

	/// <summary>Waiting for the interpreter (an AI provider can take a few seconds).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ListenLabel))]
	public partial bool IsInterpreting { get; set; }

	public string ListenLabel => IsInterpreting ? AppResources.Interpreting : IsListening ? AppResources.Listening : AppResources.Speak;

	public ObservableCollection<ReminderItem> Reminders { get; } = [];

	[RelayCommand]
	async Task LoadAsync()
	{
		var pending = await reminders.GetPendingAsync();
		Reminders.Clear();
		foreach (var reminder in pending)
			Reminders.Add(new ReminderItem(reminder, formatter.Format(reminder.NextReminderAt)));
	}

	[RelayCommand]
	Task AddAsync() => ConfirmAsync(spoken: false);

	/// <summary>Starts listening; while listening, a second tap means "I'm done talking".</summary>
	[RelayCommand(AllowConcurrentExecutions = true)]
	async Task ListenAsync()
	{
		if (listening is not null)
		{
			listening.Cancel();
			return;
		}

		using var session = listening = new CancellationTokenSource();
		IsListening = true;
		NewReminderText = "";
		try
		{
			var heard = await speech.ListenAsync(partial => NewReminderText = partial, session.Token);
			if (string.IsNullOrWhiteSpace(heard)) return;

			NewReminderText = heard;
			IsListening = false;
			await ConfirmAsync(spoken: true);
		}
		catch (SpeechUnavailableException ex)
		{
			await Shell.Current.DisplayAlertAsync(AppResources.AppTitle, ex.Message, AppResources.Ok);
		}
		finally
		{
			listening = null;
			IsListening = false;
		}
	}

	[RelayCommand]
	Task EditAsync(ReminderItem item) =>
		Shell.Current.GoToAsync(AppShell.ReminderRoute, new ShellNavigationQueryParameters
		{
			[ReminderViewModel.EditKey] = item.Reminder,
		});

	[RelayCommand]
	Task OpenSettingsAsync() => Shell.Current.GoToAsync(AppShell.SettingsRoute);

	[RelayCommand]
	async Task CompleteAsync(ReminderItem item)
	{
		await reminders.CompleteAsync(item.Reminder.Id);
		Reminders.Remove(item);
	}

	/// <summary>Shows what was understood, to correct it before saving. False when there was nothing to confirm.</summary>
	async Task<bool> ConfirmAsync(bool spoken)
	{
		var utterance = NewReminderText.Trim();
		if (utterance.Length == 0 || IsInterpreting) return false;

		InterpretedReminder interpreted;
		IsInterpreting = true;
		try
		{
			interpreted = await interpreter.InterpretAsync(utterance);
		}
		finally
		{
			IsInterpreting = false;
		}

		NewReminderText = "";
		await Shell.Current.GoToAsync(AppShell.ReminderRoute, new ShellNavigationQueryParameters
		{
			[ReminderViewModel.DraftKey] = interpreted,
			[ReminderViewModel.SpokenKey] = spoken,
		});
		return true;
	}
}
