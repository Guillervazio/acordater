namespace Acordater.Core.Scheduling;

/// <summary>Business rules for when a reminder alerts (docs/spec.md, sections 4.1–4.3).</summary>
public sealed class ReminderScheduler(TimeProvider time, QuietHours quietHours)
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
            NextReminderAt = requestedAt is { } requested && requested > now ? requested : NextRepeatFrom(now),
        };
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

    DateTimeOffset NextRepeatFrom(DateTimeOffset now) => quietHours.Defer(now + RepeatInterval, time.LocalTimeZone);
}
