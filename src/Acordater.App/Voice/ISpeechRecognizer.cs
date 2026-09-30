namespace Acordater.App.Voice;

/// <summary>Speech to text on the device (Android: SpeechRecognizer).</summary>
public interface ISpeechRecognizer
{
	/// <summary>
	/// Listens for one phrase in the current UI language. Returns null when nothing was understood or it was cancelled.
	/// </summary>
	/// <param name="partial">Receives the text heard so far, on the main thread.</param>
	/// <exception cref="SpeechUnavailableException">No microphone permission or no recognizer; the message is user-facing.</exception>
	Task<string?> ListenAsync(Action<string>? partial, CancellationToken cancellationToken);
}

public sealed class SpeechUnavailableException(string message) : Exception(message);
