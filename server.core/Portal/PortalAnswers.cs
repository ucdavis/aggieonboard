using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Messaging;
using Server.Core.Scheduling;

namespace Server.Core.Portal;

/// <summary>Step 8: how the candidate will verify their documents. <see cref="CanChoose"/> offers in person (book) or remote.</summary>
public sealed record PortalI9Choice(I9Method Method, bool CanChoose);

/// <summary>Step 9: the remote I-9 representative, offered while the step is open.</summary>
public sealed record PortalRepresentative(string? Name, string? Email);

/// <summary>Step 11: the fingerprinting ATI number, offered once the appointment has happened.</summary>
public sealed record PortalAtiNumber(string? Value);

public sealed record PortalAnswerViews(PortalI9Choice? I9Choice, PortalRepresentative? Representative, PortalAtiNumber? AtiNumber);

/// <summary>
/// The answers messages 11A, 11B and 12 used to ask for by reply, given in the portal instead. Like every
/// portal call, each takes the candidate from the session (AGENTS.md, invariant 2). An answer moves its
/// step to waiting: the candidate has done their part, and staff take it from there.
/// </summary>
public sealed class PortalAnswers(AppDbContext db, TimeProvider clock, MessageService messages)
{
    public const string I9Step = "i9";
    public const string RemoteI9Step = "i9-remote";
    public const string FingerprintingStep = "fingerprinting";
    public const string RemoteI9Message = "i9-remote";

    private const string Actor = "Candidate";

    /// <summary>What each answering step offers. The portal shows exactly this.</summary>
    public async Task<PortalAnswerViews> GetViewsAsync(Candidate candidate, CancellationToken ct = default)
    {
        var appointments = await AppointmentsAsync(candidate.Id, ct);

        var i9 = Step(candidate, I9Step);
        var remote = Step(candidate, RemoteI9Step);
        var fingerprinting = Step(candidate, FingerprintingStep);

        return new PortalAnswerViews(
            IsOpen(i9) ? new PortalI9Choice(candidate.I9Method, CanChooseI9(candidate, appointments)) : null,
            IsOpen(remote) ? new PortalRepresentative(candidate.I9RepresentativeName, candidate.I9RepresentativeEmail) : null,
            CanEnterAtiNumber(candidate, fingerprinting, appointments) ? new PortalAtiNumber(candidate.FingerprintAtiNumber) : null);
    }

    /// <summary>
    /// "Remote" in answer to message 11A. Starts the representative step and sends 11B, signed by the
    /// candidate's specialist. The choice stands even if the email can't go; the history says why.
    /// </summary>
    public async Task ChooseRemoteI9Async(int candidateId, CancellationToken ct = default)
    {
        var candidate = await LoadAsync(candidateId, ct);
        var i9 = Step(candidate, I9Step);
        if (!IsOpen(i9))
        {
            throw new JourneyRuleException("You can choose how to verify your documents once we ask you to. We'll email you when it's time.");
        }
        if (candidate.I9Method == I9Method.Remote) return;
        if (!CanChooseI9(candidate, await AppointmentsAsync(candidateId, ct)))
        {
            throw new JourneyRuleException("Cancel your in-person appointment first, then choose remote verification.");
        }

        candidate.I9Method = I9Method.Remote;
        CandidateJourney.ApplyConditions(candidate);
        Audit(candidateId, "answer.i9-method", "Chose remote I-9 verification in the portal.");
        MarkWaiting(candidateId, i9!);

        var remote = Step(candidate, RemoteI9Step)!;
        if (remote.Status == StepStatus.Upcoming)
        {
            CandidateJourney.Activate(candidate, remote, clock.GetUtcNow(), await JourneyData.LoadDeadlinesAsync(db, ct));
            Audit(candidateId, "step.activated", $"{remote.Step!.Title}: upcoming → action needed (remote verification chosen)");
        }
        await db.SaveChangesAsync(ct);

        await messages.SendOnCandidateAnswerAsync(candidateId, RemoteI9Message, ct);
    }

