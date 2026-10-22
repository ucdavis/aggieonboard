using Server.Core.Domain;

namespace Server.Core.Scheduling;

/// <summary>A Pacific due date, with a time of day only for hour-based deadlines.</summary>
public readonly record struct Due(DateOnly Date, TimeOnly? Time)
{
    /// <summary>
    /// Due times are kept to the minute: a deadline is read, not timed. One that lands exactly on
    /// midnight is the end of the day before: "48 hours from Monday 00:00" reads as end of Tuesday,
    /// not 12 a.m. Wednesday.
    /// </summary>
    public static Due FromLocal(DateTime local)
    {
        local = local.AddTicks(-(local.Ticks % TimeSpan.TicksPerMinute));
        return local.TimeOfDay == TimeSpan.Zero
            ? new Due(DateOnly.FromDateTime(local).AddDays(-1), null)
            : new Due(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }
}

/// <summary>Turns a step's <see cref="DeadlineRule"/> into a due date for one candidate.</summary>
public sealed class DeadlineCalculator(BusinessCalendar calendar, int clearanceBusinessDaysBeforeStart)
{
    /// <summary>The date every pre-employment clearance must be done by, or null without a start date.</summary>
    public DateOnly? ClearanceDeadline(DateOnly? startDate) =>
        startDate is { } start ? calendar.AddBusinessDays(start, -clearanceBusinessDaysBeforeStart) : null;

    /// <summary>
    /// Null when the rule has no deadline or its anchor is not known yet: a step not yet activated,
    /// or a candidate without a start date.
    /// </summary>
    public Due? Compute(DeadlineRule rule, DateTimeOffset? activatedAt, DateOnly? startDate)
    {
        switch (rule.Anchor)
        {
            case DeadlineAnchor.None:
                return null;

            case DeadlineAnchor.Activation:
                return activatedAt is { } at ? FromActivation(rule, at) : null;

            case DeadlineAnchor.Clearance:
                return ClearanceDeadline(startDate) is { } clearance ? new Due(clearance, null) : null;

            case DeadlineAnchor.BeforeStart:
            case DeadlineAnchor.AfterStart:
                if (startDate is not { } start) return null;
                var sign = rule.Anchor == DeadlineAnchor.BeforeStart ? -1 : 1;
                return new Due(AddDays(start, sign * rule.Amount, rule.Unit), null);

            default:
                throw new ArgumentOutOfRangeException(nameof(rule), rule.Anchor, null);
        }
    }

    private Due FromActivation(DeadlineRule rule, DateTimeOffset activatedAt)
    {
        switch (rule.Unit)
        {
            case DeadlineUnit.Hours:
                // Real elapsed hours, so a daylight-saving change in between is counted correctly.
                return Due.FromLocal(BusinessCalendar.ToPacific(activatedAt.AddHours(rule.Amount)));

            case DeadlineUnit.BusinessHours:
                return Due.FromLocal(calendar.AddBusinessHours(BusinessCalendar.ToPacific(activatedAt), rule.Amount));

            default:
                var activatedOn = DateOnly.FromDateTime(BusinessCalendar.ToPacific(activatedAt));
                return new Due(AddDays(activatedOn, rule.Amount, rule.Unit), null);
        }
    }

    private DateOnly AddDays(DateOnly from, int amount, DeadlineUnit unit) => unit switch
    {
        DeadlineUnit.BusinessDays => calendar.AddBusinessDays(from, amount),
        DeadlineUnit.CalendarDays => from.AddDays(amount),
        DeadlineUnit.Weeks => from.AddDays(7 * amount),
        _ => throw new ArgumentException($"{unit} deadlines count from activation only.", nameof(unit)),
    };
}
