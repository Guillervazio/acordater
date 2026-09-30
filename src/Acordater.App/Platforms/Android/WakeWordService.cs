using Acordater.App.Resources.Strings;
using Acordater.App.Voice;
using Acordater.Core.WakeWord;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Media;
using Android.OS;

namespace Acordater.App;

/// <summary>
/// Listens for the wake word with openWakeWord (<see cref="OpenWakeWordDetector"/>) in a microphone foreground service,
/// with a permanent notification (docs/spec.md, section 4.7). On detection it opens voice capture like the widget does
/// (<see cref="CaptureIntents"/>). The microphone is released while paused (<see cref="Pause"/>): while the app itself
/// listens and while an alarm rings.
/// Android 14+ only lets a microphone foreground service start while the app is in the foreground, so it is started
/// from the app (Settings or opening the app), never from boot; when that is not possible the user gets a
/// notification to reactivate it.
/// </summary>
[Service(Name = "com.acordater.app.WakeWordService", Exported = false, ForegroundServiceType = ForegroundService.TypeMicrophone)]
public sealed class WakeWordService : Service
{
	// Channel settings are immutable once created; bump the id to change them.
	const string ChannelId = "wake_word_v1";
	const int NotificationId = 2;
	const int StoppedNotificationId = 4;
	const string ActionStop = "com.acordater.app.action.STOP_WAKE_WORD";
	const string LogTag = "Acordater";

