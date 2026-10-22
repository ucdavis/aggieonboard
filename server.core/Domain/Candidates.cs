namespace Server.Core.Domain;

/// <summary>
/// A person moving through onboarding. Deliberately thin: no SSN, birth date, driver's license,
/// address history or documents — those stay in the vendor systems (AGENTS.md, invariant 1).
/// </summary>
public class Candidate
{
    public int Id { get; set; }

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";

    public string PositionTitle { get; set; } = "";
    public string Department { get; set; } = "";

    /// <summary>Who answers first-day questions (message 13). Free text: a name and an email or phone.</summary>
    public string? HiringManagerContact { get; set; }

    /// <summary>Tentative until clearance (message 9). Null before an offer is accepted.</summary>
    public DateOnly? AnticipatedStartDate { get; set; }

    /// <summary>Assigned once the candidate is in UCPath (message 13).</summary>
    public string? EmployeeId { get; set; }

    public int? RecruitingAssistantId { get; set; }
    public StaffMember? RecruitingAssistant { get; set; }
    public int? RecruiterId { get; set; }
    public StaffMember? Recruiter { get; set; }
    public int? OnboardingSpecialistId { get; set; }
    public StaffMember? OnboardingSpecialist { get; set; }

    // Position requirements: each one adds steps, or sections of message 12.
    public bool RequiresDrugScreen { get; set; }
    public bool RequiresDmvPullNotice { get; set; }
    public bool RequiresFingerprinting { get; set; }
    public bool RequiresPositionDescription { get; set; }
    public bool EligibleForParking { get; set; }

    /// <summary>The candidate's answer to message 11A. Decides whether the remote I-9 step applies.</summary>
    public I9Method I9Method { get; set; }

    public List<CandidateQualification> Qualifications { get; set; } = [];
    public List<CandidateStep> Steps { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public bool Meets(CandidateCondition condition) => condition switch
    {
        CandidateCondition.Always => true,
        CandidateCondition.HasQualifications => Qualifications.Count > 0,
        CandidateCondition.HasLicenses => Qualifications.Any(q => q.Kind != QualificationKind.Degree),
        CandidateCondition.HasDegrees => Qualifications.Any(q => q.Kind == QualificationKind.Degree),
        CandidateCondition.DrugScreen => RequiresDrugScreen,
        CandidateCondition.DmvPullNotice => RequiresDmvPullNotice,
        CandidateCondition.Fingerprinting => RequiresFingerprinting,
        CandidateCondition.PositionDescription => RequiresPositionDescription,
        CandidateCondition.Parking => EligibleForParking,
        CandidateCondition.RemoteI9 => I9Method == I9Method.Remote,
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null),
    };
}

public enum I9Method
{
    NotChosen,
    InPerson,
    Remote,
}

/// <summary>A license, certification or degree the position requires, for message 4's lists.</summary>
public class CandidateQualification
{
    /// <summary>What message 4 asks for a degree, verbatim.</summary>
    public const string DegreeDocument = "An official transcript showing the degree awarded and the date it was conferred";

    public int Id { get; set; }
    public int CandidateId { get; set; }
    public QualificationKind Kind { get; set; }

    /// <summary>"Registered Nurse (California)".</summary>
    public string Name { get; set; } = "";

    /// <summary>What to send for it: "Copy of your active license", or for a degree the audit's transcript wording.</summary>
    public string DocumentToSubmit { get; set; } = "";
}

public enum QualificationKind
{
    License,
    Certification,
    Degree,
}

/// <summary>Where one candidate is on one step.</summary>
public class CandidateStep
{
    public int Id { get; set; }
    public int CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public int StepDefinitionId { get; set; }
    public StepDefinition? Step { get; set; }

    public StepStatus Status { get; set; }

    /// <summary>When the step became the candidate's to act on. The clock for activation deadlines starts here.</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>Pacific date the step is due. Null when the step has no deadline.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Pacific time of day for hour-based deadlines; null means end of <see cref="DueDate"/>.</summary>
    public TimeOnly? DueTime { get; set; }

    /// <summary>True when staff set the due date by hand; recomputing the rule leaves it alone.</summary>
    public bool DueOverridden { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
    public int? CompletedById { get; set; }
    public StaffMember? CompletedBy { get; set; }

    /// <summary>Staff-only. Never shown in the portal.</summary>
    public string? StaffNote { get; set; }
}

public enum StepStatus
{
    /// <summary>Not started.</summary>
    Upcoming,

    /// <summary>The candidate has something to do.</summary>
    ActionNeeded,

    /// <summary>The candidate has done their part; waiting on staff or a vendor.</summary>
    Waiting,

    Complete,

    /// <summary>Does not apply to this candidate.</summary>
    Skipped,

    /// <summary>Stuck on something outside the normal flow; see the staff note.</summary>
    Blocked,
}
