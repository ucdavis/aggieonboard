using Server.Core.Domain;
using Server.Core.Messaging;

namespace Server.Core.Journey;

// The staff API's shapes. Enums travel as their names.

public sealed record StaffSummary(int Id, string Name, string Email, StaffRole Role);

public sealed record PipelineRow(
    int Id,
    string Name,
    string PositionTitle,
    string Department,
    JourneyPhase? Phase,
    string? CurrentStepKey,
    string? CurrentStepTitle,
    StepStatus? CurrentStepStatus,
    DateOnly? NextDueDate,
    TimeOnly? NextDueTime,
    bool IsOverdue,
    int OpenSteps,
    int CompletedSteps,
    int ApplicableSteps,
    DateOnly? AnticipatedStartDate,
    string? RecruitingAssistant,
    string? Recruiter,
    string? OnboardingSpecialist);

public sealed record CandidateRequirements(
    bool DrugScreen,
    bool DmvPullNotice,
    bool Fingerprinting,
    bool PositionDescription,
    bool Parking);

public sealed record QualificationInput(QualificationKind Kind, string Name, string? DocumentToSubmit);

public sealed record CandidateInput(
    string FirstName,
    string LastName,
    string Email,
    string PositionTitle,
    string Department,
    string? HiringManagerContact,
    DateOnly? AnticipatedStartDate,
    string? EmployeeId,
    int? RecruitingAssistantId,
    int? RecruiterId,
    int? OnboardingSpecialistId,
    CandidateRequirements Requirements,
    I9Method I9Method,
    IReadOnlyList<QualificationInput> Qualifications);

public sealed record StepDetail(
    string Key,
    int Order,
    string AuditReference,
    string Title,
    JourneyPhase Phase,
    StaffRole Owner,
    CandidateCondition Condition,
    StepStatus Status,
    DateTimeOffset? ActivatedAt,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    bool DueOverridden,
    bool IsOverdue,
    string DeadlineRule,
    bool DeadlineConfirmed,
    string? DeadlineQuestion,
    string? WatchForSender,
    string? RequestMessageKey,
    string? AcknowledgmentMessageKey,
    string? VendorMessageKey,
    DateTimeOffset? CompletedAt,
    string? CompletedBy,
    string? StaffNote,
    IReadOnlyList<StepAction> AvailableActions);

public sealed record HistoryEntry(DateTimeOffset At, string ActorName, string Action, string Detail);

/// <remarks><see cref="Notice"/> reports a side effect of the change just made, such as an automatic message.</remarks>
public sealed record CandidateDetail(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    string PositionTitle,
    string Department,
    string? HiringManagerContact,
    DateOnly? AnticipatedStartDate,
    DateOnly? ClearanceDeadline,
    string? EmployeeId,
    StaffSummary? RecruitingAssistant,
    StaffSummary? Recruiter,
    StaffSummary? OnboardingSpecialist,
    CandidateRequirements Requirements,
    I9Method I9Method,
    IReadOnlyList<QualificationInput> Qualifications,
    IReadOnlyList<StepDetail> Steps,
    IReadOnlyList<HistoryEntry> History,
    IReadOnlyList<SentMessageSummary> SentMessages,
    DateTimeOffset? PortalLastSignInAt,
    DateTimeOffset CreatedAt,
    string? Notice = null);

/// <summary>Input that fails validation, by field name (camelCase, as the client sends it).</summary>
public sealed class JourneyValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The candidate details are not valid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
