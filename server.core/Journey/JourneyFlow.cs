using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Scheduling;

namespace Server.Core.Journey;

/// <summary>
/// Pulls the new employee along: steps are grouped into stages (<see cref="StepDefinition.Stage"/>),
/// and once every step of a stage is complete or skipped the next stage opens on its own. Each step
/// opened this way gets a held email (<see cref="TaskNotification"/>) that goes only if the new
/// employee isn't in their checklist to see it (NewTaskEmails).
/// </summary>
public static class JourneyFlow
{
    /// <summary>The lowest stage with a step still to finish, or null when the journey is done.</summary>
    public static int? CurrentStage(Candidate candidate) =>
        candidate.Steps.Where(s => s.Status is not (StepStatus.Complete or StepStatus.Skipped)).Min(s => (int?)s.Step!.Stage);

    /// <summary>Opens every upcoming step of the current stage or earlier. Returns the steps it opened.</summary>
    public static IReadOnlyList<CandidateStep> OpenCurrentStage(Candidate candidate, DateTimeOffset now, DeadlineCalculator deadlines)
    {
        if (CurrentStage(candidate) is not { } stage) return [];

        // Upcoming means it applies (a step that doesn't is skipped), and opening never completes one,
        // so a single pass is enough.
        var opened = candidate.Steps
            .Where(s => s.Status == StepStatus.Upcoming && s.Step!.Stage <= stage)
            .OrderBy(s => s.Step!.Order)
            .ToList();
        foreach (var step in opened) CandidateJourney.Activate(candidate, step, now, deadlines);
        return opened;
    }

    /// <summary>
    /// Opens the current stage after a change that could finish one or add a step, and records it:
    /// a history line and a held email for each step opened. The caller saves; the candidate must
    /// already be saved, so its steps have IDs.
    /// </summary>
    public static IReadOnlyList<CandidateStep> Advance(
        AppDbContext db, Candidate candidate, DateTimeOffset now, DeadlineCalculator deadlines, StaffMember? actor, string actorName)
    {
        var opened = OpenCurrentStage(candidate, now, deadlines);
        foreach (var step in opened)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                CandidateId = candidate.Id,
                At = now,
                ActorStaffId = actor is { Id: > 0 } ? actor.Id : null,
                ActorName = actorName,
                Action = "step.activated",
                Detail = $"{step.Step!.Title}: upcoming → action needed (stage {step.Step.Stage} opened)",
            });
            db.TaskNotifications.Add(new TaskNotification { CandidateId = candidate.Id, CandidateStep = step, OpenedAt = now });
        }
        return opened;
    }
}
