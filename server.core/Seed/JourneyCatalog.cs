using System.Reflection;
using Server.Core.Domain;
using Server.Core.Messaging;

namespace Server.Core.Seed;

/// <summary>
/// The journey as the communications audit describes it: steps, process settings, holidays and the
/// rewritten messages. Reference data every environment needs. Seeding inserts what is missing and
/// never overwrites, so edits made in the app survive restarts.
/// </summary>
public static class JourneyCatalog
{
    public static IReadOnlyList<StepDefinition> Steps() =>
    [
        new()
        {
            Key = "references", Order = 1, AuditReference = "1–3", Title = "Reference check",
            Phase = JourneyPhase.TalentAcquisition, Owner = StaffRole.Recruiter,
            RequestMessageKey = "references-request", VendorMessageKey = "skillsurvey-request",
            WatchForSender = "SkillSurvey",
            Deadline = DeadlineRule.AfterActivation(2, DeadlineUnit.BusinessDays),
            DeadlineQuestion = "TA: confirm the reference requirements and the candidate and reference response deadlines. "
                + "Message 1 says \"48 hours/two working days\" (audit #1).",
        },
        new()
        {
            Key = "qualifications", Order = 2, AuditReference = "4–5", Title = "Qualification verification",
            Phase = JourneyPhase.TalentAcquisition, Owner = StaffRole.Recruiter,
            Condition = CandidateCondition.HasQualifications,
            RequestMessageKey = "qualifications-request", AcknowledgmentMessageKey = "qualifications-received",
            DeadlineQuestion = "TA: what is the approved submission process, and is there a standard deadline? (audit #4)",
        },
        new()
        {
            Key = "offer", Order = 3, AuditReference = "6", Title = "Offer",
            Phase = JourneyPhase.TalentAcquisition, Owner = StaffRole.Recruiter,
            WatchForSender = "UC Davis jobs application portal",
            DeadlineConfirmed = true,
        },
        new()
        {
            Key = "employee-data", Order = 4, AuditReference = "7", Title = "Employee Data Gathering Form",
            Phase = JourneyPhase.PreEmployment, Owner = StaffRole.Recruiter,
            RequestMessageKey = "employee-data-request", WatchForSender = "DocuSign",
            DeadlineQuestion = "SSO: confirm the form deadline and follow-up timing. TA/SSO: confirm who sends this email "
                + "in the future process, and when (audit #7).",
        },
        new()
        {
            Key = "background-check", Order = 5, AuditReference = "8", Title = "Background check",
            Phase = JourneyPhase.PreEmployment, Owner = StaffRole.ServiceChannel,
            VendorMessageKey = "universal-request", WatchForSender = "Universal Background Screening",
            Deadline = DeadlineRule.AfterActivation(48, DeadlineUnit.Hours),
            DeadlineQuestion = "SSO: confirm the 48-hour submission deadline and when that period begins (audit #8).",
        },
        new()
        {
            Key = "employee-health", Order = 6, AuditReference = "9", Title = "Welcome and Employee Health screening",
            Phase = JourneyPhase.PreEmployment, Owner = StaffRole.ServiceChannel,
            RequestMessageKey = "sso-welcome",
            Deadline = DeadlineRule.Clearance,
            DeadlineQuestion = "SSO: confirm the clearance deadline is six business days before the start date, and how "
                + "OEHC scheduling connects to the I-9 (audit #9).",
        },
        new()
        {
            Key = "drug-screen", Order = 7, AuditReference = "10", Title = "Drug screen",
            Phase = JourneyPhase.PreEmployment, Owner = StaffRole.ServiceChannel,
            Condition = CandidateCondition.DrugScreen,
            VendorMessageKey = "quest-request", WatchForSender = "Quest Diagnostics",
            // Message 9 gives Quest's current from address and subject.
            WatchForFromAddress = "noreply@questdiagnostics.com", WatchForSubject = "Your Drug Screen QPassport",
            Deadline = DeadlineRule.AfterActivation(48, DeadlineUnit.BusinessHours),
            DeadlineQuestion = "SSO: what does \"48 business hours\" mean, when does the clock start, and must the candidate "
                + "schedule testing or complete collection within it? (audit #9, #10)",
        },
        new()
        {
            Key = "i9", Order = 8, AuditReference = "11A", Title = "I-9 verification",
            Phase = JourneyPhase.PreEmployment, Owner = StaffRole.ServiceChannel,
            RequestMessageKey = "i9-choice", WatchForSender = "I-9 Tracker Complete",
            Deadline = DeadlineRule.Clearance,
            DeadlineQuestion = "SSO: confirm when this message is sent and how it connects to Employee Health scheduling; "
                + "confirm the in-person location, days and hours (audit #11).",
        },
        new()
        {
            Key = "i9-remote", Order = 9, AuditReference = "11B", Title = "Remote I-9 representative",
            Phase = JourneyPhase.PreEmployment, Owner = StaffRole.ServiceChannel,
            Condition = CandidateCondition.RemoteI9,
            RequestMessageKey = "i9-remote", WatchForSender = "I-9 Tracker Complete",
            Deadline = DeadlineRule.Clearance,
            DeadlineQuestion = "SSO: confirm representative eligibility, including the age requirement and whether family "
                + "members are excluded (audit #11).",
        },
        new()
        {
            Key = "dmv", Order = 10, AuditReference = "12", Title = "DMV Employer Pull Notice",
            Phase = JourneyPhase.PreEmployment, Owner = StaffRole.ServiceChannel,
            Condition = CandidateCondition.DmvPullNotice,
            RequestMessageKey = "additional-requirements", WatchForSender = "DocuSign",
            DeadlineConfirmed = true,
        },
        new()
        {
            Key = "fingerprinting", Order = 11, AuditReference = "12", Title = "Fingerprinting (Live Scan)",
            Phase = JourneyPhase.PreEmployment, Owner = StaffRole.ServiceChannel,
            Condition = CandidateCondition.Fingerprinting,
            RequestMessageKey = "additional-requirements",
            Deadline = DeadlineRule.Clearance,
            DeadlineQuestion = "SSO: before an employee starts, must fingerprinting be scheduled, attended or cleared? "
                + "Confirm the requirement and its deadline (audit #12).",
        },
        new()
        {
            Key = "position-description", Order = 12, AuditReference = "12", Title = "Position description",
            Phase = JourneyPhase.PreEmployment, Owner = StaffRole.ServiceChannel,
            Condition = CandidateCondition.PositionDescription,
            RequestMessageKey = "additional-requirements", WatchForSender = "DocuSign",
            DeadlineConfirmed = true,
        },
        new()
        {
            Key = "parking", Order = 13, AuditReference = "12", Title = "Complimentary parking",
            Phase = JourneyPhase.PreEmployment, Owner = StaffRole.ServiceChannel,
            Condition = CandidateCondition.Parking,
            RequestMessageKey = "additional-requirements",
            DeadlineConfirmed = true,
        },
        new()
        {
            Key = "cleared", Order = 14, AuditReference = "13", Title = "Cleared to start",
            Phase = JourneyPhase.FirstDay, Owner = StaffRole.ServiceChannel,
            RequestMessageKey = "cleared", WatchForSender = "DocuSign",
            // Message 13: "Complete before your first day".
            Deadline = DeadlineRule.BeforeStart(1, DeadlineUnit.CalendarDays),
            DeadlineQuestion = "SSO: which forms are in DocuSign and which are separate attachments, and the required "
                + "action and deadline for each (audit #13).",
        },
        new()
        {
            Key = "new-employee-welcome", Order = 15, AuditReference = "14", Title = "New Employee Welcome",
            Phase = JourneyPhase.FirstDay, Owner = StaffRole.UnitHr,
            WatchForSender = "Learning and Development",
            // Message 13: "attendance is required within your first 30 days of employment".
            Deadline = DeadlineRule.AfterStart(30, DeadlineUnit.CalendarDays),
            DeadlineConfirmed = true,
        },
    ];

