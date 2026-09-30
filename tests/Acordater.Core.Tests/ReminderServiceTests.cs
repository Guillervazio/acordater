using Acordater.Core.Alerts;
using Acordater.Core.Scheduling;
using Microsoft.Extensions.Time.Testing;
using static Acordater.Core.Tests.TestClock;

namespace Acordater.Core.Tests;

public class ReminderServiceTests
{
    readonly FakeTimeProvider time = At(29, 10);
    readonly InMemoryStore store = new();
    readonly FakeAlarms alarms = new();
    readonly FakeNotifier notifier = new();
    readonly ReminderScheduler scheduler;
    readonly ReminderService service;

    public ReminderServiceTests()
    {
        scheduler = new ReminderScheduler(time, QuietHours.Default);
        service = new ReminderService(store, scheduler, alarms, notifier);
    }

    [Fact]
    public async Task AddStoresReminderAndSchedulesItsAlarm()
    {
        var reminder = scheduler.Create("sacar la ropa", Local(29, 10, 5));

        await service.AddAsync(reminder);

        Assert.Same(reminder, await store.GetAsync(reminder.Id));
        Assert.Equal(Local(29, 10, 5), alarms.Scheduled[reminder.Id]);
    }

    [Fact]
    public async Task AlertShowsNotificationAndQueuesNextRepeat()
    {
        var reminder = await AddAsync(Local(29, 10, 5));

        time.SetLocalNow(29, 10, 5);
        await service.AlertAsync(reminder.Id);

        Assert.Contains(reminder.Id, notifier.Shown);
        Assert.Equal(Local(29, 11, 5), reminder.NextReminderAt);
        Assert.Equal(Local(29, 11, 5), alarms.Scheduled[reminder.Id]);
        Assert.Equal(1, store.Updates);
    }

    [Fact]
    public async Task AlertOfDoneOrUnknownReminderDoesNothing()
    {
        var reminder = await AddAsync(Local(29, 10, 5));
        await service.CompleteAsync(reminder.Id);

        await service.AlertAsync(reminder.Id);
        await service.AlertAsync(Guid.NewGuid());

        Assert.Empty(notifier.Shown);
        Assert.Empty(alarms.Scheduled);
    }

    [Fact]
    public async Task SnoozeDismissesAndMovesAlarmOneHourFromNow()
    {
        var reminder = await AddAsync(Local(29, 10, 5));
        time.SetLocalNow(29, 10, 5);
        await service.AlertAsync(reminder.Id);

        time.SetLocalNow(29, 10, 20);
        await service.SnoozeAsync(reminder.Id);

        Assert.Contains(reminder.Id, notifier.Dismissed);
        Assert.Equal(Local(29, 11, 20), alarms.Scheduled[reminder.Id]);
        Assert.Equal(Local(29, 11, 20), (await store.GetAsync(reminder.Id))!.NextReminderAt);
    }

    [Fact]
    public async Task CompleteMarksDoneCancelsAlarmAndDismisses()
    {
        var reminder = await AddAsync(Local(29, 10, 5));

        await service.CompleteAsync(reminder.Id);

        Assert.True(reminder.IsDone);
        Assert.DoesNotContain(reminder.Id, alarms.Scheduled.Keys);
        Assert.Contains(reminder.Id, notifier.Dismissed);
    }

    [Fact]
    public async Task EditStoresChangesDismissesAndMovesAlarm()
    {
        var reminder = await AddAsync(Local(29, 12));

        Assert.True(await service.EditAsync(reminder.Id, "regar las plantas", Local(29, 18)));

        var stored = (await store.GetAsync(reminder.Id))!;
        Assert.Equal("regar las plantas", stored.Text);
        Assert.Equal(Local(29, 18), stored.NextReminderAt);
        Assert.Equal(Local(29, 18), alarms.Scheduled[reminder.Id]);
        Assert.Contains(reminder.Id, notifier.Dismissed);
    }

    [Fact]
    public async Task EditOfDoneReminderDoesNothing()
    {
        var reminder = await AddAsync(Local(29, 12));
        await service.CompleteAsync(reminder.Id);

        Assert.False(await service.EditAsync(reminder.Id, "y", Local(29, 18)));
        Assert.False(await service.EditAsync(Guid.NewGuid(), "y", Local(29, 18)));

        Assert.Equal("x", reminder.Text);
        Assert.Empty(alarms.Scheduled);
    }

    [Fact]
    public async Task RescheduleAllRestoresFutureAlarmsAndFiresOverdueOnesNow()
    {
        var future = await AddAsync(Local(29, 18));
        var overdue = await AddAsync(Local(29, 10, 30));
        var done = await AddAsync(Local(29, 12));
        await service.CompleteAsync(done.Id);
        alarms.Scheduled.Clear(); // e.g. the phone rebooted

        time.SetLocalNow(29, 11);
        await service.RescheduleAllAsync();

        Assert.Equal(Local(29, 18), alarms.Scheduled[future.Id]);
        Assert.Equal(Local(29, 11), alarms.Scheduled[overdue.Id]);
        Assert.DoesNotContain(done.Id, alarms.Scheduled.Keys);
    }

    [Fact]
    public async Task RescheduleAllDefersOverdueRemindersPastQuietHours()
    {
        var overdue = await AddAsync(Local(29, 21));

        time.SetLocalNow(30, 3); // phone switched back on in the middle of the night
        await service.RescheduleAllAsync();

        Assert.Equal(Local(30, 8), alarms.Scheduled[overdue.Id]);
    }

    async Task<Reminder> AddAsync(DateTimeOffset at)
    {
        var reminder = scheduler.Create("x", at);
        await service.AddAsync(reminder);
        return reminder;
    }

    sealed class InMemoryStore : IReminderStore
    {
        readonly Dictionary<Guid, Reminder> reminders = [];

        public int Updates { get; private set; }

        public Task<IReadOnlyList<Reminder>> GetPendingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Reminder>>(reminders.Values.Where(r => !r.IsDone).OrderBy(r => r.NextReminderAt).ToList());

        public Task<Reminder?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(reminders.GetValueOrDefault(id));

        public Task AddAsync(Reminder reminder, CancellationToken cancellationToken = default)
        {
            reminders.Add(reminder.Id, reminder);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Reminder reminder, CancellationToken cancellationToken = default)
        {
            reminders[reminder.Id] = reminder;
            Updates++;
            return Task.CompletedTask;
        }
    }

    sealed class FakeAlarms : IAlarmScheduler
    {
        public Dictionary<Guid, DateTimeOffset> Scheduled { get; } = [];

        public void Schedule(Guid reminderId, DateTimeOffset at) => Scheduled[reminderId] = at;

        public void Cancel(Guid reminderId) => Scheduled.Remove(reminderId);
    }

    sealed class FakeNotifier : IReminderNotifier
    {
        public List<Guid> Shown { get; } = [];

        public List<Guid> Dismissed { get; } = [];

        public void Show(Reminder reminder) => Shown.Add(reminder.Id);

        public void Dismiss(Guid reminderId) => Dismissed.Add(reminderId);
    }
}
