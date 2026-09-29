namespace Acordater.Core;

/// <summary>
/// Something the user asked to be reminded of. It keeps alerting until completed.
/// Schedule state is changed only through <see cref="Scheduling.ReminderScheduler"/>.
/// </summary>
public sealed class Reminder
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Text { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset NextReminderAt { get; internal set; }

    public DateTimeOffset? CompletedAt { get; internal set; }

    public bool IsDone => CompletedAt is not null;
}
