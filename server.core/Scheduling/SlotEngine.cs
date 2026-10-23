using Server.Core.Domain;

namespace Server.Core.Scheduling;

/// <summary>A bookable time at one location. Pacific wall-clock time.</summary>
public sealed record Slot(DateTime Start, DateTime End, int Capacity, int Remaining);

/// <summary>
/// Turns a location's availability into slots. Pure: everything it needs is passed in, so the
/// rules (closures, holidays, notice, window, capacity) are tested without a database.
/// </summary>
public static class SlotEngine
{
    /// <param name="hours">Weekly hours. A holiday or closure removes them for that day.</param>
    /// <param name="sessions">Dated blocks. A closure removes them; a holiday does not, since someone scheduled them on purpose.</param>
    /// <param name="booked">Appointments still holding a seat, counted by start time.</param>
    /// <param name="earliest">No slot starts before this (the notice period, or the window's first day).</param>
    /// <param name="latest">No slot starts after the end of this day (the window's last day). Null for no limit.</param>
    public static IReadOnlyList<Slot> Generate(
        IEnumerable<WeeklyHours> hours,
        IEnumerable<ScheduleSession> sessions,
        IEnumerable<DateOnly> closures,
        IEnumerable<DateOnly> holidays,
        IReadOnlyDictionary<DateTime, int> booked,
        DateOnly from,
        DateOnly to,
        DateTime earliest,
        DateOnly? latest)
    {
        var closed = closures.ToHashSet();
        var holiday = holidays.ToHashSet();
        var byDay = hours.ToLookup(h => h.Day);
        var capacity = new Dictionary<(DateTime Start, DateTime End), int>();

        void Add(DateTime start, DateTime end, int seats)
        {
            if (seats <= 0 || start < earliest) return;
            if (latest is { } last && DateOnly.FromDateTime(start) > last) return;
            capacity[(start, end)] = capacity.GetValueOrDefault((start, end)) + seats;
        }

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (closed.Contains(date) || holiday.Contains(date)) continue;
            foreach (var h in byDay[date.DayOfWeek])
            {
                if (h.SlotMinutes <= 0) continue;
                var length = TimeSpan.FromMinutes(h.SlotMinutes);
                for (var start = date.ToDateTime(h.Start); start + length <= date.ToDateTime(h.End); start += length)
                {
                    Add(start, start + length, h.Capacity);
                }
            }
        }

        foreach (var s in sessions)
        {
            var date = DateOnly.FromDateTime(s.Start);
            if (date < from || date > to || closed.Contains(date)) continue;
            Add(s.Start, s.End, s.Capacity);
        }

        return capacity
            .OrderBy(kv => kv.Key.Start)
            .Select(kv => new Slot(kv.Key.Start, kv.Key.End, kv.Value, Math.Max(0, kv.Value - booked.GetValueOrDefault(kv.Key.Start))))
            .ToList();
    }
}
