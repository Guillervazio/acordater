using System.Globalization;
using Acordater.App.Resources.Strings;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Media;
using Android.OS;
using Android.Speech.Tts;
using TextToSpeech = Android.Speech.Tts.TextToSpeech;

namespace Acordater.App;

/// <summary>
/// Rings (alarm sound on the alarm stream + vibration, so it plays in silent mode) until every ringing reminder
/// is done or snoozed. Its foreground notification is the alarm UI: Done / Snooze for the first ringing reminder
/// and a full-screen <see cref="AlarmActivity"/> when the phone is locked.
/// The text of the ringing reminder is read aloud (also on the alarm stream) when it starts ringing and then every
/// <see cref="SpeechInterval"/>, pausing the alarm sound meanwhile.
/// The systemExempted type is allowed for alarm apps holding USE_EXACT_ALARM.
/// </summary>
[Service(Name = "com.acordater.app.AlarmRingingService", Exported = false, ForegroundServiceType = ForegroundService.TypeSystemExempted)]
public sealed class AlarmRingingService : Service, TextToSpeech.IOnInitListener
{
	// Channel settings are immutable once created; bump the id to change them.
	const string ChannelId = "alarm_v1";
	const int NotificationId = 1;
	const string UtteranceId = "alarm";
	static readonly TimeSpan SpeechInterval = TimeSpan.FromSeconds(30);

	static readonly Lock gate = new();
	static readonly List<RingingReminder> ringing = [];
	static bool running;

	MediaPlayer? player;
	Vibrator? vibrator;
	TextToSpeech? speech;
	bool speechReady;
	Guid? announcedId;
	IDisposable? wakeWordPause;
	readonly Handler handler = new(Looper.MainLooper!);
	Java.Lang.Runnable? nextAnnouncement;

	public sealed record RingingReminder(Guid Id, string Text);

	/// <summary>The reminder shown by the alarm UI, or null when nothing is ringing.</summary>
	public static RingingReminder? Current
	{
		get { lock (gate) return ringing.FirstOrDefault(); }
	}

	public static void Ring(Context context, Guid reminderId, string text)
	{
		lock (gate)
		{
			ringing.RemoveAll(r => r.Id == reminderId);
			ringing.Add(new RingingReminder(reminderId, text));
		}
		context.StartForegroundService(new Intent(context, typeof(AlarmRingingService)));
	}

	public static void Silence(Context context, Guid reminderId)
	{
		lock (gate)
		{
			if (ringing.RemoveAll(r => r.Id == reminderId) == 0 || !running) return;
		}
		context.StartService(new Intent(context, typeof(AlarmRingingService)));
	}

	public override IBinder? OnBind(Intent? intent) => null;

	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		RingingReminder? current;
		int count;
		lock (gate)
		{
			running = true;
			current = ringing.FirstOrDefault();
			count = ringing.Count;
		}

		// Always call StartForeground, even when about to stop: a service started with
		// StartForegroundService that never does so crashes the app.
		StartForeground(NotificationId, BuildNotification(current, count), ForegroundService.TypeSystemExempted);

