using System.Globalization;
using Acordater.App.Interpretation;
using Acordater.App.Resources.Strings;
using Acordater.App.Voice;
using Acordater.Core;
using Acordater.Core.Alerts;
using Acordater.Core.Interpretation;
using Acordater.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Acordater.App.ViewModels;

/// <summary>
/// Confirms a new reminder (what the interpreter understood, correctable before saving) or edits a pending one.
/// After voice capture it reads the confirmation aloud and then saves by itself unless the user touches anything.
/// </summary>
public sealed partial class ReminderViewModel(
	ReminderService reminders,
	ReminderScheduler scheduler,
	ReminderTimeFormatter formatter,
	TimeProvider time,
	VoiceFeedback voice,
	LanguageSettings language) : ObservableObject, IQueryAttributable
{
	/// <summary>Navigation parameter: the <see cref="InterpretedReminder"/> to confirm.</summary>
	public const string DraftKey = "draft";

	/// <summary>Navigation parameter: the pending <see cref="Reminder"/> to edit.</summary>
	public const string EditKey = "reminder";

	/// <summary>Navigation parameter: true when the draft was spoken, to confirm by voice and save by itself.</summary>
	public const string SpokenKey = "spoken";

	const int AutoSaveSeconds = 5;

	Reminder? editing;
	// Used by Save while the date and time are left untouched, so a default time stays a default (quiet hours apply).
	DateTimeOffset? originalRequest;
	DateTime initialDate;
	TimeSpan initialTime;
	bool confirmByVoice;
	bool saved;
	CancellationTokenSource? autoSave;

	[ObservableProperty]
	public partial string Title { get; set; } = "";

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SaveCommand))]
	public partial string Text { get; set; } = "";

	[ObservableProperty]
	public partial DateTime? Date { get; set; }

	[ObservableProperty]
	public partial TimeSpan? Time { get; set; }

	[ObservableProperty]
	public partial string Summary { get; set; } = "";

	[ObservableProperty]
	public partial string SaveLabel { get; set; } = AppResources.Save;

	/// <summary>Which interpreter understood a new reminder (AI provider, rules, or rules as a fallback); empty when editing.</summary>
	[ObservableProperty]
	public partial string InterpreterNote { get; set; } = "";

	public DateTime MinimumDate => time.GetLocalNow().Date;

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(EditKey, out var value) && value is Reminder reminder)
		{
			editing = reminder;
			Title = AppResources.EditReminderTitle;
			Text = reminder.Text;
			originalRequest = reminder.NextReminderAt;
		}
		else if (query.TryGetValue(DraftKey, out value) && value is InterpretedReminder draft)
		{
			Title = AppResources.NewReminderTitle;
			Text = draft.Text;
			originalRequest = draft.RequestedAt;
			InterpreterNote = InterpreterDescription.Describe(draft);
		}
		confirmByVoice = query.TryGetValue(SpokenKey, out value) && value is true;

		var firstAlert = TimeZoneInfo.ConvertTime(scheduler.FirstAlertAt(originalRequest), time.LocalTimeZone);
		Date = initialDate = firstAlert.Date;
		Time = initialTime = new TimeSpan(firstAlert.Hour, firstAlert.Minute, 0);
		UpdateSummary();
	}

	/// <summary>Called when the page appears: after voice capture, speaks the confirmation and counts down to saving.</summary>
	public async Task StartAsync()
	{
		if (!confirmByVoice) return;
		confirmByVoice = false;

		autoSave = new CancellationTokenSource();
		var token = autoSave.Token;
		try
		{
			var spoken = language.VoiceCulture;
			await voice.SpeakAsync(SummaryIn(spoken), spoken, token);
			for (var secondsLeft = AutoSaveSeconds; secondsLeft > 0; secondsLeft--)
			{
				SaveLabel = string.Format(CultureInfo.CurrentCulture, AppResources.SaveCountdown, secondsLeft);
				await Task.Delay(TimeSpan.FromSeconds(1), token);
			}
		}
		catch (OperationCanceledException)
		{
			return;
		}
		await SaveCommand.ExecuteAsync(null);
	}

	/// <summary>The user is correcting something (or left): no saving by itself.</summary>
	public void StopAutoSave()
	{
		autoSave?.Cancel();
		autoSave = null;
		SaveLabel = AppResources.Save;
	}

	bool CanSave() => !string.IsNullOrWhiteSpace(Text);

	[RelayCommand(CanExecute = nameof(CanSave))]
	async Task SaveAsync()
	{
		StopAutoSave();
		if (saved || !CanSave()) return;
		saved = true;

		var text = Text.Trim();
		if (editing is null)
			await reminders.AddAsync(scheduler.Create(text, RequestedAt()));
		else if (!await reminders.EditAsync(editing.Id, text, RequestedAt()))
			await Shell.Current.DisplayAlertAsync(AppResources.AppTitle, AppResources.ReminderGone, AppResources.Ok);

		await Shell.Current.GoToAsync("..");
		CaptureRequests.End();
	}

	[RelayCommand]
	async Task CancelAsync()
	{
		StopAutoSave();
		await Shell.Current.GoToAsync("..");
		CaptureRequests.End();
	}

	partial void OnTextChanged(string value) => OnUserChange();

	partial void OnDateChanged(DateTime? value) => OnUserChange();

	partial void OnTimeChanged(TimeSpan? value) => OnUserChange();

	void OnUserChange()
	{
		StopAutoSave();
		UpdateSummary();
	}

	void UpdateSummary() => Summary = SummaryIn(CultureInfo.CurrentUICulture);

	// On screen in the app's language; spoken in the voice's, which may differ.
	string SummaryIn(CultureInfo culture) =>
		string.Format(culture, ReminderTimeFormatter.Text(nameof(AppResources.ConfirmMessage), culture),
			Text.Trim(), formatter.Format(scheduler.FirstAlertAt(RequestedAt()), culture));

	DateTimeOffset? RequestedAt()
	{
		if (Date is not { } date || Time is not { } timeOfDay) return originalRequest;

		var picked = new TimeSpan(timeOfDay.Hours, timeOfDay.Minutes, 0);
		if (date.Date == initialDate && picked == initialTime) return originalRequest;

		return LocalTime.At(time.LocalTimeZone, DateOnly.FromDateTime(date), TimeOnly.FromTimeSpan(picked));
	}
}
