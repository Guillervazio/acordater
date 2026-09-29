namespace Acordater.Core;

internal static class LocalTime
{
    /// <summary>The instant at which the wall clock of <paramref name="zone"/> shows <paramref name="date"/> <paramref name="time"/>.</summary>
    public static DateTimeOffset At(TimeZoneInfo zone, DateOnly date, TimeOnly time)
    {
        var wallClock = date.ToDateTime(time);
        return new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
    }
}
