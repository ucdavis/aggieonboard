using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Scheduling;
using Server.Core.Seed;

namespace Server.Tests.Scheduling;

public class DeadlineCalculatorTests
{
    private static readonly DeadlineCalculator Deadlines =
        new(new BusinessCalendar(JourneyCatalog.Holidays().Select(h => h.Date)), clearanceBusinessDaysBeforeStart: 6);

    /// <summary>A Pacific wall-clock time as an instant, the way activation times are stored.</summary>
    private static DateTimeOffset Pacific(int year, int month, int day, int hour, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0);
        return new DateTimeOffset(local, BusinessCalendar.Pacific.GetUtcOffset(local));
    }

    [Fact]
    public void Business_day_deadlines_are_end_of_day_and_ignore_the_activation_time()
    {
        var due = Deadlines.Compute(
            DeadlineRule.AfterActivation(2, DeadlineUnit.BusinessDays), Pacific(2026, 11, 6, 16, 30), null);

        due.Should().Be(new Due(new DateOnly(2026, 11, 10), null));
    }

    [Fact]
    public void Activation_date_is_taken_in_pacific_time_not_utc()
    {
        // 9 p.m. Pacific on Friday is already Saturday in UTC; it still counts from Friday.
        var due = Deadlines.Compute(
            DeadlineRule.AfterActivation(1, DeadlineUnit.BusinessDays), Pacific(2026, 11, 6, 21), null);

        due!.Value.Date.Should().Be(new DateOnly(2026, 11, 9));
    }

    [Fact]
    public void Clock_hours_count_real_elapsed_time_across_daylight_saving()
    {
        // Saturday noon PDT; clocks fall back at 2 a.m. Sunday. 48 real hours later is 11 a.m. PST Monday.
        var due = Deadlines.Compute(
            DeadlineRule.AfterActivation(48, DeadlineUnit.Hours), Pacific(2026, 10, 31, 12), null);

        due.Should().Be(new Due(new DateOnly(2026, 11, 2), new TimeOnly(11, 0)));
    }

    [Fact]
    public void Business_hours_keep_the_time_of_day()
    {
        var due = Deadlines.Compute(
            DeadlineRule.AfterActivation(48, DeadlineUnit.BusinessHours), Pacific(2026, 11, 6, 14), null);

        due.Should().Be(new Due(new DateOnly(2026, 11, 10), new TimeOnly(14, 0)));
    }

    [Fact]
    public void Due_times_are_kept_to_the_minute()
    {
        var activated = Pacific(2026, 11, 2, 9, 41).AddSeconds(13.6589946);

        var due = Deadlines.Compute(DeadlineRule.AfterActivation(48, DeadlineUnit.Hours), activated, null);

        due.Should().Be(new Due(new DateOnly(2026, 11, 4), new TimeOnly(9, 41)));
    }

    [Fact]
    public void A_deadline_landing_on_midnight_reads_as_the_end_of_the_day_before()
    {
        // From Saturday: Monday 00:00 + 48 business hours = Wednesday 00:00, which is end of Tuesday.
        var due = Deadlines.Compute(
            DeadlineRule.AfterActivation(48, DeadlineUnit.BusinessHours), Pacific(2026, 11, 7, 10), null);

        due.Should().Be(new Due(new DateOnly(2026, 11, 10), null));
    }

    [Fact]
    public void Clearance_is_six_business_days_before_the_start_date()
    {
        var start = new DateOnly(2026, 11, 30);

        Deadlines.ClearanceDeadline(start).Should().Be(new DateOnly(2026, 11, 18));
        Deadlines.Compute(DeadlineRule.Clearance, null, start).Should().Be(new Due(new DateOnly(2026, 11, 18), null));
    }

    [Fact]
    public void Start_relative_rules_count_from_the_start_date()
    {
        var start = new DateOnly(2026, 12, 7);

        Deadlines.Compute(DeadlineRule.BeforeStart(1, DeadlineUnit.CalendarDays), null, start)!.Value.Date
            .Should().Be(new DateOnly(2026, 12, 6));
        Deadlines.Compute(DeadlineRule.AfterStart(30, DeadlineUnit.CalendarDays), null, start)!.Value.Date
            .Should().Be(new DateOnly(2027, 1, 6));
        Deadlines.Compute(DeadlineRule.BeforeStart(2, DeadlineUnit.Weeks), null, start)!.Value.Date
            .Should().Be(new DateOnly(2026, 11, 23));
    }

    [Fact]
    public void No_deadline_until_the_anchor_is_known()
    {
        Deadlines.Compute(DeadlineRule.None, Pacific(2026, 11, 6, 9), new DateOnly(2026, 12, 7)).Should().BeNull();
        Deadlines.Compute(DeadlineRule.AfterActivation(2, DeadlineUnit.BusinessDays), null, null).Should().BeNull();
        Deadlines.Compute(DeadlineRule.Clearance, Pacific(2026, 11, 6, 9), null).Should().BeNull();
        Deadlines.ClearanceDeadline(null).Should().BeNull();
    }

    [Fact]
    public void Hour_units_cannot_be_counted_from_a_start_date()
    {
        var act = () => Deadlines.Compute(DeadlineRule.BeforeStart(48, DeadlineUnit.Hours), null, new DateOnly(2026, 12, 7));

        act.Should().Throw<ArgumentException>();
    }
}