		if (current is null)
		{
			StopForeground(StopForegroundFlags.Remove);
			StopSelf();
		}
		else
		{
			StartAlarm();
			if (current.Id != announcedId) Announce();
		}
		return StartCommandResult.NotSticky;
	}

	public override void OnDestroy()
	{
		StopAlarm();
		lock (gate) running = false;
		base.OnDestroy();
	}

	void StartAlarm()
	{
		if (player is not null) return;

		// The wake word must not react to the alarm (or its reading) and gets its microphone back afterwards.
		wakeWordPause ??= WakeWordService.Pause();

		try
		{
			player = new MediaPlayer();
			player.SetAudioAttributes(new AudioAttributes.Builder()
				.SetUsage(AudioUsageKind.Alarm)!
				.SetContentType(AudioContentType.Sonification)!
				.Build());
			player.SetDataSource(this, RingtoneManager.GetDefaultUri(RingtoneType.Alarm)
				?? RingtoneManager.GetDefaultUri(RingtoneType.Ringtone)!);
			player.Looping = true;
			player.Prepare();
			player.Start();
		}
		catch (Exception ex)
		{
			global::Android.Util.Log.Error("Acordater", $"Alarm sound failed: {ex}");
		}

		vibrator = ((VibratorManager)GetSystemService(VibratorManagerService)!).DefaultVibrator;
		vibrator.Vibrate(
			VibrationEffect.CreateWaveform([0, 800, 800], repeat: 0)!,
			VibrationAttributes.CreateForUsage((int)VibrationAttributesUsageType.Alarm));

		speech ??= new TextToSpeech(this, this); // OnInit announces the reminder once the engine is ready
	}

	public void OnInit(OperationResult status)
	{
		if (status != OperationResult.Success || speech is null)
		{
			global::Android.Util.Log.Error("Acordater", $"Text to speech unavailable: {status}");
			return;
		}

		speech.SetAudioAttributes(new AudioAttributes.Builder()
			.SetUsage(AudioUsageKind.Alarm)!
			.SetContentType(AudioContentType.Speech)!
			.Build());
		speech.SetLanguage(Java.Util.Locale.Default);
		speech.SetOnUtteranceProgressListener(new SpeechListener(() => handler.Post(ResumeSound)));
		speechReady = true;
		Announce();
	}

	/// <summary>Reads the ringing reminder aloud now and schedules the next reading.</summary>
	void Announce()
	{
		if (nextAnnouncement is not null) handler.RemoveCallbacks(nextAnnouncement);
		if (!speechReady || speech is null || Current is not { } current) return;

		announcedId = current.Id;
		player?.Pause();
		var text = string.Format(CultureInfo.CurrentCulture, AppResources.AlarmSpeech, current.Text);
		if (speech.Speak(text, QueueMode.Flush, null, UtteranceId) != OperationResult.Success)
			ResumeSound();

		nextAnnouncement ??= new Java.Lang.Runnable(Announce);
		handler.PostDelayed(nextAnnouncement, (long)SpeechInterval.TotalMilliseconds);
	}

	void ResumeSound()
	{
		if (player is { IsPlaying: false }) player.Start();
	}

	void StopAlarm()
	{
		handler.RemoveCallbacksAndMessages(null);
		speech?.Stop();
		speech?.Shutdown();
		speech = null;
		speechReady = false;
		announcedId = null;

		player?.Stop();
		player?.Release();
		player = null;
		vibrator?.Cancel();
		vibrator = null;
		wakeWordPause?.Dispose();
		wakeWordPause = null;
	}

	/// <summary>Resumes the alarm sound when a reading ends. Interrupted readings are followed by a new one, so OnStop is ignored.</summary>
	sealed class SpeechListener(Action done) : UtteranceProgressListener
	{
		public override void OnStart(string? utteranceId) { }

		public override void OnDone(string? utteranceId) => done();

		[Obsolete("Required override; the overload with an error code is used on current Android versions.")]
		public override void OnError(string? utteranceId) => done();

		public override void OnError(string? utteranceId, TextToSpeechError errorCode) => done();
	}

	Notification BuildNotification(RingingReminder? current, int count)
	{
		EnsureChannel();

		var icon = Icon.CreateWithResource(this, Resource.Drawable.ic_notification);
		var notification = new Notification.Builder(this, ChannelId);
		notification.SetSmallIcon(icon);
		notification.SetCategory(Notification.CategoryAlarm);
		notification.SetOngoing(true);
		notification.SetContentTitle(current?.Text ?? AppResources.AppTitle);
		if (current is null) return notification.Build();

		notification.SetContentText(count > 1
			? string.Format(CultureInfo.CurrentCulture, AppResources.AlarmMore, count - 1)
			: AppResources.NotificationBody);
		var alarmScreen = AlarmActivity.PendingIntentFor(this);
		notification.SetFullScreenIntent(alarmScreen, true);
		notification.SetContentIntent(alarmScreen);
		notification.AddAction(new Notification.Action.Builder(icon, AppResources.Done,
			ReminderIntents.Broadcast<ReminderActionReceiver>(this, ReminderIntents.ActionDone, current.Id)).Build());
		notification.AddAction(new Notification.Action.Builder(icon, AppResources.Snooze,
			ReminderIntents.Broadcast<ReminderActionReceiver>(this, ReminderIntents.ActionSnooze, current.Id)).Build());
		return notification.Build();
	}

	void EnsureChannel()
	{
		var notifications = (NotificationManager)GetSystemService(NotificationService)!;
		var channel = new NotificationChannel(ChannelId, AppResources.ChannelName, NotificationImportance.High);
		channel.SetSound(null, null); // the service plays the alarm itself
		channel.EnableVibration(false);
		notifications.CreateNotificationChannel(channel);
		notifications.DeleteNotificationChannel("reminders_v1"); // replaced by this channel in phase 2c
	}
}
