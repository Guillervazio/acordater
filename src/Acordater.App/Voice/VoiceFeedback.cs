using System.Collections.Concurrent;
using System.Globalization;
using Android.Speech.Tts;
using TextToSpeech = Android.Speech.Tts.TextToSpeech;

namespace Acordater.App.Voice;

/// <summary>
/// Speaks confirmations in the language chosen for Acordater's voice (<see cref="LanguageSettings.VoiceCulture"/>).
/// Uses Android's text to speech directly, like the alarm: MAUI's wrapper stayed silent for Spanish on a Pixel.
/// </summary>
public sealed class VoiceFeedback
{
	const string LogTag = "Acordater";
	// Guards against an engine that never reports the end of an utterance.
	static readonly TimeSpan MaxUtterance = TimeSpan.FromSeconds(30);

	readonly Engine engine = new();

	/// <param name="culture">Language of <paramref name="text"/>, which selects the voice.</param>
	public async Task SpeakAsync(string text, CultureInfo culture, CancellationToken cancellationToken)
	{
		try
		{
			await MainThread.InvokeOnMainThreadAsync(() => engine.SpeakAsync(text, culture, cancellationToken))
				.WaitAsync(MaxUtterance, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The text is on screen anyway; a missing or broken TTS engine must not block saving.
			global::Android.Util.Log.Warn(LogTag, $"TTS failed: {ex}");
		}
	}

	/// <summary>One engine for the app's lifetime, created on first use. Main thread only.</summary>
	sealed class Engine : UtteranceProgressListener, TextToSpeech.IOnInitListener
	{
		readonly TaskCompletionSource<bool> initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
		readonly ConcurrentDictionary<string, TaskCompletionSource> utterances = new();
		TextToSpeech? speech;
		string? language;

		public async Task SpeakAsync(string text, CultureInfo culture, CancellationToken cancellationToken)
		{
			speech ??= new TextToSpeech(global::Android.App.Application.Context, this);
			if (!await initialized.Task || speech is null) return;

			if (language != culture.Name)
			{
				var result = speech.SetLanguage(Java.Util.Locale.ForLanguageTag(culture.Name));
				global::Android.Util.Log.Info(LogTag, $"TTS language {culture.Name}: {result}");
				language = culture.Name;
			}

			var id = Guid.NewGuid().ToString("N");
			var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			utterances[id] = done;
			try
			{
				if (speech.Speak(text, QueueMode.Flush, null, id) != OperationResult.Success)
				{
					global::Android.Util.Log.Warn(LogTag, "TTS could not queue the text");
					return;
				}
				using (cancellationToken.Register(() => MainThread.BeginInvokeOnMainThread(() => speech?.Stop())))
					await done.Task.WaitAsync(cancellationToken);
			}
			finally
			{
				utterances.TryRemove(id, out _);
			}
		}

		public void OnInit(OperationResult status)
		{
			if (status != OperationResult.Success)
			{
				global::Android.Util.Log.Warn(LogTag, $"Text to speech unavailable: {status}");
				speech?.Shutdown();
				speech = null;
			}
			else
			{
				speech!.SetOnUtteranceProgressListener(this);
			}
			initialized.TrySetResult(status == OperationResult.Success);
		}

		public override void OnStart(string? utteranceId) { }

		public override void OnDone(string? utteranceId) => Finish(utteranceId);

		[Obsolete("Required override; the overload with an error code is used on current Android versions.")]
		public override void OnError(string? utteranceId) => Finish(utteranceId);

		public override void OnError(string? utteranceId, TextToSpeechError errorCode)
		{
			global::Android.Util.Log.Warn(LogTag, $"TTS error: {errorCode}");
			Finish(utteranceId);
		}

		public override void OnStop(string? utteranceId, bool interrupted) => Finish(utteranceId);

		void Finish(string? utteranceId)
		{
			if (utteranceId is not null && utterances.TryRemove(utteranceId, out var done)) done.TrySetResult();
		}
	}
}
