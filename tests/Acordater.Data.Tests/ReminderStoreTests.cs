using Acordater.Core.Scheduling;
using Microsoft.Extensions.Time.Testing;

namespace Acordater.Data.Tests;

public sealed class ReminderStoreTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.FromHours(2));

    readonly TestDatabase database = new();
    readonly FakeTimeProvider time = new(Now.ToUniversalTime());
    readonly ReminderStore store;
    readonly ReminderScheduler scheduler;

    public ReminderStoreTests()
    {
        store = new ReminderStore(database);
        scheduler = new ReminderScheduler(time, QuietHours.Default);
    }

    public void Dispose() => database.Dispose();

    [Fact]
    public async Task AddedReminderRoundTrips()
    {
        var reminder = scheduler.Create("comprar jabón", Now.AddHours(2));

        await store.AddAsync(reminder);
        var loaded = await store.GetAsync(reminder.Id);

        Assert.NotNull(loaded);
        Assert.Equal("comprar jabón", loaded.Text);
        Assert.Equal(reminder.CreatedAt, loaded.CreatedAt);
        Assert.Equal(Now.AddHours(2), loaded.NextReminderAt);
        Assert.Equal(TimeSpan.FromHours(2), loaded.NextReminderAt.Offset);
        Assert.False(loaded.IsDone);
    }

    [Fact]
    public async Task PendingExcludesDoneAndIsOrderedBySoonest()
    {
        var later = scheduler.Create("later", Now.AddHours(5));
        var sooner = scheduler.Create("sooner", Now.AddMinutes(10));
        var done = scheduler.Create("done", Now.AddMinutes(5));
        scheduler.Complete(done);
        foreach (var r in new[] { later, sooner, done }) await store.AddAsync(r);

        var pending = await store.GetPendingAsync();

        Assert.Equal(["sooner", "later"], pending.Select(r => r.Text));
    }

    [Fact]
    public async Task UpdatePersistsScheduleChanges()
    {
        var reminder = scheduler.Create("x", Now.AddMinutes(30));
        await store.AddAsync(reminder);

        time.Advance(TimeSpan.FromMinutes(30));
        scheduler.ScheduleNextRepeat(reminder);
        await store.UpdateAsync(reminder);

        var loaded = await store.GetAsync(reminder.Id);
        Assert.Equal(Now.AddMinutes(90), loaded!.NextReminderAt);
    }

    [Fact]
    public async Task CompletedReminderIsPersistedAsDone()
    {
        var reminder = scheduler.Create("x");
        await store.AddAsync(reminder);

        scheduler.Complete(reminder);
        await store.UpdateAsync(reminder);

        Assert.True((await store.GetAsync(reminder.Id))!.IsDone);
        Assert.Empty(await store.GetPendingAsync());
    }
}
