using System.Diagnostics;
using System.Globalization;
using Acordater.App.Interpretation;
using Acordater.App.Resources.Strings;
using Acordater.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Acordater.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
	readonly QuietHoursSettings settings;
	readonly AiSettings ai;
	readonly ReminderScheduler scheduler;
	readonly ReminderTimeFormatter formatter;
	readonly TimeProvider time;

	// Edited in memory while switching providers; written on Save.
	readonly Dictionary<AiProvider, string> apiKeys = [];
	readonly Dictionary<AiProvider, string> models = [];
	bool loaded;

	public SettingsViewModel(
		QuietHoursSettings settings,
		AiSettings ai,
		ReminderScheduler scheduler,
		ReminderTimeFormatter formatter,
		TimeProvider time)
	{
		this.settings = settings;
		this.ai = ai;
		this.scheduler = scheduler;
		this.formatter = formatter;
		this.time = time;

		var quietHours = settings.Current;
		QuietStart = quietHours.Start.ToTimeSpan();
		QuietEnd = quietHours.End.ToTimeSpan();
		IsFirstRun = !settings.IsConfigured;
		ProviderNames = AiProviders.All.Select(p => p == AiProvider.None ? AppResources.AiProviderNone : AiProviders.DisplayName(p)).ToList();
	}

	[ObservableProperty]
	public partial TimeSpan? QuietStart { get; set; }

	[ObservableProperty]
	public partial TimeSpan? QuietEnd { get; set; }

	public bool IsFirstRun { get; }

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

	[RelayCommand]
	async Task SaveAsync()
	{
		settings.Save(new QuietHours(TimeOnly.FromTimeSpan(QuietStart ?? default), TimeOnly.FromTimeSpan(QuietEnd ?? default)));

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

		await Shell.Current.GoToAsync("..");
	}
}
