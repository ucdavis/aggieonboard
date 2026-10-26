using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Messaging;
using Server.Core.Scheduling;

namespace Server.Core.Portal;

/// <summary>A step as the candidate sees it. Staff statuses collapse to four the candidate can act on.</summary>
public enum PortalStepState
{
    /// <summary>Something for the candidate to do (staff: action needed).</summary>
    ToDo,

    /// <summary>With UC Davis Health or a vendor (staff: waiting or blocked). Nothing for the candidate to do.</summary>
    InProgress,

    Upcoming,
    Done,
}

public sealed record PortalWatchFor(string Sender, string? FromAddress, string? Subject);

public sealed record PortalContact(string Name, string Details);

public sealed record PortalMessageLink(int Id, string Subject, DateTimeOffset SentAt);

/// <summary>How the new employee does a task: in another system for now (External), or right in the checklist.</summary>
public enum PortalTaskKind
{
    /// <summary>Done elsewhere today (DocuSign, a vendor form); the prototype asks them to say when it's done.</summary>
    External,

    /// <summary>Book a time here.</summary>
    Schedule,

    /// <summary>Step 8: book the in-person desk, or choose remote.</summary>
    I9,

    /// <summary>Step 9: name the remote I-9 representative.</summary>
    Representative,
}

public sealed record PortalStep(
    string Key,
    string Title,
    JourneyPhase Phase,
    PortalStepState State,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    bool IsOverdue,
    PortalWatchFor? WatchFor,
    PortalContact? Contact,
    IReadOnlyList<PortalMessageLink> Messages,
    DateTimeOffset? CompletedAt,
    PortalBooking? Booking,
    PortalI9Choice? I9Choice = null,
    PortalRepresentative? Representative = null,
    PortalAtiNumber? AtiNumber = null,
    string TaskLabel = "",
    PortalTaskKind Kind = PortalTaskKind.External,
    string? Summary = null,
    int Stage = 0);

public sealed record PortalView(
    string FirstName,
    string PositionTitle,
    string Department,
    DateOnly? AnticipatedStartDate,
    DateOnly? ClearanceDeadline,
    IReadOnlyList<PortalStep> Steps,
    string? ViewedBy = null,
    int StepsDone = 0,
    int StepsTotal = 0,
    PortalContact? Contact = null);

/// <summary>A task's instructions: the text of its email, filled in for this candidate, or null to show the summary.</summary>
public sealed record PortalTaskBody(string Key, string? Html);

public sealed record PortalMessage(int Id, string Subject, DateTimeOffset SentAt, string Html, string Text);

