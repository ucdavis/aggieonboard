using FluentAssertions;
using Server.Core.Scheduling;
using Server.Core.Seed;

namespace Server.Tests.Scheduling;

public class BusinessCalendarTests
{
    private static readonly BusinessCalendar Calendar = new(JourneyCatalog.Holidays().Select(h => h.Date));

    [Theory]
    // Friday → Tuesday: the weekend does not count.
    [InlineData("2026-11-06", 2, "2026-11-10")]
    // Monday → Thursday: Veterans Day (Wed 11/11) does not count.
    [InlineData("2026-11-09", 2, "2026-11-12")]
    // Wednesday before Thanksgiving → the following Tuesday: Thu and Fri are both holidays.
    [InlineData("2026-11-25", 2, "2026-12-01")]
    // A start on a Saturday still counts from the next business day.
    [InlineData("2026-11-07", 1, "2026-11-09")]
    // Christmas Eve and Christmas Day.
    [InlineData("2026-12-23", 3, "2026-12-30")]
    // New Year's Eve and New Year's Day.
    [InlineData("2026-12-29", 2, "2027-01-04")]
    public void Adds_business_days_skipping_weekends_and_holidays(string from, int days, string expected)
    {
        Calendar.AddBusinessDays(DateOnly.Parse(from), days).Should().Be(DateOnly.Parse(expected));
    }

    [Fact]
    public void Counts_business_days_backwards_from_a_start_date()
    {
        // Six business days before Monday 11/30, skipping Thanksgiving Thu/Fri and the weekend.
        Calendar.AddBusinessDays(new DateOnly(2026, 11, 30), -6).Should().Be(new DateOnly(2026, 11, 18));
    }

    [Fact]
    public void Zero_business_days_is_the_same_date()
    {
        Calendar.AddBusinessDays(new DateOnly(2026, 11, 7), 0).Should().Be(new DateOnly(2026, 11, 7));
    }

    [Fact]
    public void Business_hours_stop_over_the_weekend()
    {
        // Friday 2 p.m.: 10 hours left Friday, 24 Monday, 14 into Tuesday.
        Calendar.AddBusinessHours(new DateTime(2026, 11, 6, 14, 0, 0), 48)
            .Should().Be(new DateTime(2026, 11, 10, 14, 0, 0));
    }

    [Fact]
    public void Business_hours_started_on_a_weekend_wait_for_monday()
    {
        // Saturday morning: the clock starts Monday 00:00 and runs out at the end of Tuesday.
        Calendar.AddBusinessHours(new DateTime(2026, 11, 7, 10, 0, 0), 48)
            .Should().Be(new DateTime(2026, 11, 11, 0, 0, 0));
    }

    [Fact]
    public void Business_hours_skip_a_holiday_midweek()
    {
        // Tuesday 9 a.m.: 15 hours Tuesday, Veterans Day skipped, 24 Thursday, 9 into Friday.
        Calendar.AddBusinessHours(new DateTime(2026, 11, 10, 9, 0, 0), 48)
            .Should().Be(new DateTime(2026, 11, 13, 9, 0, 0));
    }

    [Theory]
    [InlineData("2026-11-11", false)] // Veterans Day
    [InlineData("2026-11-14", false)] // Saturday
    [InlineData("2026-11-12", true)]
    public void Knows_business_days(string date, bool expected)
    {
        Calendar.IsBusinessDay(DateOnly.Parse(date)).Should().Be(expected);
    }
}
