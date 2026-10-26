using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Notification;
using Server.Core.Portal;
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

/// <summary>Whether an automatic email went, and a sentence saying so for whoever made the change.</summary>
public sealed record SendOutcome(bool Sent, string Notice);

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
    PortalTokens tokens,
    IOptions<PortalOptions> portal,
    ILogger<MessageService> logger)
{
    /// <summary>Stands in for a staff member when the candidate asked for the message themselves.</summary>
    private static StaffMember OnCandidateRequest() => new() { Name = "UC Davis Health", Email = "" };

    public async Task<MessagePreview?> PreviewAsync(int candidateId, string key, StaffMember sender, CancellationToken ct = default)
    {
        var draft = await DraftAsync(candidateId, key, sender, clock.GetUtcNow(), ct);
        if (draft is null) return null;

        var timesSent = await db.SentMessages.CountAsync(m => m.CandidateId == candidateId && m.MessageKey == key, ct);
        return new MessagePreview(
            draft.Template.Key, draft.Template.AuditReference, draft.Template.Sender, draft.Template.Version,
            draft.Candidate.Email, ReplyTo(sender), draft.Rendered.Subject, draft.Html, draft.Text,
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
    /// Sends because the candidate asked (a new sign-in link). Signed by the onboarding specialist or
    /// recruiting assistant when one is assigned. Quietly does nothing when it cannot be sent: the
    /// person asking learns nothing either way (AGENTS.md, invariant 3).
    /// </summary>
    public async Task<bool> SendOnCandidateRequestAsync(int candidateId, string key, CancellationToken ct = default)
    {
        var sender = await AssignedSenderAsync(candidateId, ct);
        var draft = await DraftAsync(candidateId, key, sender, clock.GetUtcNow(), ct);
        if (draft is null || !draft.CanSend) return false;
        try
        {
            await DeliverAndRecordAsync(draft, sender, automatic: true, ct, actorName: "Candidate (asked for a new link)");
            return true;
        }
        catch (MessageDeliveryException)
        {
            return false;
        }
    }

    /// <summary>
    /// Sends as a consequence of a step move (message 5). Never throws for a message that cannot go:
    /// the move stands, the history says why the message did not, and the returned notice tells the
    /// staff member who made the move.
    /// </summary>
    public async Task<string?> SendAutomaticAsync(
        int candidateId, string key, StaffMember sender, CancellationToken ct = default, string? actorName = null)
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
                await DeliverAndRecordAsync(draft, sender, automatic: true, ct, actorName);
                return $"Message {draft.Template.AuditReference} (“{draft.Rendered.Subject}”) was sent to {draft.Candidate.Email}.";
            }
            catch (MessageDeliveryException ex)
            {
                notice = $"Message {draft.Template.AuditReference} was not sent automatically: {ex.Message} Send it from the step to try again.";
            }
        }

        Audit(draft.Candidate.Id, sender, "message.not-sent", notice, actorName);
        await db.SaveChangesAsync(ct);
        return notice;
    }

    /// <summary>
    /// Sends because of the candidate's answer in the portal (choosing remote I-9 sends 11B). Signed by
    /// their assigned staff, as <see cref="SendAutomaticAsync"/>: it never throws, and the history says
    /// whether it went.
    /// </summary>
    public async Task<string?> SendOnCandidateAnswerAsync(int candidateId, string key, CancellationToken ct = default) =>
        await SendAutomaticAsync(candidateId, key, await AssignedSenderAsync(candidateId, ct), ct, actorName: "Candidate");

    /// <summary>
    /// Emails the candidate about an appointment: booked, changed (<paramref name="previousStart"/> is the
    /// old time), cancelled, or tomorrow. Signed and replied to by the candidate's assigned staff, whoever
    /// made the change. Booking, change and cancellation carry a calendar invitation. Like
    /// <see cref="SendAutomaticAsync"/> it never throws: the change stands, and the returned notice and
    /// the history say whether the email went.
    /// </summary>
    public async Task<SendOutcome?> SendAppointmentAsync(
        int appointmentId, string key, DateTime? previousStart, string actorName, CancellationToken ct = default)
    {
        var appointment = await db.Appointments
            .Include(a => a.AppointmentType).Include(a => a.Location).Include(a => a.Candidate)
            .SingleOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment is null || !SystemMessages.Appointment.Contains(key)) return null;

        var type = appointment.AppointmentType!;
        var location = appointment.Location!;
        var facts = new AppointmentFacts(
            type.Name, appointment.Start, appointment.End, location.Name, location.Address,
            appointment.Start.AddHours(-type.ChangeCutoffHours), previousStart);
        var sender = await AssignedSenderAsync(appointment.CandidateId, ct);
        var draft = await DraftAsync(appointment.CandidateId, key, sender, clock.GetUtcNow(), ct, facts);
        if (draft is null) return null;

        string notice;
        if (!draft.CanSend)
        {
            notice = $"The “{draft.Rendered.Subject}” email was not sent. {WhyNot(draft)}";
        }
        else
        {
            try
            {
                var invite = key == SystemMessages.AppointmentReminder ? null : Invite(appointment, draft, cancel: key == SystemMessages.AppointmentCancelled);
                await DeliverAndRecordAsync(draft, sender, automatic: true, ct, actorName, invite is null ? [] : [invite]);
                return new SendOutcome(true, $"“{draft.Rendered.Subject}” was emailed to {draft.Candidate.Email}.");
            }
            catch (MessageDeliveryException ex)
            {
                notice = $"The “{draft.Rendered.Subject}” email was not sent: {ex.Message}";
            }
        }

        Audit(draft.Candidate.Id, sender, "message.not-sent", notice, actorName);
        await db.SaveChangesAsync(ct);
        return new SendOutcome(false, notice);
    }

    /// <summary>
    /// One event per candidate and appointment type, so a change moves the event already in their
    /// calendar and a cancellation removes it. Appointment IDs only grow, so each new booking, and
    /// each cancellation after it, carries a higher sequence than anything sent before.
    /// </summary>
    private EmailAttachment Invite(Appointment appointment, Draft draft, bool cancel)
    {
        var type = appointment.AppointmentType!;
        var location = appointment.Location!;
        var portalUrl = portal.Value.PortalUrl;
        var description = cancel
            ? "This appointment is cancelled." + (portalUrl is null ? "" : $" Book a new time from your onboarding checklist: {portalUrl}")
            : portalUrl is null ? type.Name : $"Change or cancel from your onboarding checklist: {portalUrl}";
        var ics = CalendarInvite.Create(new CalendarEvent(
            Uid: $"{appointment.CandidateId}-{type.Key}@aggieonboard",
            Sequence: appointment.Id * 2 + (cancel ? 1 : 0),
            Start: appointment.Start,
            End: appointment.End,
            Summary: $"UC Davis Health: {type.Name}",
            Location: location.Address is { } address ? $"{location.Name}, {address}" : location.Name,
            Description: description,
            OrganizerName: string.IsNullOrWhiteSpace(smtp.Value.FromName) ? "UC Davis Health" : smtp.Value.FromName,
            OrganizerEmail: smtp.Value.FromEmail,
            AttendeeName: $"{draft.Candidate.FirstName} {draft.Candidate.LastName}",
            AttendeeEmail: draft.Candidate.Email), draft.Now, cancel);
        return new EmailAttachment(CalendarInvite.FileName, CalendarInvite.ContentType(cancel), System.Text.Encoding.UTF8.GetBytes(ics));
    }

    /// <summary>Who signs a message nobody on staff pressed Send for: the candidate's specialist or assistant, when assigned.</summary>
    private async Task<StaffMember> AssignedSenderAsync(int candidateId, CancellationToken ct)
    {
        var assigned = await db.Candidates
            .Where(c => c.Id == candidateId)
            .Select(c => c.OnboardingSpecialist ?? c.RecruitingAssistant ?? c.Recruiter)
            .SingleOrDefaultAsync(ct);
        return assigned is { IsActive: true } ? assigned : OnCandidateRequest();
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
        string Text,
        IReadOnlyList<CandidateStep> Starts,
        IReadOnlyList<string> Blockers,
        DateTimeOffset Now)
    {
        public bool CanSend => Blockers.Count == 0 && Rendered.Missing.Count == 0;
    }

    private async Task<Draft?> DraftAsync(
        int candidateId, string key, StaffMember sender, DateTimeOffset now, CancellationToken ct, AppointmentFacts? appointment = null)
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

        var fields = MergeFieldResolver.Resolve(
            new MergeContext(candidate, sender, settings, deadlines, dueByStep, now, portal.Value.LinkLifetimeDays, appointment));
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
        if (SystemMessages.Appointment.Contains(key) && appointment is null)
        {
            blockers.Add("Appointment emails are sent by the schedule when an appointment is booked, changed or cancelled, and the day before it.");
        }
        if (key == SystemMessages.PortalLink && portal.Value.PortalUrl is null)
        {
            blockers.Add("Sign-in links need the app's address (Portal:BaseUrl or Notification:BaseUrl).");
        }
        if (string.IsNullOrWhiteSpace(smtp.Value.Host))
        {
            blockers.Add("Email isn't set up on this server (Smtp:Host is empty).");
        }

        // The preview, and the copy kept in the log, point the button at the portal itself. Only the
        // delivered email carries a one-time sign-in link, so the log never holds a credential.
        var portalUrl = portal.Value.PortalUrl;
        var html = await RenderEmailAsync(rendered, portalUrl, ct);
        return new Draft(candidate, template, rendered, html, WithButton(rendered.Text, portalUrl), starts, blockers, now);
    }

    private async Task DeliverAndRecordAsync(
        Draft draft, StaffMember sender, bool automatic, CancellationToken ct, string? actorName = null,
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var pending = portal.Value.PortalUrl is null ? null : PortalTokens.Create();
        var signInUrl = pending is null ? null : portal.Value.SignInUrl(pending.Token);
        var html = pending is null ? draft.Html : await RenderEmailAsync(draft.Rendered, signInUrl, ct);

        // Deliver first: if the SMTP server refuses, nothing is recorded as sent, no step starts and
        // the sign-in link is never recorded, so it cannot work.
        try
        {
            await email.SendAsync(new EmailMessage
            {
                Recipients = new EmailRecipients { To = [draft.Candidate.Email] },
                ReplyTo = ReplyTo(sender),
                Subject = draft.Rendered.Subject,
                HtmlBody = html,
                TextBody = WithButton(draft.Rendered.Text, signInUrl),
                Attachments = attachments ?? [],
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sending message {Key} to candidate {CandidateId} failed.", draft.Template.Key, draft.Candidate.Id);
            throw new MessageDeliveryException("The email server did not accept the message.", ex);
        }

        if (pending is not null) tokens.Record(draft.Candidate.Id, pending);

        db.SentMessages.Add(new SentMessage
        {
            CandidateId = draft.Candidate.Id,
            MessageTemplateId = draft.Template.Id,
            MessageKey = draft.Template.Key,
            AuditReference = draft.Template.AuditReference,
            ToAddress = draft.Candidate.Email,
            ReplyTo = ReplyTo(sender),
            Subject = draft.Rendered.Subject,
            Html = draft.Html,
            Text = draft.Text,
            SentAt = draft.Now,
            SentByStaffId = StaffId(sender),
            SentByName = actorName ?? sender.Name,
            Automatic = automatic,
        });
        Audit(draft.Candidate.Id, sender, "message.sent",
            $"{(automatic ? "Automatically sent" : "Sent")} {Describe(draft.Template)} (“{draft.Rendered.Subject}”) to {draft.Candidate.Email}.", actorName);

        var deadlines = await JourneyData.LoadDeadlinesAsync(db, ct);
        foreach (var step in draft.Starts)
        {
            CandidateJourney.Activate(draft.Candidate, step, draft.Now, deadlines);
            Audit(draft.Candidate.Id, sender, "step.activated",
                $"{step.Step!.Title}: upcoming → action needed ({Describe(draft.Template)} sent)", actorName);
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

    private Task<string> RenderEmailAsync(RenderedMessage rendered, string? buttonUrl, CancellationToken ct) =>
        renderer.RenderAsync(CandidateMessageModel.TemplatePath, new CandidateMessageModel
        {
            BodyHtml = rendered.BodyHtml,
            PreviewText = rendered.Subject,
            ButtonUrl = buttonUrl,
            ButtonText = buttonUrl is null ? null : CandidateMessageModel.PortalButtonText,
        }, ct);

    private static string WithButton(string text, string? url) =>
        url is null ? text : $"{text}\n{CandidateMessageModel.PortalButtonText}: {url}\n";

    /// <summary>"message 4" for the audit's messages; the key for the app's own ("the portal-link message").</summary>
    private static string Describe(MessageTemplate template) =>
        SystemMessages.Keys.Contains(template.Key) ? $"the {template.Key} message" : $"message {template.AuditReference}";

    private static string? ReplyTo(StaffMember sender) => string.IsNullOrEmpty(sender.Email) ? null : sender.Email;
    private static int? StaffId(StaffMember sender) => sender.Id == 0 ? null : sender.Id;

    private void Audit(int candidateId, StaffMember actor, string action, string detail, string? actorName = null) =>
        db.AuditEvents.Add(new AuditEvent
        {
            CandidateId = candidateId,
            At = clock.GetUtcNow(),
            ActorStaffId = StaffId(actor),
            ActorName = actorName ?? actor.Name,
            Action = action,
            Detail = detail,
        });
}