    /// <summary>Message 11B's answer: the representative's name and email. Can be corrected while the step is open.</summary>
    public async Task SetRepresentativeAsync(int candidateId, string? name, string? email, CancellationToken ct = default)
    {
        var candidate = await LoadAsync(candidateId, ct);
        var step = Step(candidate, RemoteI9Step);
        if (candidate.I9Method != I9Method.Remote || !IsOpen(step))
        {
            throw new JourneyRuleException("You can name a representative once you've chosen remote verification.");
        }

        var errors = new Dictionary<string, string[]>();
        CandidateAnswers.ValidateRepresentative(name, email, (field, message) => errors[field] = [message], "name", "email", required: true);
        if (errors.Count > 0) throw new JourneyValidationException(errors);

        var previous = candidate.I9RepresentativeName is null ? null : $"{candidate.I9RepresentativeName} ({candidate.I9RepresentativeEmail})";
        candidate.I9RepresentativeName = name!.Trim();
        candidate.I9RepresentativeEmail = email!.Trim();
        var current = $"{candidate.I9RepresentativeName} ({candidate.I9RepresentativeEmail})";
        if (current != previous)
        {
            Audit(candidateId, "answer.i9-representative", previous is null
                ? $"Entered their I-9 representative in the portal: {current}."
                : $"Changed their I-9 representative in the portal from {previous} to {current}.");
        }
        MarkWaiting(candidateId, step!);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Message 12's last fingerprinting answer: the ATI number, once the appointment has happened.</summary>
    public async Task SetAtiNumberAsync(int candidateId, string? atiNumber, CancellationToken ct = default)
    {
        var candidate = await LoadAsync(candidateId, ct);
        var step = Step(candidate, FingerprintingStep);
        if (!CanEnterAtiNumber(candidate, step, await AppointmentsAsync(candidateId, ct)))
        {
            throw new JourneyRuleException("You can enter your ATI number after your fingerprinting appointment.");
        }

        var value = CandidateAnswers.NormalizeAtiNumber(atiNumber);
        var errors = new Dictionary<string, string[]>();
        if (value is null) errors["atiNumber"] = ["Enter your ATI number."];
        CandidateAnswers.ValidateAtiNumber(value, (field, message) => errors[field] = [message], "atiNumber");
        if (errors.Count > 0) throw new JourneyValidationException(errors);

        var previous = candidate.FingerprintAtiNumber;
        candidate.FingerprintAtiNumber = value;
        if (value != previous)
        {
            Audit(candidateId, "answer.ati-number", previous is null
                ? $"Entered their fingerprinting ATI number in the portal: {value}."
                : $"Changed their fingerprinting ATI number in the portal from {previous} to {value}.");
        }
        MarkWaiting(candidateId, step!);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// "I've done this" for a task done in another system (the prototype's stand-in for that system
    /// telling us). Moves the step to waiting; staff confirm it, and the last confirmation of a stage
    /// opens the next.
    /// </summary>
    public async Task MarkDoneAsync(int candidateId, string key, CancellationToken ct = default)
    {
        var candidate = await LoadAsync(candidateId, ct);
        var step = candidate.Steps.SingleOrDefault(s => s.Step!.Key == key && s.Status != StepStatus.Skipped);
        if (step is null || key is I9Step or RemoteI9Step || await db.AppointmentTypes.AnyAsync(t => t.StepKey == key, ct))
        {
            throw new JourneyRuleException("This task is done right here in your checklist.");
        }
        if (step.Status == StepStatus.Waiting || step.Status == StepStatus.Complete) return;
        if (step.Status != StepStatus.ActionNeeded)
        {
            throw new JourneyRuleException("This task isn't open yet. We'll let you know when it is.");
        }

        Audit(candidateId, "task.done", $"Marked “{(step.Step!.TaskLabel.Length > 0 ? step.Step.TaskLabel : step.Step.Title)}” done in the portal.");
        MarkWaiting(candidateId, step);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>In person or remote, until an in-person appointment is booked or remote is chosen. Switching back is for staff.</summary>
    private static bool CanChooseI9(Candidate candidate, IReadOnlyList<Appointment> appointments) =>
        candidate.I9Method != I9Method.Remote
        && !appointments.Any(a => a.AppointmentType!.StepKey == I9Step && a.Status == AppointmentStatus.Booked);

    /// <summary>
    /// Once the fingerprinting appointment has started or been marked attended, or if a number is already
    /// in (to correct it). Step 11: whether clearance needs anything more is open (audit #12).
    /// </summary>
    private bool CanEnterAtiNumber(Candidate candidate, CandidateStep? step, IReadOnlyList<Appointment> appointments)
    {
        if (!IsOpen(step)) return false;
        if (candidate.FingerprintAtiNumber is not null) return true;

        var now = BusinessCalendar.ToPacific(clock.GetUtcNow());
        return appointments.Any(a => a.AppointmentType!.StepKey == FingerprintingStep
            && (a.Status == AppointmentStatus.Attended || (a.Status == AppointmentStatus.Booked && a.Start <= now)));
    }

    private void MarkWaiting(int candidateId, CandidateStep step)
    {
        if (step.Status != StepStatus.ActionNeeded) return;
        CandidateJourney.MarkWaiting(step);
        Audit(candidateId, "step.waiting", $"{step.Step!.Title}: action needed → waiting (answered in the portal)");
    }

    private static bool IsOpen(CandidateStep? step) => step?.Status is StepStatus.ActionNeeded or StepStatus.Waiting;

    private static CandidateStep? Step(Candidate candidate, string key) =>
        candidate.Steps.SingleOrDefault(s => s.Step!.Key == key && s.Status != StepStatus.Skipped);

    private async Task<Candidate> LoadAsync(int candidateId, CancellationToken ct) =>
        await JourneyData.CandidatesWithJourney(db).SingleOrDefaultAsync(c => c.Id == candidateId && c.ArchivedAt == null, ct)
            ?? throw new JourneyRuleException("Your onboarding checklist isn't available. Contact your onboarding specialist.");

    private async Task<List<Appointment>> AppointmentsAsync(int candidateId, CancellationToken ct) =>
        await db.Appointments.Include(a => a.AppointmentType).Where(a => a.CandidateId == candidateId).ToListAsync(ct);

    private void Audit(int candidateId, string action, string detail) =>
        db.AuditEvents.Add(new AuditEvent
        {
            CandidateId = candidateId,
            At = clock.GetUtcNow(),
            ActorName = Actor,
            Action = action,
            Detail = detail,
        });
}
