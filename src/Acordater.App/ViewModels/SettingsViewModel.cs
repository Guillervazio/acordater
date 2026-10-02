using System.Diagnostics;
using System.Globalization;
using Acordater.App.Interpretation;
using Acordater.App.Resources.Strings;
using Acordater.App.Voice;
using Acordater.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Acordater.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
	readonly QuietHoursSettings settings;
	readonly LanguageSettings language;
	readonly AiSettings ai;
	readonly WakeWordSettings wakeWordSettings;
	readonly IWakeWordDetector wakeWord;
	readonly ReminderScheduler scheduler;
	readonly ReminderTimeFormatter formatter;
	readonly TimeProvider time;

	// Edited in memory while switching providers; written on Save.
	readonly Dictionary<AiProvider, string> apiKeys = [];
	readonly Dictionary<AiProvider, string> models = [];
	bool loaded;
	bool changingWakeWord;

	public SettingsViewModel(
		QuietHoursSettings settings,
		LanguageSettings language,
		AiSettings ai,
		WakeWordSettings wakeWordSettings,
		IWakeWordDetector wakeWord,
		ReminderScheduler scheduler,
		ReminderTimeFormatter formatter,
		TimeProvider time)
	{
		this.settings = settings;
		this.language = language;
		this.ai = ai;
		this.wakeWordSettings = wakeWordSettings;
		this.wakeWord = wakeWord;
		this.scheduler = scheduler;
		this.formatter = formatter;
		this.time = time;

		var quietHours = settings.Current;
		QuietStart = quietHours.Start.ToTimeSpan();
		QuietEnd = quietHours.End.ToTimeSpan();
		IsFirstRun = !settings.IsConfigured;
		LanguageNames = LanguageSettings.All.Select(l => l switch
		{
			AppLanguage.Spanish => AppResources.LanguageSpanish,
			AppLanguage.English => AppResources.LanguageEnglish,
			AppLanguage.Bilingual => AppResources.LanguageBilingual,
			_ => AppResources.LanguagePhone,
		}).ToList();
		SelectedLanguageIndex = LanguageSettings.All.ToList().IndexOf(language.Language);
		VoiceNames = LanguageSettings.AllVoices.Select(v => v switch
		{
			VoiceLanguage.Spanish => AppResources.LanguageSpanish,
			VoiceLanguage.English => AppResources.LanguageEnglish,
			_ => AppResources.VoiceSameAsApp,
		}).ToList();
		SelectedVoiceIndex = LanguageSettings.AllVoices.ToList().IndexOf(language.Voice);
		ProviderNames = AiProviders.All.Select(p => p == AiProvider.None ? AppResources.AiProviderNone : AiProviders.DisplayName(p)).ToList();
	}

	[ObservableProperty]
	public partial TimeSpan? QuietStart { get; set; }

	[ObservableProperty]
	public partial TimeSpan? QuietEnd { get; set; }

	public bool IsFirstRun { get; }

	// Language (docs/spec.md, section 4.5).

	public IReadOnlyList<string> LanguageNames { get; }

	[ObservableProperty]
	public partial int SelectedLanguageIndex { get; set; }

	public IReadOnlyList<string> VoiceNames { get; }

	[ObservableProperty]
	public partial int SelectedVoiceIndex { get; set; }

	// AI interpretation (docs/spec.md, section 5).

	public IReadOnlyList<string> ProviderNames { get; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsAiEnabled))]
	public partial int SelectedProviderIndex { get; set; } = -1;

	AiProvider SelectedProvider => SelectedProviderIndex >= 0 ? AiProviders.All[SelectedProviderIndex] : AiProvider.None;

	public bool IsAiEnabled => SelectedProvider != AiProvider.None;

	[ObservableProperty]
	public partial string ApiKey { get; set; } = "";

	[ObservableProperty]
	public partial string Model { get; set; } = "";

	[ObservableProperty]
	public partial string ModelPlaceholder { get; set; } = "";

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(TestAiCommand))]
	public partial bool IsTesting { get; set; }

	[ObservableProperty]
	public partial string TestResult { get; set; } = "";

	// Wake word (docs/spec.md, section 4.7).

	[ObservableProperty]
	public partial bool WakeWordEnabled { get; set; }

	[ObservableProperty]
	public partial string WakeWordModel { get; set; } = "";

	[ObservableProperty]
	public partial bool HasCustomWakeWord { get; set; }

	[ObservableProperty]
	public partial string WakeWordStatus { get; set; } = "";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(WakeWordThresholdText))]
	public partial double WakeWordThreshold { get; set; }

	public string WakeWordThresholdText =>
		string.Format(CultureInfo.CurrentCulture, AppResources.WakeWordThreshold, WakeWordThreshold);

	/// <summary>Called when the page appears: secrets come from SecureStorage, which is async.</summary>
	public async Task LoadAsync()
	{
		if (loaded) return;

		foreach (var provider in AiProviders.All.Where(p => p != AiProvider.None))
		{
			apiKeys[provider] = await ai.GetApiKeyAsync(provider);
			models[provider] = ai.GetModel(provider);
		}
		loaded = true;

		SelectedProviderIndex = Math.Max(0, AiProviders.All.ToList().IndexOf(ai.Provider));
		changingWakeWord = true;
		WakeWordEnabled = wakeWordSettings.Enabled;
		changingWakeWord = false;
		WakeWordThreshold = wakeWordSettings.Threshold;
		UpdateWakeWordInfo();
	}

	partial void OnSelectedProviderIndexChanging(int oldValue, int newValue)
	{
		// Keep what was typed for the previous provider.
		if (loaded && oldValue >= 0 && AiProviders.All[oldValue] is var previous and not AiProvider.None)
		{
			apiKeys[previous] = ApiKey;
			models[previous] = Model;
		}
	}

	partial void OnSelectedProviderIndexChanged(int value)
	{
		var provider = SelectedProvider;
		ApiKey = apiKeys.GetValueOrDefault(provider, "");
		Model = models.GetValueOrDefault(provider, "");
		ModelPlaceholder = AiProviders.DefaultModel(provider);
		TestResult = "";
	}

	bool CanTestAi() => !IsTesting;

	/// <summary>Interprets a sample phrase with what is typed (saved or not) and shows the result or the error.</summary>
	[RelayCommand(CanExecute = nameof(CanTestAi))]
	async Task TestAiAsync()
	{
		var provider = SelectedProvider;
		if (provider == AiProvider.None) return;
		if (string.IsNullOrWhiteSpace(ApiKey))
		{
			TestResult = AppResources.AiKeyMissing;
			return;
		}

		IsTesting = true;
		TestResult = AppResources.AiTestRunning;
		try
		{
			var interpreter = AiProviders.CreateInterpreter(provider, ApiKey.Trim(), Model, time)!;
			var stopwatch = Stopwatch.StartNew();
			var result = await interpreter.InterpretAsync(AppResources.AiTestSample);
			var seconds = stopwatch.Elapsed.TotalSeconds;

			TestResult = result.Failure is { } failure
				? string.Format(CultureInfo.CurrentCulture, AppResources.AiTestFailed, InterpreterDescription.Reason(failure.Kind), failure.Detail)
				: string.Format(CultureInfo.CurrentCulture, AppResources.AiTestOk, seconds, result.Text,
					formatter.Format(scheduler.FirstAlertAt(result.RequestedAt)));
		}
		catch (Exception ex)
		{
			// E.g. a malformed key or model the SDK rejects before sending anything.
			TestResult = string.Format(CultureInfo.CurrentCulture, AppResources.AiTestFailed, AppResources.FailureProvider, ex.Message);
		}
		finally
		{
			IsTesting = false;
		}
	}

	async partial void OnWakeWordEnabledChanged(bool value)
	{
		if (changingWakeWord) return;

		if (!value)
		{
			wakeWordSettings.Enabled = false;
			wakeWord.Stop();
			UpdateWakeWordInfo();
			return;
		}

		WakeWordStatus = AppResources.WakeWordStarting;
		try
		{
			await wakeWord.StartAsync();
			wakeWordSettings.Enabled = true;
			UpdateWakeWordInfo();
		}
		catch (WakeWordUnavailableException ex)
		{
			wakeWordSettings.Enabled = false;
			SetWakeWordSwitch(false);
			WakeWordStatus = ex.Message;
		}
	}

	// The slider moves freely; the value snaps to steps of 0.05.
	partial void OnWakeWordThresholdChanged(double value)
	{
		var snapped = Math.Round(value * 20) / 20;
		if (snapped != value) WakeWordThreshold = snapped;
	}

	/// <summary>When the slider is released: saves the threshold and applies it to a running detector.</summary>
	[RelayCommand]
	async Task ApplyWakeWordThresholdAsync()
	{
		if ((float)WakeWordThreshold == wakeWordSettings.Threshold) return;

		wakeWordSettings.Threshold = (float)WakeWordThreshold;
		await RestartWakeWordAsync();
		UpdateWakeWordInfo();
	}

	[RelayCommand]
	Task ImportWakeWordModelAsync() =>
		ImportAsync(".onnx", AppResources.WakeWordImportModel, wakeWordSettings.ImportKeywordAsync);

	[RelayCommand]
	async Task RemoveWakeWordModelAsync()
	{
		wakeWordSettings.RemoveCustomModel();
		await RestartWakeWordAsync();
		UpdateWakeWordInfo();
	}

	async Task ImportAsync(string extension, string title, Func<Stream, string, Task> import)
	{
		// .onnx has no standard MIME type, so any file can be picked and the extension is checked here.
		var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = title });
		if (file is null) return;

		if (!file.FileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
		{
			WakeWordStatus = string.Format(CultureInfo.CurrentCulture, AppResources.WakeWordWrongFile, extension);
			return;
		}

		await using (var stream = await file.OpenReadAsync())
			await import(stream, file.FileName);
		await RestartWakeWordAsync();
		UpdateWakeWordInfo();
	}

	/// <summary>Applies a new model to a running detector.</summary>
	async Task RestartWakeWordAsync()
	{
		if (!wakeWord.IsRunning) return;

		wakeWord.Stop();
		try
		{
			await wakeWord.StartAsync();
		}
		catch (WakeWordUnavailableException ex)
		{
			wakeWordSettings.Enabled = false;
			SetWakeWordSwitch(false);
			WakeWordStatus = ex.Message;
		}
	}

	void SetWakeWordSwitch(bool value)
	{
		changingWakeWord = true;
		WakeWordEnabled = value;
		changingWakeWord = false;
	}

	void UpdateWakeWordInfo()
	{
		var phrase = wakeWordSettings.Phrase;
		HasCustomWakeWord = wakeWordSettings.HasCustomKeyword;
		WakeWordModel = wakeWordSettings.HasCustomKeyword
			? string.Format(CultureInfo.CurrentCulture, AppResources.WakeWordModelCustom, phrase)
			: string.Format(CultureInfo.CurrentCulture, AppResources.WakeWordModelBuiltIn, phrase);
		WakeWordStatus = wakeWord.IsRunning
			? string.Format(CultureInfo.CurrentCulture, AppResources.WakeWordActive, phrase)
			: wakeWordSettings.Enabled && wakeWord.LastError is { } error ? error : AppResources.WakeWordInactive;
	}

	[RelayCommand]
	async Task SaveAsync()
	{
		settings.Save(new QuietHours(TimeOnly.FromTimeSpan(QuietStart ?? default), TimeOnly.FromTimeSpan(QuietEnd ?? default)));

		language.Voice = LanguageSettings.AllVoices[Math.Max(0, SelectedVoiceIndex)];
		var chosenLanguage = LanguageSettings.All[Math.Max(0, SelectedLanguageIndex)];
		var languageChanged = chosenLanguage != language.Language;
		if (languageChanged)
		{
			language.Language = chosenLanguage;
			language.Apply();
		}

		if (loaded)
		{
			if (SelectedProvider != AiProvider.None)
			{
				apiKeys[SelectedProvider] = ApiKey;
				models[SelectedProvider] = Model;
			}
			foreach (var (provider, apiKey) in apiKeys)
				await ai.SetApiKeyAsync(provider, apiKey);
			foreach (var (provider, model) in models)
				ai.SetModel(provider, model);
			ai.Provider = SelectedProvider;
		}

		// Pages already built keep their texts; dictation, speech and the AI use the new language right away.
		if (languageChanged)
			await Shell.Current.DisplayAlertAsync(AppResources.LanguageTitle, AppResources.LanguageRestart, AppResources.Ok);

		await Shell.Current.GoToAsync("..");
	}
}