    /// <summary>Unknown values are null on purpose: a message that needs one cannot be sent until someone answers.</summary>
    public static IReadOnlyList<AppSetting> Settings() =>
    [
        new()
        {
            Key = SettingKeys.ClearanceBusinessDaysBeforeStart, Value = "6", Kind = SettingKind.Integer,
            Description = "Business days before the start date that every pre-employment clearance must be complete.",
            OpenQuestion = "SSO: confirm the clearance deadline is six business days before the start date (audit #9).",
        },
        new()
        {
            Key = SettingKeys.ReferenceSurveyBusinessDays, Kind = SettingKind.Integer,
            Description = "Business days references have to complete SkillSurvey, counted from the reference request.",
            OpenQuestion = "TA: confirm the reference response deadline (audit #1).",
        },
        new()
        {
            Key = SettingKeys.QualificationSubmissionMethod, Kind = SettingKind.Text,
            Description = "How candidates send license, certification and degree documents (message 4).",
            OpenQuestion = "TA: what is the approved submission process? (audit #4)",
        },
        new()
        {
            Key = SettingKeys.EmployeeDataFormUrl, Kind = SettingKind.Url,
            Description = "Link to the Employee Data Gathering Form (message 7).",
            OpenQuestion = "Form owner: confirm the form link once duplicate screens are removed (audit #7).",
        },
        new()
        {
            Key = SettingKeys.EmployeeDataFormContact, Kind = SettingKind.Text,
            Description = "Who answers questions about the Employee Data Gathering Form (message 7).",
            OpenQuestion = "TA/SSO: confirm who sends this email and answers questions about the form (audit #7).",
        },
        new()
        {
            Key = SettingKeys.OehcFormUrl, Kind = SettingKind.Url,
            Description = "Link to the Employee Health (OEHC) appointment request form (message 9).",
            OpenQuestion = "SSO: provide the OEHC appointment/request form link (audit #9).",
        },
        new()
        {
            Key = SettingKeys.FingerprintSchedulingUrl, Kind = SettingKind.Url,
            Description = "Link for scheduling fingerprinting with the UC Davis Health or Campus police department (message 12).",
            OpenQuestion = "SSO: provide the fingerprinting scheduling link (audit #12).",
        },
    ];

