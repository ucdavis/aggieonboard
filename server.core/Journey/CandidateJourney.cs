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

    /// <summary>Back to the step's own rule. Only an activated step has a computed due date.</summary>
    public static void ClearDueOverride(Candidate candidate, CandidateStep step, DeadlineCalculator deadlines)
    {
        step.DueOverridden = false;
        step.DueDate = null;
        step.DueTime = null;
        RecomputeDue(candidate, step, deadlines);
    }

    /// <summary>Which statuses each move starts from. The UI offers exactly these, so it cannot drift from the rules.</summary>
    public static readonly IReadOnlyDictionary<StepAction, StepStatus[]> AllowedFrom = new Dictionary<StepAction, StepStatus[]>
    {
        [StepAction.Activate] = [StepStatus.Upcoming],
        [StepAction.MarkWaiting] = [StepStatus.ActionNeeded],
        // Upcoming is allowed: some steps happen outside the app before anyone activates them.
        [StepAction.Complete] = [StepStatus.Upcoming, StepStatus.ActionNeeded, StepStatus.Waiting, StepStatus.Blocked],
        [StepAction.Block] = [StepStatus.Upcoming, StepStatus.ActionNeeded, StepStatus.Waiting],
        [StepAction.Unblock] = [StepStatus.Blocked],
        [StepAction.Reopen] = [StepStatus.Complete],
    };

    public static IReadOnlyList<StepAction> AllowedActions(StepStatus status) =>
        AllowedFrom.Where(kv => kv.Value.Contains(status)).Select(kv => kv.Key).Order().ToList();

    /// <summary>
    /// A staff move on one step. Throws <see cref="JourneyRuleException"/> when the step's status
    /// does not allow it; the message says why, for the person who clicked.
    /// </summary>
    public static void Apply(
        Candidate candidate, CandidateStep step, StepAction action, DateTimeOffset at, StaffMember? by, DeadlineCalculator deadlines)
    {
        if (!AllowedFrom[action].Contains(step.Status))
        {
            var title = step.Step?.Title ?? "This step";
            throw new JourneyRuleException($"{title} is {Describe(step.Status)}; it cannot be {Past(action)} from there.");
        }

        switch (action)
        {
            case StepAction.Activate:
                Activate(candidate, step, at, deadlines);
                break;

            case StepAction.MarkWaiting:
                MarkWaiting(step);
                break;

            case StepAction.Complete:
                step.ActivatedAt ??= at;
                Complete(step, at, by);
                break;

            case StepAction.Block:
                step.Status = StepStatus.Blocked;
                break;

            case StepAction.Unblock:
                step.Status = step.ActivatedAt is null ? StepStatus.Upcoming : StepStatus.ActionNeeded;
                break;

            case StepAction.Reopen:
                step.Status = StepStatus.ActionNeeded;
                step.CompletedAt = null;
                step.CompletedBy = null;
                step.CompletedById = null;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    /// <summary>
    /// After a requirement flag, qualification or I-9 choice changes: a step that stopped applying
    /// is skipped (unless already complete, which is history), and one that started applying comes
    /// back as upcoming. Returns the steps that changed.
    /// </summary>
    public static IReadOnlyList<CandidateStep> ApplyConditions(Candidate candidate)
    {
        var changed = new List<CandidateStep>();
        foreach (var step in candidate.Steps)
        {
            var applies = candidate.Meets(step.Step!.Condition);
            if (!applies && step.Status is not (StepStatus.Skipped or StepStatus.Complete))
            {
                step.Status = StepStatus.Skipped;
                step.ActivatedAt = null;
                step.DueDate = null;
                step.DueTime = null;
                step.DueOverridden = false;
                changed.Add(step);
            }
            else if (applies && step.Status == StepStatus.Skipped)
            {
                step.Status = StepStatus.Upcoming;
                changed.Add(step);
            }
        }
        return changed;
    }

    /// <summary>After the start date or a setting changes: recompute every open step's due date that staff did not set.</summary>
    public static void RecomputeDues(Candidate candidate, DeadlineCalculator deadlines)
    {
        foreach (var step in candidate.Steps) RecomputeDue(candidate, step, deadlines);
    }

    private static void RecomputeDue(Candidate candidate, CandidateStep step, DeadlineCalculator deadlines)
    {
        if (step.DueOverridden || step.ActivatedAt is null || step.Status is StepStatus.Complete or StepStatus.Skipped) return;

        var due = deadlines.Compute(step.Step!.Deadline, step.ActivatedAt, candidate.AnticipatedStartDate);
        step.DueDate = due?.Date;
        step.DueTime = due?.Time;
    }

    /// <summary>
    /// The candidate still owes it and the deadline has passed. A waiting step is not overdue: the
    /// candidate has done their part.
    /// </summary>
    public static bool IsOverdue(CandidateStep step, DateTimeOffset now)
    {
        if (step.Status is not (StepStatus.ActionNeeded or StepStatus.Blocked) || step.DueDate is not { } date) return false;

        var local = BusinessCalendar.ToPacific(now);
        var today = DateOnly.FromDateTime(local);
        return date < today || (date == today && step.DueTime is { } time && time < TimeOnly.FromDateTime(local));
    }

    /// <summary>The first step, in journey order, that is neither complete nor skipped. Null once the journey is done.</summary>
    public static CandidateStep? CurrentStep(Candidate candidate) =>
        candidate.Steps
            .Where(s => s.Status is not (StepStatus.Complete or StepStatus.Skipped))
            .OrderBy(s => s.Step!.Order)
            .FirstOrDefault();

    public static string Describe(StepStatus status) => status switch
    {
        StepStatus.Upcoming => "upcoming",
        StepStatus.ActionNeeded => "action needed",
        StepStatus.Waiting => "waiting",
        StepStatus.Complete => "complete",
        StepStatus.Skipped => "skipped",
        StepStatus.Blocked => "blocked",
        _ => status.ToString(),
    };

    private static string Past(StepAction action) => action switch
    {
        StepAction.Activate => "started",
        StepAction.MarkWaiting => "marked waiting",
        StepAction.Complete => "completed",
        StepAction.Block => "blocked",
        StepAction.Unblock => "unblocked",
        StepAction.Reopen => "reopened",
        _ => action.ToString(),
    };
}

public enum StepAction
{
    /// <summary>Upcoming → action needed. Starts the deadline clock.</summary>
    Activate,

    /// <summary>Action needed → waiting: the candidate has done their part.</summary>
    MarkWaiting,

    Complete,
    Block,
    Unblock,

    /// <summary>Complete → action needed, when something has to be redone.</summary>
    Reopen,
}

/// <summary>A move the journey's rules do not allow. The message is written for staff.</summary>
public sealed class JourneyRuleException(string message) : Exception(message);
