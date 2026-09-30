namespace Acordater.Core.Scheduling;

/// <summary>Business rules for when a reminder alerts (docs/spec.md, sections 4.1–4.3).</summary>
public sealed class ReminderScheduler(TimeProvider time, IQuietHoursProvider quietHours)
{
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromHours(1);

    /// <param name="requestedAt">
    /// First alert explicitly asked for by the user. It is respected even inside quiet hours.
    /// Null or a past moment means the default: one hour from now, deferred past quiet hours.
    /// </param>
    public Reminder Create(string text, DateTimeOffset? requestedAt = null)
    {
        var now = time.GetLocalNow();
        return new Reminder
        {
            Text = text,
            CreatedAt = now,
            NextReminderAt = FirstAlertAt(requestedAt),
        };
    }

    /// <summary>
    /// The user corrected a pending reminder. The chosen time follows the same rules as <paramref name="requestedAt"/>
    /// in <see cref="Create"/>: respected even inside quiet hours, or the default if it is already in the past.
    /// </summary>
    public void Edit(Reminder reminder, string text, DateTimeOffset? requestedAt)
    {
        if (reminder.IsDone)
            throw new InvalidOperationException($"Reminder {reminder.Id} is already done.");

        reminder.Text = text;
        reminder.NextReminderAt = FirstAlertAt(requestedAt);
    }

    /// <summary>
    /// Pushes the next alert one hour from now. Used both when an alert fires unanswered and when the user snoozes it.
    /// </summary>
    public void ScheduleNextRepeat(Reminder reminder)
    {
        if (reminder.IsDone)
            throw new InvalidOperationException($"Reminder {reminder.Id} is already done.");

        reminder.NextReminderAt = NextRepeatFrom(time.GetLocalNow());
    }

    public void Complete(Reminder reminder) => reminder.CompletedAt = time.GetLocalNow();

    /// <summary>
    /// When the alarm of a pending reminder should fire: its scheduled time, or, if that already passed
    /// (e.g. the phone was off), now, deferred past quiet hours.
    /// </summary>
    public DateTimeOffset AlarmTimeFor(Reminder reminder)
    {
        var now = time.GetLocalNow();
        return reminder.NextReminderAt > now ? reminder.NextReminderAt : quietHours.Current.Defer(now, time.LocalTimeZone);
    }

    /// <summary>When a reminder created or edited now would first alert (see <see cref="Create"/>).</summary>
    public DateTimeOffset FirstAlertAt(DateTimeOffset? requestedAt)
    {
        var now = time.GetLocalNow();
        return requestedAt is { } requested && requested > now ? requested : NextRepeatFrom(now);
    }

    DateTimeOffset NextRepeatFrom(DateTimeOffset now) => quietHours.Current.Defer(now + RepeatInterval, time.LocalTimeZone);
}
