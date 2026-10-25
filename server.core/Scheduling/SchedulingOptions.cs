namespace Server.Core.Scheduling;

/// <summary>Configuration section "Scheduling".</summary>
public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    /// <summary>Whether this instance sends day-before reminders. Run it on one instance only.</summary>
    public bool SendReminders { get; init; } = true;

    /// <summary>The Pacific hour, on the day before, from which reminders go out.</summary>
    public int ReminderHour { get; init; } = 9;

    /// <summary>How often the reminder job looks for appointments to remind.</summary>
    public int ReminderCheckMinutes { get; init; } = 15;
}
