using Microsoft.Extensions.Time.Testing;

namespace Acordater.Core.Tests;

/// <summary>Deterministic clock in a fixed UTC+2 zone (no DST), starting on Tuesday 2026-09-29.</summary>
internal static class TestClock
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    public static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test+2", Offset, "Test+2", "Test+2");

    public static DateTimeOffset Local(int day, int hour, int minute = 0) =>
        new(2026, 9, day, hour, minute, 0, Offset);

    // FakeTimeProvider ignores the offset of the DateTimeOffset it is given, so always hand it UTC.
    public static FakeTimeProvider At(int day, int hour, int minute = 0)
    {
        var time = new FakeTimeProvider(Local(day, hour, minute).ToUniversalTime());
        time.SetLocalTimeZone(Zone);
        return time;
    }

    public static void SetLocalNow(this FakeTimeProvider time, int day, int hour, int minute = 0) =>
        time.SetUtcNow(Local(day, hour, minute).ToUniversalTime());
}
