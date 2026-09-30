using System.Globalization;
using Acordater.App.Resources.Strings;
using Acordater.App.Voice;
using Android.Content;
using Android.OS;
using Android.Speech;

namespace Acordater.App;

/// <summary>
/// Speech to text with Android's SpeechRecognizer, on the device when its language model is installed.
/// When it is missing, its download is requested and that attempt falls back to the default recognition service.
/// The language comes from <see cref="LanguageSettings"/>; in bilingual mode the recognizer switches between Spanish
/// and English as it hears them (Android 14+), and if that fails it listens again in the main language only.
/// Cancelling stops listening and returns what was heard so far.
/// </summary>
public sealed class AndroidSpeechRecognizer(LanguageSettings language) : ISpeechRecognizer
{
	const string LogTag = "Acordater";

	// If the recognizer never reports back after being stopped, give up instead of hanging.
	static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

	public Task<string?> ListenAsync(Action<string>? partial, CancellationToken cancellationToken) =>
		MainThread.InvokeOnMainThreadAsync(() => ListenOnMainThreadAsync(partial, cancellationToken));

	async Task<string?> ListenOnMainThreadAsync(Action<string>? partial, CancellationToken cancellationToken)
	{
		if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
			throw new SpeechUnavailableException(AppResources.MicrophoneDenied);

		var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
		var switchLanguages = language.DictationSwitchLanguages;
		var intent = RecognizeIntent(language.DictationLanguage, switchLanguages);

		Outcome? outcome = null;
		if (SpeechRecognizer.IsOnDeviceRecognitionAvailable(context))
		{
			outcome = await ListenOnceAsync(context, onDevice: true, intent, partial, cancellationToken);
			if (switchLanguages is not null && IsFailure(outcome.Error) && !cancellationToken.IsCancellationRequested)
			{
				// Language switching needs both language models on the device; listen again in one language.
				intent = RecognizeIntent(language.DictationLanguage, switchLanguages: null);
				outcome = await ListenOnceAsync(context, onDevice: true, intent, partial, cancellationToken);
			}
		}

		if (outcome is null || IsLanguageMissing(outcome.Error))
		{
			if (!SpeechRecognizer.IsRecognitionAvailable(context))
				throw new SpeechUnavailableException(AppResources.SpeechUnavailable);
			outcome = await ListenOnceAsync(context, onDevice: false, intent, partial, cancellationToken);
		}

		return outcome.Error switch
		{
			null => outcome.Text,
			SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout or SpeechRecognizerError.Client => null,
			SpeechRecognizerError.InsufficientPermissions => throw new SpeechUnavailableException(AppResources.MicrophoneDenied),
			var error when IsLanguageMissing(error) => throw new SpeechUnavailableException(AppResources.SpeechUnavailable),
			var error => throw new SpeechUnavailableException(string.Format(CultureInfo.CurrentCulture, AppResources.SpeechFailed, error)),
		};
	}

	static async Task<Outcome> ListenOnceAsync(Context context, bool onDevice, Intent intent, Action<string>? partial, CancellationToken cancellationToken)
	{
		var recognizer = onDevice
			? SpeechRecognizer.CreateOnDeviceSpeechRecognizer(context)
			: SpeechRecognizer.CreateSpeechRecognizer(context)!;
		var listener = new Listener(partial);
		recognizer.SetRecognitionListener(listener);

		using var stop = cancellationToken.Register(() => MainThread.BeginInvokeOnMainThread(async () =>
		{
			if (listener.Completion.IsCompleted) return; // already destroyed
			recognizer.StopListening();
			await Task.Delay(StopTimeout);
			listener.Complete(new Outcome(null, SpeechRecognizerError.Client));
		}));
		try
		{
			recognizer.StartListening(intent);
			var outcome = await listener.Completion;
			global::Android.Util.Log.Info(LogTag, $"Speech ({(onDevice ? "on device" : "default service")}): {outcome.Error?.ToString() ?? "ok"}");

			if (onDevice && IsLanguageMissing(outcome.Error))
				recognizer.TriggerModelDownload(intent); // for next time
			return outcome;
		}
		finally
		{
			recognizer.Destroy();
		}
	}

	static Intent RecognizeIntent(string language, IReadOnlyList<string>? switchLanguages)
	{
		var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
		intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
		intent.PutExtra(RecognizerIntent.ExtraLanguage, language);
		intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
		intent.PutExtra(RecognizerIntent.ExtraPreferOffline, true);
		if (switchLanguages is not null)
		{
			var allowed = new List<string>(switchLanguages);
			intent.PutExtra(RecognizerIntent.ExtraEnableLanguageDetection, true);
			intent.PutStringArrayListExtra(RecognizerIntent.ExtraLanguageDetectionAllowedLanguages, allowed);
			intent.PutExtra(RecognizerIntent.ExtraEnableLanguageSwitch, RecognizerIntent.LanguageSwitchBalanced);
			intent.PutStringArrayListExtra(RecognizerIntent.ExtraLanguageSwitchAllowedLanguages, allowed);
		}
		return intent;
	}

	// Errors worth a second attempt (not "heard nothing" or "stopped").
	static bool IsFailure(SpeechRecognizerError? error) =>
		error is not null and not (SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout or SpeechRecognizerError.Client);

	static bool IsLanguageMissing(SpeechRecognizerError? error) =>
		error is SpeechRecognizerError.LanguageNotSupported or SpeechRecognizerError.LanguageUnavailable;

	sealed record Outcome(string? Text, SpeechRecognizerError? Error);

	/// <summary>Callbacks arrive on the main thread.</summary>
	sealed class Listener(Action<string>? partial) : Java.Lang.Object, IRecognitionListener
	{
		readonly TaskCompletionSource<Outcome> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<Outcome> Completion => completion.Task;

		public void Complete(Outcome outcome) => completion.TrySetResult(outcome);

		public void OnResults(Bundle? results) => Complete(new Outcome(BestResult(results), null));

		public void OnError(SpeechRecognizerError error) => Complete(new Outcome(null, error));

		public void OnPartialResults(Bundle? partialResults)
		{
			if (BestResult(partialResults) is { } text) partial?.Invoke(text);
		}

		public void OnBeginningOfSpeech() { }

		public void OnBufferReceived(byte[]? buffer) { }

		public void OnEndOfSpeech() { }

		public void OnEvent(int eventType, Bundle? @params) { }

		public void OnReadyForSpeech(Bundle? @params) { }

		public void OnRmsChanged(float rmsdB) { }

		static string? BestResult(Bundle? bundle) =>
			bundle?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
	}
}
