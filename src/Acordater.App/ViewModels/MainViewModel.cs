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
	QuietHoursSettings quietHours,
	IWakeWordDetector wakeWord,
	WakeWordSettings wakeWordSettings,
	VoiceFeedback voice,
	LanguageSettings language) : ObservableObject
{
	CancellationTokenSource? listening;
	bool announce;

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

	/// <summary>
	/// Capture started without looking at the phone (wake word, widget, tile): says that it is listening first,
	/// so the user knows when to talk.
	/// </summary>
	public Task ListenOnRequestAsync()
	{
		announce = true;
		return ListenCommand.ExecuteAsync(null);
	}

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
		var confirming = false;
		try
		{
			string? heard;
			// The wake word detector gives the microphone to the speech recognizer meanwhile.
			using (wakeWord.Pause())
			{
				if (announce)
				{
					announce = false;
					var culture = language.VoiceCulture;
					await voice.SpeakAsync(ReminderTimeFormatter.Text(nameof(AppResources.ListeningPrompt), culture), culture, session.Token);
				}
				heard = await speech.ListenAsync(partial => NewReminderText = partial, session.Token);
			}
			if (string.IsNullOrWhiteSpace(heard)) return;

			NewReminderText = heard;
			IsListening = false;
			confirming = await ConfirmAsync(spoken: true);
		}
		catch (OperationCanceledException)
		{
			// Tapped while the prompt was being spoken.
		}
		catch (SpeechUnavailableException ex)
		{
			await Shell.Current.DisplayAlertAsync(AppResources.AppTitle, ex.Message, AppResources.Ok);
		}
		finally
		{
			listening = null;
			IsListening = false;
			if (!confirming) CaptureRequests.End();
		}
	}

	/// <summary>
	/// Turns the wake word back on when it should be listening but is not (after a reboot, an app update or being
	/// stopped by the system): only possible while the app is in the foreground on Android 14+.
	/// </summary>
	public async Task EnsureWakeWordAsync()
	{
		if (!wakeWordSettings.Enabled || wakeWord.IsRunning) return;
		try
		{
			await wakeWord.StartAsync();
		}
		catch (WakeWordUnavailableException ex)
		{
			System.Diagnostics.Debug.WriteLine($"Wake word not restarted: {ex.Message}");
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
