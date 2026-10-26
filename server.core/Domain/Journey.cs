namespace Server.Core.Domain;

/// <summary>
/// One tracked step of the candidate journey: something a candidate does, or waits on, between
/// "you're a top candidate" and their first day. Steps are what staff advance and the portal lists;
/// the emails about them are <see cref="MessageTemplate"/>s, because the two don't line up one to
/// one — message 12 covers four requirements, and the qualification step has a request and an
/// acknowledgment.
/// </summary>
public class StepDefinition
{
    public int Id { get; set; }

    /// <summary>Stable identifier ("drug-screen"), used by seeds, merge fields and code. Unique.</summary>
    public string Key { get; set; } = "";

    /// <summary>Position in the journey. Staff see the first step that is neither complete nor skipped as current.</summary>
    public int Order { get; set; }

    /// <summary>The message number(s) in the communications audit this step comes from ("4–5", "12").</summary>
    public string AuditReference { get; set; } = "";

    public string Title { get; set; } = "";
    public JourneyPhase Phase { get; set; }

    /// <summary>The staff role that moves this step along.</summary>
    public StaffRole Owner { get; set; }

    /// <summary>When the step applies. A candidate for whom it is false gets the step as Skipped.</summary>
    public CandidateCondition Condition { get; set; }

    /// <summary>The message that asks the candidate to do this step. Sending it activates the step.</summary>
    public string? RequestMessageKey { get; set; }

    /// <summary>The message sent when what the candidate sent arrives (message 5).</summary>
    public string? AcknowledgmentMessageKey { get; set; }

    /// <summary>A vendor's email for this step, kept as communications' proposed wording; never sent by the app.</summary>
    public string? VendorMessageKey { get; set; }

    /// <summary>
    /// Steps with the same stage open together; the next stage opens on its own once every step of
    /// this one is complete or skipped (JourneyFlow). Stage 5 is all of pre-employment.
    /// </summary>
    public int Stage { get; set; }

    /// <summary>What the new employee is asked to do, as the portal's button says it: "Schedule your fingerprinting".</summary>
    public string TaskLabel { get; set; } = "";

    /// <summary>
    /// What the portal says about the task when the step has no message of its own to show (a vendor
    /// step, the offer, New Employee Welcome). Our wording, listed under Copy to review.
    /// </summary>
    public string? TaskSummary { get; set; }

    public DeadlineRule Deadline { get; set; } = DeadlineRule.None;

    /// <summary>
    /// False while the audit's question about this deadline is unanswered. The rule still applies;
    /// staff see it flagged.
    /// </summary>
    public bool DeadlineConfirmed { get; set; }

    /// <summary>The audit's open question about this step's timing, verbatim enough to chase.</summary>
    public string? DeadlineQuestion { get; set; }

    /// <summary>
    /// The outside system that emails the candidate for this step (SkillSurvey, Universal, Quest…),
    /// so the portal can say what to watch for. Null when only the app emails.
    /// </summary>
    public string? WatchForSender { get; set; }

    /// <summary>The vendor's from address, when known. Helps the candidate search spam.</summary>
    public string? WatchForFromAddress { get; set; }

    /// <summary>The subject line the vendor actually sends today, when known — not a proposed rewrite.</summary>
    public string? WatchForSubject { get; set; }
}

public enum JourneyPhase
{
    TalentAcquisition,
    PreEmployment,
    FirstDay,
}

/// <summary>
/// The groups that own the journey's steps (decided 2026-10-26). Recruiters (Talent Acquisition, with the
/// recruiting assistants) own steps 1–4; Service Channel owns pre-employment through "cleared to start"
/// (5–14) and hands the new employee back to Unit HR, which owns the first day and New Employee
/// Welcome (15, with L&amp;D).
/// </summary>
public enum StaffRole
{
    Recruiter,
    ServiceChannel,
    UnitHr,

    /// <summary>
    /// Runs appointments only (a police department, Employee Health): signs in to the schedule,
    /// never to the pipeline or candidate records. Owns no journey step.
    /// </summary>
    TeamMember,
}

/// <summary>
/// A yes/no fact about a candidate that decides whether a step applies, or whether a section of a
/// message is included.
/// </summary>
public enum CandidateCondition
{
    Always,

    /// <summary>The position requires at least one license, certification or degree to be verified.</summary>
    HasQualifications,

    /// <summary>At least one license or certification (message 4's first list).</summary>
    HasLicenses,

    /// <summary>At least one degree (message 4's Education list).</summary>
    HasDegrees,

    DrugScreen,
    DmvPullNotice,
    Fingerprinting,
    PositionDescription,
    Parking,

    /// <summary>The candidate chose remote I-9 verification (message 11A's reply).</summary>
    RemoteI9,
}

/// <summary>
/// How a step's due date is computed. Owned by <see cref="StepDefinition"/>. Every rule is data so
/// that SSO's and TA's answers to the audit are edits, not releases.
/// </summary>
/// <remarks>
/// The shorthands return a new instance each time: EF tracks an owned value by reference, so two
/// steps sharing one instance would fail to save.
/// </remarks>
public record DeadlineRule(DeadlineAnchor Anchor, int Amount, DeadlineUnit Unit)
{
    public static DeadlineRule None => new(DeadlineAnchor.None, 0, DeadlineUnit.CalendarDays);

    /// <summary>Counted from the moment the step became the candidate's to act on.</summary>
    public static DeadlineRule AfterActivation(int amount, DeadlineUnit unit) => new(DeadlineAnchor.Activation, amount, unit);

    /// <summary>The pre-employment clearance deadline, itself set by <see cref="SettingKeys.ClearanceBusinessDaysBeforeStart"/>.</summary>
    public static DeadlineRule Clearance => new(DeadlineAnchor.Clearance, 0, DeadlineUnit.BusinessDays);

    public static DeadlineRule BeforeStart(int amount, DeadlineUnit unit) => new(DeadlineAnchor.BeforeStart, amount, unit);
    public static DeadlineRule AfterStart(int amount, DeadlineUnit unit) => new(DeadlineAnchor.AfterStart, amount, unit);
}

public enum DeadlineAnchor
{
    /// <summary>No computed deadline; staff enter one when it applies.</summary>
    None,
    Activation,
    Clearance,
    BeforeStart,
    AfterStart,
}

public enum DeadlineUnit
{
    /// <summary>Clock hours ("within 48 hours").</summary>
    Hours,

    /// <summary>
    /// Hours that elapse only on business days ("48 business hours"). Whether SSO means this,
    /// or office hours, is an open question (audit #8, #10).
    /// </summary>
    BusinessHours,

    CalendarDays,
    BusinessDays,
    Weeks,
}
