namespace Server.Core.Domain;

/// <summary>
/// A process fact the journey depends on: a link, a contact, a number of days. Most come from the
/// audit's open questions, so each records whether it is confirmed and what is still being asked.
/// A null <see cref="Value"/> means nobody knows yet — a message that needs it cannot be sent.
/// </summary>
public class AppSetting
{
    /// <summary>One of <see cref="SettingKeys"/>.</summary>
    public string Key { get; set; } = "";

    public string? Value { get; set; }
    public SettingKind Kind { get; set; }
    public string Description { get; set; } = "";
    public bool IsConfirmed { get; set; }

    /// <summary>What TA, SSO or a vendor still has to answer, with the audit message number.</summary>
    public string? OpenQuestion { get; set; }
}

public enum SettingKind
{
    Text,
    Url,
    Integer,
}

public static class SettingKeys
{
    /// <summary>Business days before the start date that every clearance must be complete (audit #9).</summary>
    public const string ClearanceBusinessDaysBeforeStart = "ClearanceBusinessDaysBeforeStart";

    /// <summary>Business days references have to answer SkillSurvey, counted from the request (audit #1).</summary>
    public const string ReferenceSurveyBusinessDays = "ReferenceSurveyBusinessDays";

    /// <summary>How candidates send qualification documents (audit #4).</summary>
    public const string QualificationSubmissionMethod = "QualificationSubmissionMethod";

    public const string EmployeeDataFormUrl = "EmployeeDataFormUrl";

    /// <summary>Who answers questions about the Employee Data Gathering Form (audit #7).</summary>
    public const string EmployeeDataFormContact = "EmployeeDataFormContact";

    public const string OehcFormUrl = "OehcFormUrl";
    public const string FingerprintSchedulingUrl = "FingerprintSchedulingUrl";

    /// <summary>Minutes a new employee must be away from their checklist before a newly opened task is emailed.</summary>
    public const string NewTaskEmailDelayMinutes = "NewTaskEmailDelayMinutes";

    public static readonly string[] All =
    [
        ClearanceBusinessDaysBeforeStart,
        ReferenceSurveyBusinessDays,
        QualificationSubmissionMethod,
        EmployeeDataFormUrl,
        EmployeeDataFormContact,
        OehcFormUrl,
        FingerprintSchedulingUrl,
        NewTaskEmailDelayMinutes,
    ];
}

/// <summary>A UC Davis holiday. Business-day deadlines skip these and weekends.</summary>
public class Holiday
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}
