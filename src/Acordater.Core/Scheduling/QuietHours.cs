namespace Acordater.Core.Scheduling;

/// <summary>
/// Daily window [Start, End) in local time during which default reminders and repeats are not delivered.
/// The window may wrap around midnight. Start == End disables it.
/// </summary>
public sealed record QuietHours(TimeOnly Start, TimeOnly End)
{
    public static QuietHours Default { get; } = new(new TimeOnly(22, 0), new TimeOnly(8, 0));

    public bool IsEnabled => Start != End;

    bool WrapsMidnight => Start > End;

    public bool Contains(TimeOnly time) =>
        IsEnabled && (WrapsMidnight ? time >= Start || time < End : time >= Start && time < End);

    /// <summary>Moves a moment that falls inside the window to the end of that window; other moments are returned unchanged.</summary>
    public DateTimeOffset Defer(DateTimeOffset moment, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(moment, zone).DateTime;
        var time = TimeOnly.FromDateTime(local);
        if (!Contains(time)) return moment;

        var endDate = DateOnly.FromDateTime(local);
        if (WrapsMidnight && time >= Start) endDate = endDate.AddDays(1);
        return LocalTime.At(zone, endDate, End);
    }
}
