namespace Acordater.Core.Alerts;

/// <summary>Persistence of reminders (implemented in Acordater.Data).</summary>
public interface IReminderStore
{
    /// <summary>Not-done reminders, soonest first.</summary>
    Task<IReadOnlyList<Reminder>> GetPendingAsync(CancellationToken cancellationToken = default);

    Task<Reminder?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(Reminder reminder, CancellationToken cancellationToken = default);

    Task UpdateAsync(Reminder reminder, CancellationToken cancellationToken = default);
}

/// <summary>Wakes the app at a given moment to alert a reminder (Android: AlarmManager). One alarm per reminder.</summary>
public interface IAlarmScheduler
{
    /// <summary>Sets or replaces the alarm of a reminder. A moment in the past fires immediately.</summary>
    void Schedule(Guid reminderId, DateTimeOffset at);

    void Cancel(Guid reminderId);
}

/// <summary>Shows the alert with its Done / Snooze actions.</summary>
public interface IReminderNotifier
{
    void Show(Reminder reminder);

    void Dismiss(Guid reminderId);
}