	// After a detection the microphone stays free for the capture, even if the user never opens it.
	static readonly TimeSpan DetectionPause = TimeSpan.FromSeconds(30);
	// Loading the models takes well under a second; this only guards against a hang.
	static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);

	static readonly Lock gate = new();
	static WakeWordService? instance;
	static int pauses;
	static TaskCompletionSource<string?>? starting;

	readonly Handler handler = new(Looper.MainLooper!);
	OpenWakeWordDetector? detector;
	Capture? capture;
	IDisposable? detectionPause;
	bool destroyed;

	public static bool IsRunning
	{
		get { lock (gate) return instance is not null; }
	}

	/// <summary>Last reason it stopped by itself, for Settings; null after a successful start.</summary>
	public static string? LastError { get; private set; }

	/// <summary>Starts (or confirms) listening. Returns null on success or a user-facing error.</summary>
	public static async Task<string?> StartAsync(Context context)
	{
		TaskCompletionSource<string?> started;
		lock (gate) started = starting ??= new(TaskCreationOptions.RunContinuationsAsynchronously);

		try
		{
			context.StartForegroundService(new Intent(context, typeof(WakeWordService)));
		}
		catch (ForegroundServiceStartNotAllowedException)
		{
			Complete(AppResources.WakeWordBackgroundStart);
		}

		var done = await Task.WhenAny(started.Task, Task.Delay(StartTimeout));
		if (done == started.Task) return await started.Task;
		Complete(AppResources.WakeWordStartTimeout);
		return AppResources.WakeWordStartTimeout;
	}

	public static void Stop(Context context)
	{
		context.StopService(new Intent(context, typeof(WakeWordService)));
		Complete(AppResources.WakeWordStopped);
	}

	/// <summary>Releases the microphone until the returned handle is disposed. Pauses nest.</summary>
	public static IDisposable Pause()
	{
		lock (gate) pauses++;
		Refresh();
		return new PauseHandle();
	}

	/// <summary>Tells the user the wake word is off and how to turn it back on (tap opens the app, which restarts it).</summary>
	public static void NotifyStopped(Context context, string reason)
	{
		var open = PendingIntent.GetActivity(context, 2,
			new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop),
			PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
		var notification = new Notification.Builder(context, ChannelId)
			.SetSmallIcon(Icon.CreateWithResource(context, Resource.Drawable.ic_mic))!
			.SetContentTitle(AppResources.WakeWordStoppedTitle)!
			.SetContentText(reason)!
			.SetStyle(new Notification.BigTextStyle().BigText(reason))!
			.SetContentIntent(open)!
			.SetAutoCancel(true)!
			.Build();
		Notifications(context).Notify(StoppedNotificationId, notification);
	}

	public override IBinder? OnBind(Intent? intent) => null;

	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		if (intent?.Action == ActionStop)
		{
			// The user turned it off from the notification: it stays off until turned on again in Settings.
			Services.GetRequiredService<WakeWordSettings>().Enabled = false;
			StopSelf();
			Complete(AppResources.WakeWordStopped);
			return StartCommandResult.NotSticky;
		}

		try
		{
			StartForeground(NotificationId, BuildNotification(), ForegroundService.TypeMicrophone);
		}
		catch (Exception ex) when (ex is ForegroundServiceStartNotAllowedException or Java.Lang.SecurityException)
		{
			// Android 14+: not while the app is in the background (e.g. a restart by the system), or no microphone permission.
			global::Android.Util.Log.Warn(LogTag, $"Wake word service could not start in the foreground: {ex.GetType().Name}");
			Fail(AppResources.WakeWordBackgroundStart);
			return StartCommandResult.NotSticky;
		}

		lock (gate) instance = this;
		Notifications(this).Cancel(StoppedNotificationId);
		if (detector is null)
			_ = InitializeAsync();
		else
			Complete(null);
		return StartCommandResult.Sticky;
	}

	public override void OnDestroy()
	{
		destroyed = true;
		lock (gate)
		{
			if (instance == this) instance = null;
		}
		handler.RemoveCallbacksAndMessages(null);
		ReleaseDetectionPause(); // pauses are static: a pending one would keep the next start silent
		StopCapture();
		detector?.Dispose();
		detector = null;
		base.OnDestroy();
	}

	async Task InitializeAsync()
	{
		var settings = Services.GetRequiredService<WakeWordSettings>();
		try
		{
			var melspectrogram = await ReadPackageFileAsync(WakeWordSettings.MelspectrogramModel);
			var embedding = await ReadPackageFileAsync(WakeWordSettings.EmbeddingModel);
			var keyword = settings.HasCustomKeyword
				? await File.ReadAllBytesAsync(WakeWordSettings.KeywordFile)
				: await ReadPackageFileAsync(WakeWordSettings.BuiltInKeywordModel);
			var loaded = await Task.Run(() => new OpenWakeWordDetector(melspectrogram, embedding, keyword));

			if (destroyed)
			{
				loaded.Dispose();
				return;
			}
			detector = loaded;
			LastError = null;
			UpdateListening();
			if (!destroyed) Complete(null);
		}
		catch (Exception ex) when (ex is Microsoft.ML.OnnxRuntime.OnnxRuntimeException or IOException)
		{
			global::Android.Util.Log.Warn(LogTag, $"Wake word model failed to load: {ex}");
			Fail(AppResources.WakeWordInvalidModel);
		}
	}

	static async Task<byte[]> ReadPackageFileAsync(string name)
	{
		await using var stream = await FileSystem.OpenAppPackageFileAsync(name);
		using var memory = new MemoryStream();
		await stream.CopyToAsync(memory);
		return memory.ToArray();
	}

	/// <summary>Starts or stops the microphone to match the pauses. Main thread only.</summary>
	void UpdateListening()
	{
		if (detector is null || destroyed) return;

		bool shouldListen;
		lock (gate) shouldListen = pauses == 0;
		if (shouldListen == capture is not null) return;

		if (shouldListen) StartCapture(detector);
		else StopCapture();
	}

	void StartCapture(OpenWakeWordDetector detector)
	{
		var minBuffer = AudioRecord.GetMinBufferSize(OpenWakeWordDetector.SampleRate, ChannelIn.Mono, Encoding.Pcm16bit);
		// Room for ~0.6 s: waking up from silence processes a few chunks at once.
		var record = new AudioRecord(AudioSource.VoiceRecognition, OpenWakeWordDetector.SampleRate, ChannelIn.Mono, Encoding.Pcm16bit,
			Math.Max(minBuffer, OpenWakeWordDetector.ChunkSamples * 2 * 8));
		if (record.State != State.Initialized)
		{
			record.Release();
			Fail(AppResources.WakeWordMicrophoneUnavailable);
			return;
		}

		detector.Reset();
		record.StartRecording();
		capture = new Capture(record, detector, this);
	}

	void StopCapture()
	{
		capture?.Stop();
		capture = null;
	}

	void OnDetected()
	{
		if (destroyed || detectionPause is not null) return; // already handling a detection

		global::Android.Util.Log.Info(LogTag, "Wake word detected");
		detectionPause = Pause();
		handler.PostDelayed(ReleaseDetectionPause, (long)DetectionPause.TotalMilliseconds);
		CaptureIntents.OnWakeWord(this);
	}

	void ReleaseDetectionPause()
	{
		detectionPause?.Dispose();
		detectionPause = null;
	}

	/// <summary>Stops by itself: reports to a pending start, or else notifies the user.</summary>
	void Fail(string reason)
	{
		global::Android.Util.Log.Warn(LogTag, $"Wake word stopped: {reason}");
		LastError = reason;
		bool hadPendingStart;
		lock (gate) hadPendingStart = starting is not null;
		if (!hadPendingStart) NotifyStopped(this, reason);
		Complete(reason);

		StopCapture();
		StopForeground(StopForegroundFlags.Remove);
		StopSelf();
	}

	Notification BuildNotification()
	{
		EnsureChannel();
		var phrase = Services.GetRequiredService<WakeWordSettings>().Phrase;
		var stop = PendingIntent.GetService(this, 0,
			new Intent(this, typeof(WakeWordService)).SetAction(ActionStop),
			PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
		var icon = Icon.CreateWithResource(this, Resource.Drawable.ic_mic);

		return new Notification.Builder(this, ChannelId)
			.SetSmallIcon(icon)!
			.SetContentTitle(string.Format(System.Globalization.CultureInfo.CurrentCulture, AppResources.WakeWordNotificationTitle, phrase))!
			.SetContentText(string.Format(System.Globalization.CultureInfo.CurrentCulture, AppResources.WakeWordNotificationText, phrase))!
			.SetContentIntent(ReminderIntents.OpenApp(this))!
			.SetOngoing(true)!
			.SetForegroundServiceBehavior((int)NotificationForegroundService.Immediate)!
			.AddAction(new Notification.Action.Builder(icon, AppResources.WakeWordTurnOff, stop).Build())!
			.Build();
	}

	void EnsureChannel()
	{
		var channel = new NotificationChannel(ChannelId, AppResources.WakeWordChannel, NotificationImportance.Low);
		channel.SetShowBadge(false);
		Notifications(this).CreateNotificationChannel(channel);
	}

	static void Complete(string? error)
	{
		TaskCompletionSource<string?>? pending;
		lock (gate)
		{
			pending = starting;
			starting = null;
		}
		pending?.TrySetResult(error);
	}

	static void Refresh()
	{
		WakeWordService? service;
		lock (gate) service = instance;
		if (service is null) return;

		// Synchronously when possible, so that the microphone is free before the caller starts using it.
		if (Looper.MyLooper() == Looper.MainLooper) service.UpdateListening();
		else service.handler.Post(service.UpdateListening);
	}

	static IServiceProvider Services => IPlatformApplication.Current!.Services;

	static NotificationManager Notifications(Context context) => (NotificationManager)context.GetSystemService(NotificationService)!;

	sealed class PauseHandle : IDisposable
	{
		int disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 1) return;
			lock (gate) pauses--;
			Refresh();
		}
	}

	/// <summary>Reads the microphone on its own thread and feeds the detector, 80 ms at a time.</summary>
	sealed class Capture
	{
		readonly AudioRecord record;
		readonly OpenWakeWordDetector detector;
		readonly WakeWordService service;
		readonly Thread thread;
		volatile bool stopping;

		public Capture(AudioRecord record, OpenWakeWordDetector detector, WakeWordService service)
		{
			this.record = record;
			this.detector = detector;
			this.service = service;
			thread = new Thread(Run) { IsBackground = true, Name = "WakeWord" };
			thread.Start();
		}

		void Run()
		{
			var buffer = new short[OpenWakeWordDetector.ChunkSamples];
			while (!stopping)
			{
				var read = record.Read(buffer, 0, buffer.Length);
				if (stopping) break;
				if (read < 0)
				{
					service.handler.Post(() => service.Fail(AppResources.WakeWordMicrophoneUnavailable));
					return;
				}

				if (detector.Process(buffer.AsSpan(0, read)))
				{
					detector.Reset(); // one detection per utterance
					service.handler.Post(service.OnDetected);
				}
			}
		}

		/// <summary>Releases the microphone; returns once the reading thread no longer uses the detector.</summary>
		public void Stop()
		{
			stopping = true;
			record.Stop(); // unblocks Read
			thread.Join(TimeSpan.FromSeconds(2));
			record.Release();
		}
	}
}

/// <summary>Thin adapter from the app's <see cref="IWakeWordDetector"/> to <see cref="WakeWordService"/>.</summary>
public sealed class AndroidWakeWordDetector : IWakeWordDetector
{
	public bool IsRunning => WakeWordService.IsRunning;

	public string? LastError => WakeWordService.LastError;

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
			throw new WakeWordUnavailableException(AppResources.MicrophoneDenied);
		await Permissions.RequestAsync<Permissions.PostNotifications>();

		if (await WakeWordService.StartAsync(Platform.AppContext) is { } error)
			throw new WakeWordUnavailableException(error);
	}

	public void Stop() => WakeWordService.Stop(Platform.AppContext);

	public IDisposable Pause() => WakeWordService.Pause();
}
