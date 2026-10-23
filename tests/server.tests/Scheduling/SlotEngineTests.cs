using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Scheduling;

namespace Server.Tests.Scheduling;

public class SlotEngineTests
{
    // Wednesday 2026-11-04 and the Wednesday after it; Veterans Day (Wednesday 11/11) is a holiday.
    private static readonly DateOnly Wednesday = new(2026, 11, 4);
    private static readonly DateOnly VeteransDay = new(2026, 11, 11);
    private static readonly WeeklyHours WednesdayMornings =
        new() { Day = DayOfWeek.Wednesday, Start = new(8, 0), End = new(9, 0), SlotMinutes = 15, Capacity = 2 };

    private static IReadOnlyList<Slot> Generate(
        IEnumerable<WeeklyHours>? hours = null,
        IEnumerable<ScheduleSession>? sessions = null,
        IEnumerable<DateOnly>? closures = null,
        IReadOnlyDictionary<DateTime, int>? booked = null,
        DateTime? earliest = null,
        DateOnly? latest = null,
        DateOnly? to = null) =>
        SlotEngine.Generate(
            hours ?? [WednesdayMornings], sessions ?? [], closures ?? [], [VeteransDay],
            booked ?? new Dictionary<DateTime, int>(), Wednesday, to ?? Wednesday,
            earliest ?? DateTime.MinValue, latest);

    [Fact]
    public void Weekly_hours_become_back_to_back_slots()
    {
        Generate().Select(s => s.Start.ToString("HH:mm")).Should().Equal("08:00", "08:15", "08:30", "08:45");
        Generate()[0].Should().Be(new Slot(Wednesday.ToDateTime(new(8, 0)), Wednesday.ToDateTime(new(8, 15)), 2, 2));
    }

    [Fact]
    public void A_partial_slot_at_the_end_is_not_offered()
    {
        var hours = new WeeklyHours { Day = DayOfWeek.Wednesday, Start = new(8, 0), End = new(8, 50), SlotMinutes = 20, Capacity = 1 };

        Generate(hours: [hours]).Select(s => s.Start.ToString("HH:mm")).Should().Equal("08:00", "08:20");
    }

    [Fact]
    public void Bookings_use_up_seats_and_a_full_slot_has_none_left()
    {
        var eight = Wednesday.ToDateTime(new(8, 0));

        var slots = Generate(booked: new Dictionary<DateTime, int> { [eight] = 2, [eight.AddMinutes(15)] = 1 });

        slots[0].Remaining.Should().Be(0);
        slots[1].Remaining.Should().Be(1);
    }

    [Fact]
    public void Holidays_and_closures_remove_the_day()
    {
        Generate(to: VeteransDay).Should().OnlyContain(s => DateOnly.FromDateTime(s.Start) == Wednesday);
        Generate(closures: [Wednesday]).Should().BeEmpty();
    }

    [Fact]
    public void A_session_on_a_holiday_is_kept_but_a_closure_removes_it()
    {
        var session = new ScheduleSession { Start = VeteransDay.ToDateTime(new(9, 0)), End = VeteransDay.ToDateTime(new(12, 0)), Capacity = 40 };

        Generate(hours: [], sessions: [session], to: VeteransDay).Should().ContainSingle().Which.Capacity.Should().Be(40);
        Generate(hours: [], sessions: [session], closures: [VeteransDay], to: VeteransDay).Should().BeEmpty();
    }

    [Fact]
    public void Nothing_before_the_earliest_time_or_after_the_last_day()
    {
        Generate(earliest: Wednesday.ToDateTime(new(8, 20))).Select(s => s.Start.ToString("HH:mm")).Should().Equal("08:30", "08:45");
        Generate(latest: Wednesday.AddDays(-1)).Should().BeEmpty();
    }

    [Fact]
    public void Overlapping_hours_add_their_capacity()
    {
        var second = new WeeklyHours { Day = DayOfWeek.Wednesday, Start = new(8, 0), End = new(8, 30), SlotMinutes = 15, Capacity = 1 };

        Generate(hours: [WednesdayMornings, second])[0].Capacity.Should().Be(3);
    }
}
