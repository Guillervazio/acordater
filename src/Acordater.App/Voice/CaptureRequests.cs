namespace Acordater.App.Voice;

/// <summary>
/// Requests to start voice capture as soon as the main page is shown, coming from the home screen widget,
/// the quick settings tile or the wake word. A request made before the page exists (cold start) waits until it appears.
/// </summary>
public static class CaptureRequests
{
	static bool pending;

	public static event Action? Raised;

	/// <summary>A capture finished (saved, cancelled or nothing heard), e.g. to leave the lock screen again.</summary>
	public static event Action? Ended;

	public static void Raise()
	{
		pending = true;
		Raised?.Invoke();
	}

	public static bool TryTake()
	{
		var wasPending = pending;
		pending = false;
		return wasPending;
	}

	public static void End() => Ended?.Invoke();
}
