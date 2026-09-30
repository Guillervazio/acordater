namespace Acordater.App.Voice;

/// <summary>
/// Listens for the wake word in the background, also with the phone locked, and then starts voice capture
/// like the widget does (Android: Porcupine in a microphone foreground service). See docs/spec.md, section 4.7.
/// </summary>
public interface IWakeWordDetector
{
	bool IsRunning { get; }

	/// <summary>Why it last stopped by itself (user-facing), or null.</summary>
	string? LastError { get; }

	/// <summary>Starts listening. Only possible while the app is in the foreground.</summary>
	/// <exception cref="WakeWordUnavailableException">It could not start; the message is user-facing.</exception>
	Task StartAsync(CancellationToken cancellationToken = default);

	void Stop();

	/// <summary>Releases the microphone until the returned handle is disposed (e.g. while the app itself listens).</summary>
	IDisposable Pause();
}

public sealed class WakeWordUnavailableException(string message) : Exception(message);