    /// <summary>
    /// UC Davis observed holidays from the Academic &amp; Administrative Calendar
    /// (dates.ucdavis.edu, read 2026-10-22). Extend each year; business-day deadlines past the last
    /// entry silently miss holidays.
    /// </summary>
    public static IReadOnlyList<Holiday> Holidays() =>
    [
        new() { Date = new(2026, 11, 11), Name = "Veterans Day" },
        new() { Date = new(2026, 11, 26), Name = "Thanksgiving Holiday" },
        new() { Date = new(2026, 11, 27), Name = "Thanksgiving Holiday" },
        new() { Date = new(2026, 12, 24), Name = "Winter Holiday" },
        new() { Date = new(2026, 12, 25), Name = "Winter Holiday" },
        new() { Date = new(2026, 12, 31), Name = "Winter Holiday" },
        new() { Date = new(2027, 1, 1), Name = "Winter Holiday" },
        new() { Date = new(2027, 1, 18), Name = "Martin Luther King Jr. Day" },
        new() { Date = new(2027, 2, 15), Name = "Presidents' Day" },
        new() { Date = new(2027, 3, 26), Name = "César Chávez Day" },
        new() { Date = new(2027, 5, 31), Name = "Memorial Day" },
        // The calendar lists Saturday, June 19; confirm whether UC observes it on Friday, June 18.
        new() { Date = new(2027, 6, 19), Name = "Juneteenth" },
        new() { Date = new(2027, 7, 5), Name = "Independence Day" },
        new() { Date = new(2027, 9, 6), Name = "Labor Day" },
    ];

