namespace Server.Core.Messaging;

/// <summary>
/// Messages the app needs that the communications audit does not cover. Their wording is new, so
/// it is listed under "Copy to review" in docs/PLAN.md. They belong to no step.
/// </summary>
public static class SystemMessages
{
    /// <summary>A sign-in link to the portal, sent by staff or at the candidate's request.</summary>
    public const string PortalLink = "portal-link";

    // Sent by the schedule when an appointment is booked, moved or cancelled, and the day before it.
    // Each carries a calendar invitation except the reminder.
    public const string AppointmentBooked = "appointment-booked";
    public const string AppointmentChanged = "appointment-changed";
    public const string AppointmentCancelled = "appointment-cancelled";
    public const string AppointmentReminder = "appointment-reminder";

    public static readonly IReadOnlySet<string> Appointment = new HashSet<string>
    {
        AppointmentBooked, AppointmentChanged, AppointmentCancelled, AppointmentReminder,
    };

    /// <summary>Sent when steps open on their own and the new employee hasn't been in their checklist (JourneyFlow).</summary>
    public const string NewTasks = "new-tasks";

    public static readonly IReadOnlySet<string> Keys = new HashSet<string>(Appointment) { PortalLink, NewTasks };
}
