namespace Server.Core.Domain;

/// <summary>
/// One change staff made: who, when, what. Written by every mutation of a candidate or their steps,
/// and shown as the candidate's history. Never edited or deleted.
/// </summary>
public class AuditEvent
{
    public int Id { get; set; }

    /// <summary>The candidate the change was about. Null for changes to shared data (Phase 6).</summary>
    public int? CandidateId { get; set; }

    public DateTimeOffset At { get; set; }

    public int? ActorStaffId { get; set; }

    /// <summary>Recorded as text too, so history still reads after a staff row changes.</summary>
    public string ActorName { get; set; } = "";

    /// <summary>Machine-readable kind: <c>candidate.created</c>, <c>step.completed</c>, <c>step.due-set</c>…</summary>
    public string Action { get; set; } = "";

    /// <summary>What changed, in words: "Drug screen: Upcoming → Action needed".</summary>
    public string Detail { get; set; } = "";
}
