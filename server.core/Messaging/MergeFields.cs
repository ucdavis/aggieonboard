using System.Text.RegularExpressions;

namespace Server.Core.Messaging;

/// <summary>
/// Every <c>{{Field}}</c> a message the app sends may use. A template naming anything else is
/// rejected when it is saved, and a field without a value blocks sending (AGENTS.md, invariant 6).
/// Values are resolved in Phase 3; this is the contract they will meet.
/// </summary>
public static partial class MergeFields
{
    public sealed record Field(string Name, string Description);

    public static readonly IReadOnlyList<Field> All =
    [
        // The candidate
        new("FirstName", "The candidate's first name."),
        new("PositionTitle", "The position title."),
        new("Department", "The hiring department."),
        new("StartDate", "Anticipated start date."),
        new("EmployeeId", "UCPath employee ID (message 13)."),
        new("HiringManagerContact", "Who answers first-day questions (message 13)."),

        // Who is writing, and who to ask
        new("SenderName", "The staff member sending the message, for the signature."),
        new("RecruitingAssistantContact", "The assigned recruiter's email, and phone if any (the audit's recruiting assistant)."),
        new("OnboardingSpecialistName", "The assigned Service Channel specialist's name (the audit's onboarding specialist)."),
        new("OnboardingSpecialistContact", "The assigned Service Channel specialist's email, and phone if any."),

        // Deadlines
        new("ReferenceSurveyDeadline", "When references should finish their SkillSurvey (message 1)."),
        new("QualificationsDeadline", "Due date of the qualification verification step."),
        new("EmployeeDataDeadline", "Due date of the Employee Data Gathering Form step."),
        new("ClearanceDeadline", "Date every pre-employment clearance must be complete."),
        new("DmvDeadline", "Due date of the DMV Employer Pull Notice step."),
        new("PositionDescriptionDeadline", "Due date of the position description signature."),

        // Lists built from the candidate's requirements
        new("LicenseList", "Bulleted licenses and certifications to verify, each with the document to send."),
        new("DegreeList", "Bulleted degrees to verify, each with the document to send."),
        new("QualificationKinds", "What was received, in words: \"license and education\"."),
        new("RequirementList", "The additional requirements that apply, in words (message 12's subject)."),

        // Process settings
        new("QualificationSubmissionMethod", "How candidates send qualification documents."),
        new("EmployeeDataFormUrl", "Link to the Employee Data Gathering Form."),
        new("EmployeeDataFormContact", "Who answers questions about the Employee Data Gathering Form."),
        new("OehcFormUrl", "Link to the Employee Health (OEHC) appointment request form."),
        new("FingerprintSchedulingUrl", "Link for scheduling a fingerprinting appointment."),

        // The portal
        new("PortalLinkLifetime", "How long an emailed checklist link works, in words: \"14 days\"."),

        // Appointments: only the app's appointment emails have one
        new("AppointmentName", "What the appointment is for: \"Fingerprinting (Live Scan)\"."),
        new("AppointmentTime", "Its day and time: \"Wednesday, October 28, 2026, 9:15–9:30 a.m.\"."),
        new("AppointmentPlace", "The location's name, and its address when there is one."),
        new("AppointmentChangeDeadline", "Until when the candidate can change or cancel it online."),
        new("PreviousAppointmentTime", "The time it moved from (the change email)."),

        // New steps: only the new-steps email has them
        new("NewTaskList", "Bulleted list of the steps that just opened in the checklist."),
    ];

    public static readonly IReadOnlySet<string> Names = All.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Fields only an appointment email has a value for; any other message using one could never send.</summary>
    public static readonly IReadOnlySet<string> AppointmentOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        "AppointmentName", "AppointmentTime", "AppointmentPlace", "AppointmentChangeDeadline", "PreviousAppointmentTime",
    };

    /// <summary>Fields only the new-steps email has a value for.</summary>
    public static readonly IReadOnlySet<string> NewTasksOnly = new HashSet<string>(StringComparer.Ordinal) { "NewTaskList" };

    /// <summary>The distinct field names a piece of template text uses, in order of first use.</summary>
    public static IReadOnlyList<string> In(string text) =>
        Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9]*)\s*\}\}")]
    private static partial Regex Placeholder();
}
