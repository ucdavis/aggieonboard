namespace Server.Core.Scheduling;

/// <summary>
/// Weekdays that are not UC Davis holidays, in Pacific time. All business-day and business-hour
/// arithmetic in the app goes through here, so there is one answer to "two working days from
/// Friday".
/// </summary>
public sealed class BusinessCalendar(IEnumerable<DateOnly> holidays)
{
    public static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    private readonly HashSet<DateOnly> _holidays = [.. holidays];

    public bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !_holidays.Contains(date);

    /// <summary>
    /// The <paramref name="days"/>th business day after <paramref name="from"/> (before it, when
    /// negative). <paramref name="from"/> itself never counts, business day or not.
    /// </summary>
    public DateOnly AddBusinessDays(DateOnly from, int days)
    {
        var step = days < 0 ? -1 : 1;
        var date = from;
        for (var remaining = Math.Abs(days); remaining > 0;)
        {
            date = date.AddDays(step);
            if (IsBusinessDay(date)) remaining--;
        }
        return date;
    }

    /// <summary>
    /// Adds hours that elapse only on business days: a clock that stops at midnight before a
    /// weekend or holiday and restarts at the next business day's midnight. A start on a
    /// non-business day waits for the next business day.
    /// </summary>
    public DateTime AddBusinessHours(DateTime localStart, int hours)
    {
        var t = localStart;
        while (!IsBusinessDay(DateOnly.FromDateTime(t)))
        {
            t = t.Date.AddDays(1);
        }

        var remaining = TimeSpan.FromHours(hours);
        while (true)
        {
            var leftToday = t.Date.AddDays(1) - t;
            if (remaining <= leftToday) return t + remaining;

            remaining -= leftToday;
            t = t.Date.AddDays(1);
            while (!IsBusinessDay(DateOnly.FromDateTime(t)))
            {
                t = t.AddDays(1);
            }
        }
    }

    public static DateTime ToPacific(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Pacific).DateTime;
}