    public static IReadOnlyList<Team> Teams() =>
    [
        new() { Key = "i9-desk", Name = "SSO I-9 desk" },
        new() { Key = "health-pd", Name = "UC Davis Health Police Department" },
        new() { Key = "campus-pd", Name = "UC Davis Campus Police Department" },
        new() { Key = "oehc", Name = "Employee Health (OEHC)" },
        new() { Key = "learning", Name = "Learning and Development" },
    ];

    /// <summary>A location to seed, by its team's key.</summary>
    public sealed record SeedLocation(string TypeKey, string TeamKey, string Name, string? Address, IReadOnlyList<WeeklyHours> Hours);

    /// <summary>
    /// What candidates book. Windows quote the audit where it gives one; the rest is flagged
    /// unconfirmed. Hours are seeded only where the audit states them (I-9: Wednesdays 8–2).
    /// </summary>
    public static IReadOnlyList<AppointmentType> AppointmentTypes() =>
    [
        new()
        {
            Key = "i9-in-person", Name = "In-person I-9 verification", StepKey = "i9",
            Latest = DeadlineRule.Clearance, CompletesStepOnAttendance = true,
            WindowQuestion = "SSO: confirm the in-person location, days and hours, how many candidates one slot takes, and "
                + "the latest an I-9 appointment may be (audit #11).",
        },
        new()
        {
            Key = "fingerprinting", Name = "Fingerprinting (Live Scan)", StepKey = "fingerprinting",
            // Message 12: "the earliest appointment that allows you to meet your pre-employment clearance deadline".
            Latest = DeadlineRule.Clearance,
            WindowQuestion = "SSO: before an employee starts, must fingerprinting be scheduled, attended or cleared? Police "
                + "departments: confirm hours, addresses and that appointments can be booked here (audit #12).",
        },
        new()
        {
            Key = "employee-health", Name = "Employee Health screening", StepKey = "employee-health",
            // Message 9: "You must attend the appointment at least two weeks before your start date."
            Latest = DeadlineRule.BeforeStart(14, DeadlineUnit.CalendarDays),
            WindowQuestion = "OEHC: confirm hours, the address, and booking here instead of calling the candidate. SSO: "
                + "confirm OEHC timing and its connection to the I-9 (audit #9).",
        },
        new()
        {
            Key = "new-employee-welcome", Name = "New Employee Welcome", StepKey = "new-employee-welcome",
            // Message 13: "attendance is required within your first 30 days of employment".
            Earliest = DeadlineRule.AfterStart(0, DeadlineUnit.CalendarDays),
            Latest = DeadlineRule.AfterStart(30, DeadlineUnit.CalendarDays),
            CompletesStepOnAttendance = true,
            WindowQuestion = "L&D: confirm session dates and seats, and whether candidates may choose a session (L&D assigns "
                + "one automatically today) (audit #13–14).",
        },
    ];

    public static IReadOnlyList<SeedLocation> Locations() =>
    [
        new("i9-in-person", "i9-desk", "Rancho Cordova", "10850 White Rock Road, Rancho Cordova, CA 95670",
        [
            // Message 11A: "Appointments are available on Wednesdays from 8 a.m.–2 p.m." The 15-minute length and
            // one-at-a-time capacity are assumptions (docs/PLAN.md).
            new() { Day = DayOfWeek.Wednesday, Start = new(8, 0), End = new(14, 0), SlotMinutes = 15, Capacity = 1 },
        ]),
        new("fingerprinting", "health-pd", "UC Davis Health Police Department, Sacramento", null, []),
        new("fingerprinting", "campus-pd", "UC Davis Campus Police Department, Davis", null, []),
        new("employee-health", "oehc", "Employee Health (OEHC), Sacramento", null, []),
        new("new-employee-welcome", "learning", "Education Building, UC Davis Health Sacramento campus", null, []),
    ];

    /// <summary>The audit's rewritten messages, from the Markdown files embedded under Seed/Messages.</summary>
    public static IReadOnlyList<MessageTemplateFile> Messages()
    {
        var assembly = typeof(JourneyCatalog).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Seed.Messages.", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n => MessageTemplateFile.Parse(Read(assembly, n), n))
            .ToList();
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
