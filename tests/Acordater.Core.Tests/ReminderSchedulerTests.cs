using Acordater.Core.Scheduling;
using static Acordater.Core.Tests.TestClock;

namespace Acordater.Core.Tests;

public class ReminderSchedulerTests
{
    static ReminderScheduler Scheduler(TimeProvider time) => new(time, QuietHours.Default);

    [Fact]
    public void DefaultFirstReminderIsOneHourAfterCreation()
    {
        var reminder = Scheduler(At(29, 10)).Create("comprar jabón");

        Assert.Equal("comprar jabón", reminder.Text);
        Assert.Equal(Local(29, 10), reminder.CreatedAt);
        Assert.Equal(Local(29, 11), reminder.NextReminderAt);
        Assert.False(reminder.IsDone);
    }

    [Fact]
    public void RequestedTimeIsUsedForFirstReminder()
    {
        var reminder = Scheduler(At(29, 10)).Create("limpiar la caja del gato", Local(29, 12));

        Assert.Equal(Local(29, 12), reminder.NextReminderAt);
    }

    [Fact]
    public void RequestedTimeInThePastFallsBackToDefault()
    {
        var reminder = Scheduler(At(29, 10)).Create("x", Local(29, 9));

        Assert.Equal(Local(29, 11), reminder.NextReminderAt);
    }

    [Fact]
    public void DefaultFirstReminderInsideQuietHoursMovesToEndOfQuietHours()
    {
        var reminder = Scheduler(At(29, 21, 30)).Create("x");

        Assert.Equal(Local(30, 8), reminder.NextReminderAt);
    }

    [Fact]
    public void ExplicitRequestedTimeInsideQuietHoursIsRespected()
    {
        var reminder = Scheduler(At(29, 10)).Create("sacar la basura", Local(29, 23));

        Assert.Equal(Local(29, 23), reminder.NextReminderAt);
    }

    [Fact]
    public void NextRepeatIsOneHourAfterNow()
    {
        var time = At(29, 10);
        var scheduler = Scheduler(time);
        var reminder = scheduler.Create("x", Local(29, 12));

        time.SetLocalNow(29, 12, 5);
        scheduler.ScheduleNextRepeat(reminder);

        Assert.Equal(Local(29, 13, 5), reminder.NextReminderAt);
    }

    [Theory]
    [InlineData(21, 15)] // repeat would land at 22:15, in the evening part of quiet hours
    [InlineData(23, 0)]  // after an explicit 23:00 reminder, repeat would land at 00:00
    public void RepeatInsideQuietHoursMovesToNextMorning(int hour, int minute)
    {
        var time = At(29, hour, minute);
        var scheduler = Scheduler(time);
        var reminder = scheduler.Create("x", Local(29, 23, 30));

        scheduler.ScheduleNextRepeat(reminder);

        Assert.Equal(Local(30, 8), reminder.NextReminderAt);
    }

    [Fact]
    public void CustomQuietHoursAreApplied()
    {
        var scheduler = new ReminderScheduler(At(29, 22), new QuietHours(new(23, 0), new(7, 0)));

        var reminder = scheduler.Create("x");

        Assert.Equal(Local(30, 7), reminder.NextReminderAt);
    }

    [Fact]
    public void CompleteMarksReminderDone()
    {
        var time = At(29, 10);
        var scheduler = Scheduler(time);
        var reminder = scheduler.Create("x");

        time.SetLocalNow(29, 10, 30);
        scheduler.Complete(reminder);

        Assert.True(reminder.IsDone);
        Assert.Equal(Local(29, 10, 30), reminder.CompletedAt);
    }

    [Fact]
    public void CompletedReminderCannotBeRescheduled()
    {
        var scheduler = Scheduler(At(29, 10));
        var reminder = scheduler.Create("x");
        scheduler.Complete(reminder);

        Assert.Throws<InvalidOperationException>(() => scheduler.ScheduleNextRepeat(reminder));
    }
}

public class QuietHoursTests
{
    [Theory]
    [InlineData(22, 0, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)]
    [InlineData(7, 59, true)]
    [InlineData(8, 0, false)]
    [InlineData(21, 59, false)]
    [InlineData(12, 0, false)]
    public void DefaultWindowWrapsAroundMidnight(int hour, int minute, bool expected) =>
        Assert.Equal(expected, QuietHours.Default.Contains(new TimeOnly(hour, minute)));

    [Theory]
    [InlineData(13, 0, true)]
    [InlineData(14, 59, true)]
    [InlineData(15, 0, false)]
    [InlineData(12, 59, false)]
    public void SameDayWindow(int hour, int minute, bool expected) =>
        Assert.Equal(expected, new QuietHours(new(13, 0), new(15, 0)).Contains(new TimeOnly(hour, minute)));

    [Fact]
    public void EqualStartAndEndDisablesQuietHours()
    {
        var quiet = new QuietHours(new(8, 0), new(8, 0));

        Assert.False(quiet.IsEnabled);
        Assert.False(quiet.Contains(new TimeOnly(8, 0)));
        Assert.Equal(TestClock.Local(29, 23), quiet.Defer(TestClock.Local(29, 23), TestClock.Zone));
    }
}
