namespace Acordater.App.Voice;

/// <summary>
/// Requests to start voice capture as soon as the main page is shown, coming from the home screen widget
/// or the quick settings tile. A request made before the page exists (cold start) waits until it appears.
/// </summary>
public static class CaptureRequests
{
	static bool pending;

	public static event Action? Raised;

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
}
