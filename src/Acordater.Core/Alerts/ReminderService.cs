using Acordater.Core.Scheduling;

namespace Acordater.Core.Alerts;

/// <summary>
/// Reminder lifecycle: keeps the stored reminder, its alarm and its notification in sync.
/// Platform code only forwards events here (alarm fired, action tapped, device booted).
/// </summary>
public sealed class ReminderService(
    IReminderStore store,
    ReminderScheduler scheduler,
    IAlarmScheduler alarms,
    IReminderNotifier notifier)
{
    public Task<IReadOnlyList<Reminder>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        store.GetPendingAsync(cancellationToken);

    public async Task AddAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        await store.AddAsync(reminder, cancellationToken);
        alarms.Schedule(reminder.Id, reminder.NextReminderAt);
    }

    /// <summary>The alarm of a reminder fired: alert it and queue the next repeat.</summary>
    public async Task AlertAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        if (await store.GetAsync(reminderId, cancellationToken) is not { IsDone: false } reminder) return;

        notifier.Show(reminder);
        await RepeatAsync(reminder, cancellationToken);
    }

    public async Task SnoozeAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        notifier.Dismiss(reminderId);
        if (await store.GetAsync(reminderId, cancellationToken) is not { IsDone: false } reminder) return;

        await RepeatAsync(reminder, cancellationToken);
    }

    public async Task CompleteAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        notifier.Dismiss(reminderId);
        alarms.Cancel(reminderId);
        if (await store.GetAsync(reminderId, cancellationToken) is not { IsDone: false } reminder) return;

        scheduler.Complete(reminder);
        await store.UpdateAsync(reminder, cancellationToken);
    }

    /// <summary>
    /// Re-creates the alarm of every pending reminder (after boot or app start, when alarms may have been lost).
    /// Reminders that came due meanwhile alert right away, or at the end of quiet hours.
    /// </summary>
    public async Task RescheduleAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var reminder in await store.GetPendingAsync(cancellationToken))
            alarms.Schedule(reminder.Id, scheduler.AlarmTimeFor(reminder));
    }

    async Task RepeatAsync(Reminder reminder, CancellationToken cancellationToken)
    {
        scheduler.ScheduleNextRepeat(reminder);
        await store.UpdateAsync(reminder, cancellationToken);
        alarms.Schedule(reminder.Id, reminder.NextReminderAt);
    }
}
