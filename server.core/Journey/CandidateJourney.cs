using Server.Core.Domain;
using Server.Core.Scheduling;

namespace Server.Core.Journey;

/// <summary>
/// The moves a candidate's steps can make. Seeds use these now; the staff pipeline (Phase 2) calls
/// the same ones, so fixtures can never be in a state the app could not have produced.
/// </summary>
public static class CandidateJourney
{
    /// <summary>One step per definition, in journey order. A step whose condition the candidate does not meet starts Skipped.</summary>
    public static void AddSteps(Candidate candidate, IEnumerable<StepDefinition> definitions)
    {
        foreach (var definition in definitions.OrderBy(d => d.Order))
        {
            candidate.Steps.Add(new CandidateStep
            {
                Candidate = candidate,
                Step = definition,
                StepDefinitionId = definition.Id,
                Status = candidate.Meets(definition.Condition) ? StepStatus.Upcoming : StepStatus.Skipped,
            });
        }
    }

    /// <summary>
    /// The step becomes the candidate's to act on. Starts its deadline clock and computes the due
    /// date, unless staff already set one by hand.
    /// </summary>
    public static void Activate(Candidate candidate, CandidateStep step, DateTimeOffset at, DeadlineCalculator deadlines)
    {
        var definition = step.Step ?? throw new InvalidOperationException("Load the step definition before activating.");
        if (step.Status == StepStatus.Skipped)
        {
            throw new InvalidOperationException($"Step '{definition.Key}' does not apply to this candidate.");
        }

        step.Status = StepStatus.ActionNeeded;
        step.ActivatedAt = at;
        if (!step.DueOverridden)
        {
            var due = deadlines.Compute(definition.Deadline, at, candidate.AnticipatedStartDate);
            step.DueDate = due?.Date;
            step.DueTime = due?.Time;
        }
    }

    /// <summary>The candidate has done their part; staff or a vendor still have to finish.</summary>
    public static void MarkWaiting(CandidateStep step) => step.Status = StepStatus.Waiting;

    public static void Complete(CandidateStep step, DateTimeOffset at, StaffMember? by)
    {
        step.Status = StepStatus.Complete;
        step.CompletedAt = at;
        step.CompletedBy = by;
        step.CompletedById = by?.Id;
    }

    /// <summary>Staff set the due date themselves. It sticks through later recomputation.</summary>
    public static void OverrideDue(CandidateStep step, DateOnly date, TimeOnly? time = null)
    {
        step.DueDate = date;
        step.DueTime = time;
        step.DueOverridden = true;
    }
}
