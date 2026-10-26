namespace Server.Core.Domain;

/// <summary>
/// A step that opened on its own (JourneyFlow), waiting to be emailed about. The email goes only once
/// the new employee has been away from their checklist for the NewTaskEmailDelayMinutes setting,
/// and not at all if the task is done by then: whoever is in the checklist already sees it.
/// </summary>
public class TaskNotification
{
    public int Id { get; set; }
    public int CandidateId { get; set; }
    public int CandidateStepId { get; set; }
    public CandidateStep? CandidateStep { get; set; }
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>When it was settled: emailed, or dropped because the task was done or already emailed.</summary>
    public DateTimeOffset? SettledAt { get; set; }

    /// <summary>"emailed", "done first", "already emailed", or why the email could not go.</summary>
    public string? Outcome { get; set; }
}
