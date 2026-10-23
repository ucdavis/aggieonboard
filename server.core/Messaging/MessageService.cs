using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Notification;
using Server.Core.Scheduling;

namespace Server.Core.Messaging;

public sealed record MessagePreview(
    string Key,
    string AuditReference,
    MessageSender Sender,
    int TemplateVersion,
    string To,
    string? ReplyTo,
    string Subject,
    string Html,
    string Text,
    IReadOnlyList<MissingField> Missing,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> StartsSteps,
    int TimesSent,
    bool CanSend);

public sealed record SentMessageSummary(
    int Id, string Key, string AuditReference, string Subject, string To, DateTimeOffset SentAt, string SentBy, bool Automatic);

public sealed record SentMessageView(
    int Id, string Key, string AuditReference, int TemplateVersion, string Subject, string To, string? ReplyTo,
    DateTimeOffset SentAt, string SentBy, bool Automatic, string Html, string Text);

/// <summary>The SMTP server refused or could not be reached. Nothing was recorded as sent.</summary>
public sealed class MessageDeliveryException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Previews and sends the journey's messages. Sending is refused while any field is missing
/// (AGENTS.md, invariant 6); a sent message is logged exactly as it went out (invariant 7); and
/// sending a step's request message is what starts that step.
/// </summary>
public sealed class MessageService(
    AppDbContext db,
    TimeProvider clock,
    INotificationRenderer renderer,
    IEmailService email,
    IOptions<SmtpOptions> smtp,
    ILogger<MessageService> logger)
{
    public async Task<MessagePreview?> PreviewAsync(int candidateId, string key, StaffMember sender, CancellationToken ct = default)
    {
        var draft = await DraftAsync(candidateId, key, sender, clock.GetUtcNow(), ct);
        if (draft is null) return null;

        var timesSent = await db.SentMessages.CountAsync(m => m.CandidateId == candidateId && m.MessageKey == key, ct);
        return new MessagePreview(
            draft.Template.Key, draft.Template.AuditReference, draft.Template.Sender, draft.Template.Version,
            draft.Candidate.Email, sender.Email, draft.Rendered.Subject, draft.Html, draft.Rendered.Text,
            draft.Rendered.Missing, draft.Blockers, draft.Starts.Select(s => s.Step!.Title).ToList(),
            timesSent, draft.CanSend);
    }

    /// <summary>Sends now, as the staff member who pressed Send. Throws <see cref="JourneyRuleException"/> when it cannot be sent yet.</summary>
    public async Task<bool> SendAsync(int candidateId, string key, StaffMember sender, CancellationToken ct = default)
    {
        var draft = await DraftAsync(candidateId, key, sender, clock.GetUtcNow(), ct);
        if (draft is null) return false;
        if (!draft.CanSend) throw new JourneyRuleException(WhyNot(draft));

        await DeliverAndRecordAsync(draft, sender, automatic: false, ct);
        return true;
    }

    /// <summary>
    /// Sends as a consequence of a step move (message 5). Never throws for a message that cannot go:
    /// the move stands, the history says why the message did not, and the returned notice tells the
    /// staff member who made the move.
    /// </summary>
    public async Task<string?> SendAutomaticAsync(int candidateId, string key, StaffMember sender, CancellationToken ct = default)
    {
        var draft = await DraftAsync(candidateId, key, sender, clock.GetUtcNow(), ct);
        if (draft is null) return null;

        string notice;
        if (!draft.CanSend)
        {
            notice = $"Message {draft.Template.AuditReference} was not sent automatically. {WhyNot(draft)} Send it from the step once that is fixed.";
        }
        else
        {
            try
            {
                await DeliverAndRecordAsync(draft, sender, automatic: true, ct);
                return $"Message {draft.Template.AuditReference} (“{draft.Rendered.Subject}”) was sent to {draft.Candidate.Email}.";
            }
            catch (MessageDeliveryException ex)
            {
                notice = $"Message {draft.Template.AuditReference} was not sent automatically: {ex.Message} Send it from the step to try again.";
            }
        }

        Audit(draft.Candidate.Id, sender, "message.not-sent", notice);
        await db.SaveChangesAsync(ct);
        return notice;
    }

    public async Task<IReadOnlyList<SentMessageSummary>> GetSentAsync(int candidateId, CancellationToken ct = default) =>
        await db.SentMessages
            .Where(m => m.CandidateId == candidateId)
            .OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id)
            .Select(m => new SentMessageSummary(m.Id, m.MessageKey, m.AuditReference, m.Subject, m.ToAddress, m.SentAt, m.SentByName, m.Automatic))
            .ToListAsync(ct);

    public async Task<SentMessageView?> GetSentMessageAsync(int candidateId, int sentId, CancellationToken ct = default) =>
        await db.SentMessages
            .Where(m => m.CandidateId == candidateId && m.Id == sentId)
            .Select(m => new SentMessageView(
                m.Id, m.MessageKey, m.AuditReference, m.Template!.Version, m.Subject, m.ToAddress, m.ReplyTo,
                m.SentAt, m.SentByName, m.Automatic, m.Html, m.Text))
            .SingleOrDefaultAsync(ct);

    private sealed record Draft(
        Candidate Candidate,
        MessageTemplate Template,
        RenderedMessage Rendered,
        string Html,
        IReadOnlyList<CandidateStep> Starts,
        IReadOnlyList<string> Blockers,
        DateTimeOffset Now)
    {
        public bool CanSend => Blockers.Count == 0 && Rendered.Missing.Count == 0;
    }

    private async Task<Draft?> DraftAsync(int candidateId, string key, StaffMember sender, DateTimeOffset now, CancellationToken ct)
    {
        var candidate = await JourneyData.CandidatesWithJourney(db).SingleOrDefaultAsync(c => c.Id == candidateId, ct);
        var template = await db.MessageTemplates
            .Include(t => t.Sections)
            .Where(t => t.Key == key)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(ct);
        if (candidate is null || template is null) return null;

        var deadlines = await JourneyData.LoadDeadlinesAsync(db, ct);
        var settings = await db.Settings.ToDictionaryAsync(s => s.Key, ct);

        var steps = candidate.Steps
            .Where(s => s.Step!.RequestMessageKey == key || s.Step.AcknowledgmentMessageKey == key || s.Step.VendorMessageKey == key)
            .ToList();
        var applicable = steps.Where(s => candidate.Meets(s.Step!.Condition)).ToList();
        var starts = applicable
            .Where(s => s.Step!.RequestMessageKey == key && s.Status == StepStatus.Upcoming)
            .OrderBy(s => s.Step!.Order)
            .ToList();

        // A step this message starts is due as if it started now, which is when it will.
        var dueByStep = candidate.Steps.ToDictionary(
            s => s.Step!.Key,
            s => s.DueDate is { } date ? new Due(date, s.DueTime)
                : starts.Contains(s) ? deadlines.Compute(s.Step!.Deadline, now, candidate.AnticipatedStartDate)
                : (Due?)null);

        var fields = MergeFieldResolver.Resolve(new MergeContext(candidate, sender, settings, deadlines, dueByStep, now));
        var rendered = MessageRenderer.Render(template, candidate, fields);

        var blockers = new List<string>();
        if (template.Sender == MessageSender.External)
        {
            var vendor = steps.Select(s => s.Step!.WatchForSender).FirstOrDefault(v => v != null) ?? "A vendor";
            blockers.Add($"{vendor} sends this message, not AggieOnboard. This is the wording proposed to them.");
        }
        else if (steps.Count > 0 && applicable.Count == 0)
        {
            blockers.Add($"This message is for {string.Join(", ", steps.Select(s => s.Step!.Title))}, which {(steps.Count == 1 ? "doesn't" : "don't")} apply to {candidate.FirstName}.");
        }
        if (string.IsNullOrWhiteSpace(smtp.Value.Host))
        {
            blockers.Add("Email isn't set up on this server (Smtp:Host is empty).");
        }

        var html = await renderer.RenderAsync(CandidateMessageModel.TemplatePath, new CandidateMessageModel
        {
            BodyHtml = rendered.BodyHtml,
            PreviewText = rendered.Subject,
        }, ct);

        return new Draft(candidate, template, rendered, html, starts, blockers, now);
    }

    private async Task DeliverAndRecordAsync(Draft draft, StaffMember sender, bool automatic, CancellationToken ct)
    {
        // Deliver first: if the SMTP server refuses, nothing is recorded as sent and no step starts.
        try
        {
            await email.SendAsync(new EmailMessage
            {
                Recipients = new EmailRecipients { To = [draft.Candidate.Email] },
                ReplyTo = sender.Email,
                Subject = draft.Rendered.Subject,
                HtmlBody = draft.Html,
                TextBody = draft.Rendered.Text,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sending message {Key} to candidate {CandidateId} failed.", draft.Template.Key, draft.Candidate.Id);
            throw new MessageDeliveryException("The email server did not accept the message.", ex);
        }

        db.SentMessages.Add(new SentMessage
        {
            CandidateId = draft.Candidate.Id,
            MessageTemplateId = draft.Template.Id,
            MessageKey = draft.Template.Key,
            AuditReference = draft.Template.AuditReference,
            ToAddress = draft.Candidate.Email,
            ReplyTo = sender.Email,
            Subject = draft.Rendered.Subject,
            Html = draft.Html,
            Text = draft.Rendered.Text,
            SentAt = draft.Now,
            SentByStaffId = sender.Id,
            SentByName = sender.Name,
            Automatic = automatic,
        });
        Audit(draft.Candidate.Id, sender, "message.sent",
            $"{(automatic ? "Automatically sent" : "Sent")} message {draft.Template.AuditReference} (“{draft.Rendered.Subject}”) to {draft.Candidate.Email}.");

        var deadlines = await JourneyData.LoadDeadlinesAsync(db, ct);
        foreach (var step in draft.Starts)
        {
            CandidateJourney.Activate(draft.Candidate, step, draft.Now, deadlines);
            Audit(draft.Candidate.Id, sender, "step.activated",
                $"{step.Step!.Title}: upcoming → action needed (message {draft.Template.AuditReference} sent)");
        }

        await db.SaveChangesAsync(ct);
    }

    private static string WhyNot(Draft draft)
    {
        var reasons = draft.Blockers.ToList();
        if (draft.Rendered.Missing.Count > 0)
        {
            reasons.Add("Missing: " + string.Join("; ", draft.Rendered.Missing.Select(m => $"{m.Description.TrimEnd('.')} ({m.Fix})")));
        }
        return string.Join(" ", reasons);
    }

    private void Audit(int candidateId, StaffMember actor, string action, string detail) =>
        db.AuditEvents.Add(new AuditEvent
        {
            CandidateId = candidateId,
            At = clock.GetUtcNow(),
            ActorStaffId = actor.Id,
            ActorName = actor.Name,
            Action = action,
            Detail = detail,
        });
}