/// <summary>
/// The candidate's own view of their journey. Every method takes the candidate ID from the signed-in
/// session and reads nothing that belongs to anyone else (AGENTS.md, invariant 2). No staff notes,
/// no internal statuses, no skipped steps.
/// </summary>
public sealed class PortalService(
    AppDbContext db, TimeProvider clock, PortalTokens tokens, MessageService messages, SchedulingService scheduling, PortalAnswers answers)
{
    public async Task<PortalView?> GetViewAsync(int candidateId, CancellationToken ct = default)
    {
        var candidate = await JourneyData.CandidatesWithJourney(db)
            .SingleOrDefaultAsync(c => c.Id == candidateId && c.ArchivedAt == null, ct);
        if (candidate is null) return null;

        var deadlines = await JourneyData.LoadDeadlinesAsync(db, ct);
        var sent = await db.SentMessages
            .Where(m => m.CandidateId == candidateId)
            .OrderByDescending(m => m.SentAt)
            .Select(m => new { m.Id, m.MessageKey, m.Subject, m.SentAt })
            .ToListAsync(ct);
        var now = clock.GetUtcNow();
        var bookings = await scheduling.GetPortalBookingsAsync(candidateId, ct);
        var asks = await answers.GetViewsAsync(candidate, ct);

        // Only what's open now and what's behind them: later stages stay out of sight until they open.
        var steps = candidate.Steps
            .Where(s => s.Status is not (StepStatus.Skipped or StepStatus.Upcoming))
            .OrderBy(s => s.Step!.Order)
            .Select(s =>
            {
                var d = s.Step!;
                var state = s.Status switch
                {
                    StepStatus.ActionNeeded => PortalStepState.ToDo,
                    StepStatus.Waiting or StepStatus.Blocked => PortalStepState.InProgress,
                    StepStatus.Complete => PortalStepState.Done,
                    _ => PortalStepState.Upcoming,
                };
                var keys = new[] { d.RequestMessageKey, d.AcknowledgmentMessageKey };
                return new PortalStep(
                    d.Key,
                    d.Title,
                    d.Phase,
                    state,
                    state == PortalStepState.Done ? null : s.DueDate,
                    state == PortalStepState.Done ? null : s.DueTime,
                    state == PortalStepState.ToDo && CandidateJourney.IsOverdue(s, now),
                    d.WatchForSender is { } sender && state is PortalStepState.ToDo or PortalStepState.Upcoming
                        ? new PortalWatchFor(sender, d.WatchForFromAddress, d.WatchForSubject)
                        : null,
                    ContactFor(candidate, d.Owner),
                    sent.Where(m => keys.Contains(m.MessageKey)).Select(m => new PortalMessageLink(m.Id, m.Subject, m.SentAt)).ToList(),
                    state == PortalStepState.Done ? s.CompletedAt : null,
                    bookings.GetValueOrDefault(d.Key),
                    d.Key == PortalAnswers.I9Step ? asks.I9Choice : null,
                    d.Key == PortalAnswers.RemoteI9Step ? asks.Representative : null,
                    d.Key == PortalAnswers.FingerprintingStep ? asks.AtiNumber : null,
                    d.TaskLabel.Length > 0 ? d.TaskLabel : d.Title,
                    KindOf(d.Key, bookings.ContainsKey(d.Key)),
                    d.TaskSummary,
                    d.Stage);
            })
            .ToList();

        // Who to ask now: the owner of the first step still to do, else of the latest one.
        var lead = candidate.Steps.Where(s => s.Status is StepStatus.ActionNeeded or StepStatus.Waiting or StepStatus.Blocked)
            .OrderBy(s => s.Step!.Order).FirstOrDefault()?.Step!.Owner ?? StaffRole.ServiceChannel;
        var applicable = candidate.Steps.Count(s => s.Status != StepStatus.Skipped);
        return new PortalView(
            candidate.FirstName, candidate.PositionTitle, candidate.Department,
            candidate.AnticipatedStartDate, deadlines.ClearanceDeadline(candidate.AnticipatedStartDate), steps,
            null, candidate.Steps.Count(s => s.Status == StepStatus.Complete), applicable, ContactFor(candidate, lead));
    }

    /// <summary>The task's email text for this candidate, for the task page. Null when it has no email of its own, or one can't be filled.</summary>
    public async Task<PortalTaskBody?> GetTaskBodyAsync(int candidateId, string key, CancellationToken ct = default)
    {
        var candidate = await JourneyData.CandidatesWithJourney(db).SingleOrDefaultAsync(c => c.Id == candidateId && c.ArchivedAt == null, ct);
        var step = candidate?.Steps.SingleOrDefault(s => s.Step!.Key == key && s.Status is not (StepStatus.Skipped or StepStatus.Upcoming));
        if (step is null) return null;
        return new PortalTaskBody(key, await messages.RenderForStepAsync(candidateId, step.Step!, ct));
    }

    private static PortalTaskKind KindOf(string key, bool bookable) => key switch
    {
        PortalAnswers.I9Step => PortalTaskKind.I9,
        PortalAnswers.RemoteI9Step => PortalTaskKind.Representative,
        _ => bookable ? PortalTaskKind.Schedule : PortalTaskKind.External,
    };

    /// <summary>A message this candidate was sent, as logged: its button points at the portal, never at a sign-in link.</summary>
    public async Task<PortalMessage?> GetMessageAsync(int candidateId, int sentId, CancellationToken ct = default) =>
        await db.SentMessages
            .Where(m => m.CandidateId == candidateId && m.Id == sentId)
            .Select(m => new PortalMessage(m.Id, m.Subject, m.SentAt, m.Html, m.Text))
            .SingleOrDefaultAsync(ct);

    /// <summary>
    /// "Send me a new link." Sends one only to an active candidate with that address, at most
    /// <see cref="PortalOptions.LinksPerHour"/> an hour; the caller learns nothing about which happened.
    /// </summary>
    public async Task RequestLinkAsync(string? email, CancellationToken ct = default)
    {
        var address = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(address) || address.Length > 320) return;

        // An address shared by two records (a re-hire) gets a link for the most recent one.
        var candidateId = await db.Candidates
            .Where(c => c.ArchivedAt == null && c.Email.ToLower() == address)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (candidateId is not { } id || !await tokens.MayIssueAsync(id, ct)) return;

        await messages.SendOnCandidateRequestAsync(id, SystemMessages.PortalLink, ct);
    }

    /// <summary>Who to ask about a step: the person in the candidate's team who owns it.</summary>
    private static PortalContact? ContactFor(Candidate c, StaffRole owner)
    {
        var person = owner switch
        {
            StaffRole.Recruiter => c.Recruiter,
            StaffRole.UnitHr => c.UnitHr ?? c.ServiceChannel,
            _ => c.ServiceChannel,
        };
        return person is { IsActive: true } ? new PortalContact(person.Name, person.ContactDetails) : null;
    }
}
