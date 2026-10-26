using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Messaging;
using Server.Core.Scheduling;

namespace Server.Core.Journey;

/// <summary>
/// What staff do to candidates: list, read, create, edit, and move steps. Every change writes an
/// <see cref="AuditEvent"/> in the same save, so history cannot fall out of step with the data.
/// </summary>
public sealed partial class JourneyService(AppDbContext db, TimeProvider clock, MessageService messages)
{
    public async Task<IReadOnlyList<StaffSummary>> GetStaffAsync(CancellationToken ct = default) =>
        await db.Staff
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new StaffSummary(s.Id, s.Name, s.Email, s.Role))
            .ToListAsync(ct);

    /// <summary>Every candidate not archived. <paramref name="assignedTo"/> narrows to one staff member's candidates.</summary>
    public async Task<IReadOnlyList<PipelineRow>> GetPipelineAsync(int? assignedTo = null, CancellationToken ct = default)
    {
        var query = WithJourney().Where(c => c.ArchivedAt == null);
        if (assignedTo is { } staffId)
        {
            query = query.Where(c =>
                c.RecruiterId == staffId || c.ServiceChannelId == staffId || c.UnitHrId == staffId);
        }

        var now = clock.GetUtcNow();
        var candidates = await query.ToListAsync(ct);
        return candidates
            .Select(c => ToRow(c, now))
            .OrderByDescending(r => r.IsOverdue)
            .ThenBy(r => r.NextDueDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.Name)
            .ToList();
    }

    public async Task<CandidateDetail?> GetCandidateAsync(int id, CancellationToken ct = default)
    {
        var candidate = await WithJourney().SingleOrDefaultAsync(c => c.Id == id, ct);
        return candidate is null ? null : await ToDetailAsync(candidate, ct);
    }

    public async Task<CandidateDetail> CreateCandidateAsync(CandidateInput input, StaffMember actor, CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);
        var now = clock.GetUtcNow();
        var candidate = new Candidate { CreatedAt = now };
        Assign(candidate, input);

        var definitions = await db.StepDefinitions.OrderBy(s => s.Order).ToListAsync(ct);
        CandidateJourney.AddSteps(candidate, definitions);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync(ct);

        await AuditAsync(candidate.Id, actor, "candidate.created", $"Created {candidate.FirstName} {candidate.LastName} for {candidate.PositionTitle}.", ct);

        // The first stage (references) opens at once; its email waits for the new-step delay.
        JourneyFlow.Advance(db, candidate, now, await DeadlinesAsync(ct), actor, actor.Name);
        await db.SaveChangesAsync(ct);
        return (await GetCandidateAsync(candidate.Id, ct))!;
    }

    /// <summary>
    /// Replaces the candidate's details, requirements and qualifications. Steps that stop or start
    /// applying are skipped or restored, and open due dates follow a changed start date.
    /// </summary>
    public async Task<CandidateDetail?> UpdateCandidateAsync(int id, CandidateInput input, StaffMember actor, CancellationToken ct = default)
    {
        var candidate = await WithJourney().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (candidate is null) return null;
        await ValidateAsync(input, ct);

        var names = await db.Staff.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        string? NameOf(int? staffId) => staffId is { } sid && names.TryGetValue(sid, out var name) ? name : null;

        var before = Snapshot(candidate, NameOf);
        Assign(candidate, input);
        var after = Snapshot(candidate, NameOf);

        var changedSteps = CandidateJourney.ApplyConditions(candidate);
        var deadlines = await DeadlinesAsync(ct);
        CandidateJourney.RecomputeDues(candidate, deadlines);
        // A requirement added to the stage under way opens with it; one dropped may finish the stage.
        JourneyFlow.Advance(db, candidate, clock.GetUtcNow(), deadlines, actor, actor.Name);

        var changes = before.Keys
            .Where(k => before[k] != after[k])
            .Select(k => $"{k}: {before[k] ?? "—"} → {after[k] ?? "—"}")
            .Concat(changedSteps.Select(s => $"{s.Step!.Title}: now {CandidateJourney.Describe(s.Status)}"))
            .ToList();

        await db.SaveChangesAsync(ct);
        if (changes.Count > 0)
        {
            await AuditAsync(candidate.Id, actor, "candidate.updated", string.Join("; ", changes) + ".", ct);
        }
        // Read back rather than reuse: changed assignments leave the loaded staff navigations stale.
        return await GetCandidateAsync(id, ct);
    }

    public async Task<CandidateDetail?> ApplyStepActionAsync(
        int id, string stepKey, StepAction action, string? reason, StaffMember actor, CancellationToken ct = default)
    {
        var (candidate, step) = await FindStepAsync(id, stepKey, ct);
        if (candidate is null || step is null) return null;

        var from = step.Status;
        var deadlines = await DeadlinesAsync(ct);
        CandidateJourney.Apply(candidate, step, action, clock.GetUtcNow(), actor, deadlines);
        await db.SaveChangesAsync(ct);

        var detail = $"{step.Step!.Title}: {CandidateJourney.Describe(from)} → {CandidateJourney.Describe(step.Status)}";
        if (!string.IsNullOrWhiteSpace(reason)) detail += $". {reason.Trim()}";
        await AuditAsync(candidate.Id, actor, $"step.{ActionName(action)}", detail, ct);

        // Completing the last step of a stage opens the next one.
        if (JourneyFlow.Advance(db, candidate, clock.GetUtcNow(), deadlines, actor, actor.Name).Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        // What the candidate sent has arrived: acknowledge it (message 5), or say why that couldn't happen.
        string? notice = null;
        if (action == StepAction.MarkWaiting && step.Step.AcknowledgmentMessageKey is { } acknowledgment)
        {
            notice = await messages.SendAutomaticAsync(candidate.Id, acknowledgment, actor, ct);
        }
        return await ToDetailAsync(candidate, ct) with { Notice = notice };
    }

    /// <summary>Sets the due date by hand, or, with a null date, returns the step to its rule.</summary>
    public async Task<CandidateDetail?> SetStepDueAsync(
        int id, string stepKey, DateOnly? date, TimeOnly? time, StaffMember actor, CancellationToken ct = default)
    {
        var (candidate, step) = await FindStepAsync(id, stepKey, ct);
        if (candidate is null || step is null) return null;
        if (step.Status is StepStatus.Complete or StepStatus.Skipped)
        {
            throw new JourneyRuleException($"{step.Step!.Title} is {CandidateJourney.Describe(step.Status)}; it has no due date to change.");
        }

        string detail;
        if (date is { } due)
        {
            CandidateJourney.OverrideDue(step, due, time);
            detail = $"{step.Step!.Title}: due {FormatDue(due, time)} (set by hand)";
        }
        else
        {
            CandidateJourney.ClearDueOverride(candidate, step, await DeadlinesAsync(ct));
            detail = $"{step.Step!.Title}: due date back to its rule"
                + (step.DueDate is { } d ? $" ({FormatDue(d, step.DueTime)})" : "");
        }

        await db.SaveChangesAsync(ct);
        await AuditAsync(candidate.Id, actor, "step.due-set", detail, ct);
        return await ToDetailAsync(candidate, ct);
    }

    public async Task<CandidateDetail?> SetStepNoteAsync(int id, string stepKey, string? note, StaffMember actor, CancellationToken ct = default)
    {
        var (candidate, step) = await FindStepAsync(id, stepKey, ct);
        if (candidate is null || step is null) return null;

        step.StaffNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await db.SaveChangesAsync(ct);
        await AuditAsync(candidate.Id, actor, "step.note", $"{step.Step!.Title}: note {(step.StaffNote is null ? "cleared" : "updated")}", ct);
        return await ToDetailAsync(candidate, ct);
    }

    private Task<DeadlineCalculator> DeadlinesAsync(CancellationToken ct) => JourneyData.LoadDeadlinesAsync(db, ct);

    private IQueryable<Candidate> WithJourney() => JourneyData.CandidatesWithJourney(db);

    private async Task<(Candidate?, CandidateStep?)> FindStepAsync(int id, string stepKey, CancellationToken ct)
    {
        var candidate = await WithJourney().SingleOrDefaultAsync(c => c.Id == id, ct);
        return (candidate, candidate?.Steps.SingleOrDefault(s => s.Step!.Key == stepKey));
    }

    private static void Assign(Candidate candidate, CandidateInput input)
    {
        candidate.FirstName = input.FirstName.Trim();
        candidate.LastName = input.LastName.Trim();
        candidate.Email = input.Email.Trim();
        candidate.PositionTitle = input.PositionTitle.Trim();
        candidate.Department = input.Department.Trim();
        candidate.HiringManagerContact = Blank(input.HiringManagerContact);
        candidate.AnticipatedStartDate = input.AnticipatedStartDate;
        candidate.EmployeeId = Blank(input.EmployeeId);
        candidate.RecruiterId = input.RecruiterId;
        candidate.ServiceChannelId = input.ServiceChannelId;
        candidate.UnitHrId = input.UnitHrId;
        candidate.RequiresDrugScreen = input.Requirements.DrugScreen;
        candidate.RequiresDmvPullNotice = input.Requirements.DmvPullNotice;
        candidate.RequiresFingerprinting = input.Requirements.Fingerprinting;
        candidate.RequiresPositionDescription = input.Requirements.PositionDescription;
        candidate.EligibleForParking = input.Requirements.Parking;
        candidate.I9Method = input.I9Method;
        candidate.I9RepresentativeName = Blank(input.I9RepresentativeName);
        candidate.I9RepresentativeEmail = Blank(input.I9RepresentativeEmail);
        candidate.FingerprintAtiNumber = CandidateAnswers.NormalizeAtiNumber(input.FingerprintAtiNumber);

        candidate.Qualifications.Clear();
        foreach (var q in input.Qualifications)
        {
            candidate.Qualifications.Add(new CandidateQualification
            {
                Kind = q.Kind,
                Name = q.Name.Trim(),
                DocumentToSubmit = Blank(q.DocumentToSubmit)
                    ?? (q.Kind == QualificationKind.Degree ? CandidateQualification.DegreeDocument : ""),
            });
        }
    }

    /// <summary>The fields history reports when they change, as display strings.</summary>
    private static Dictionary<string, string?> Snapshot(Candidate c, Func<int?, string?> staffName) => new()
    {
        ["Name"] = $"{c.FirstName} {c.LastName}",
        ["Email"] = c.Email,
        ["Position"] = c.PositionTitle,
        ["Department"] = c.Department,
        ["Hiring manager contact"] = c.HiringManagerContact,
        ["Start date"] = c.AnticipatedStartDate?.ToString("yyyy-MM-dd"),
        ["Employee ID"] = c.EmployeeId,
        ["Recruiter"] = staffName(c.RecruiterId),
        ["Service Channel"] = staffName(c.ServiceChannelId),
        ["Unit HR"] = staffName(c.UnitHrId),
        ["Drug screen"] = YesNo(c.RequiresDrugScreen),
        ["DMV pull notice"] = YesNo(c.RequiresDmvPullNotice),
        ["Fingerprinting"] = YesNo(c.RequiresFingerprinting),
        ["Position description"] = YesNo(c.RequiresPositionDescription),
        ["Parking"] = YesNo(c.EligibleForParking),
        ["I-9 method"] = c.I9Method.ToString(),
        ["I-9 representative"] = c.I9RepresentativeName is null ? null : $"{c.I9RepresentativeName} ({c.I9RepresentativeEmail})",
        ["ATI number"] = c.FingerprintAtiNumber,
        ["Qualifications"] = string.Join(", ", c.Qualifications.Select(q => q.Name)),
    };

    private async Task ValidateAsync(CandidateInput input, CancellationToken ct)
    {
        var errors = new Dictionary<string, List<string>>();
        void Error(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        void Required(string field, string? value, string label, int max = 200)
        {
            if (string.IsNullOrWhiteSpace(value)) Error(field, $"{label} is required.");
            else if (value.Trim().Length > max) Error(field, $"{label} must be {max} characters or fewer.");
        }

        Required("firstName", input.FirstName, "First name");
        Required("lastName", input.LastName, "Last name");
        Required("email", input.Email, "Email", 320);
        Required("positionTitle", input.PositionTitle, "Position title");
        Required("department", input.Department, "Department");
        if (!string.IsNullOrWhiteSpace(input.Email) && !EmailPattern().IsMatch(input.Email.Trim()))
        {
            Error("email", "Enter an email address like name@example.com.");
        }
        if (input.EmployeeId?.Trim().Length > 20) Error("employeeId", "Employee ID must be 20 characters or fewer.");
        if (input.HiringManagerContact?.Trim().Length > 500) Error("hiringManagerContact", "Hiring manager contact must be 500 characters or fewer.");
        CandidateAnswers.ValidateRepresentative(
            input.I9RepresentativeName, input.I9RepresentativeEmail, Error, "i9RepresentativeName", "i9RepresentativeEmail");
        CandidateAnswers.ValidateAtiNumber(CandidateAnswers.NormalizeAtiNumber(input.FingerprintAtiNumber), Error, "fingerprintAtiNumber");

        for (var i = 0; i < input.Qualifications.Count; i++)
        {
            var q = input.Qualifications[i];
            if (string.IsNullOrWhiteSpace(q.Name)) Error($"qualifications[{i}].name", "Name the license, certification or degree.");
            if (q.Kind != QualificationKind.Degree && string.IsNullOrWhiteSpace(q.DocumentToSubmit))
            {
                Error($"qualifications[{i}].documentToSubmit", "Say what document to send.");
            }
        }

        var assignments = new (string Field, int? Id, StaffRole Role, string Label)[]
        {
            ("recruiterId", input.RecruiterId, StaffRole.Recruiter, "recruiter"),
            ("serviceChannelId", input.ServiceChannelId, StaffRole.ServiceChannel, "Service Channel specialist"),
            ("unitHrId", input.UnitHrId, StaffRole.UnitHr, "Unit HR contact"),
        };
        var ids = assignments.Select(a => a.Id).OfType<int>().ToList();
        var staff = await db.Staff.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        foreach (var (field, id, role, label) in assignments)
        {
            if (id is not { } staffId) continue;
            if (!staff.TryGetValue(staffId, out var member) || !member.IsActive) Error(field, $"Choose an active {label}.");
            else if (member.Role != role) Error(field, $"{member.Name} is not a {label}.");
        }

        if (errors.Count > 0)
        {
            throw new JourneyValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }

    private PipelineRow ToRow(Candidate c, DateTimeOffset now)
    {
        var current = CandidateJourney.CurrentStep(c);
        var owed = c.Steps.Where(s => s.Status is StepStatus.ActionNeeded or StepStatus.Blocked && s.DueDate != null)
            .OrderBy(s => s.DueDate).ThenBy(s => s.DueTime ?? TimeOnly.MaxValue)
            .FirstOrDefault();

        return new PipelineRow(
            c.Id,
            $"{c.FirstName} {c.LastName}",
            c.PositionTitle,
            c.Department,
            current?.Step!.Phase,
            current?.Step!.Key,
            current?.Step!.Title,
            current?.Status,
            current?.Step!.Owner,
            owed?.DueDate,
            owed?.DueTime,
            c.Steps.Any(s => CandidateJourney.IsOverdue(s, now)),
            c.Steps.Count(s => s.Status is StepStatus.ActionNeeded or StepStatus.Waiting or StepStatus.Blocked),
            c.Steps.Count(s => s.Status == StepStatus.Complete),
            c.Steps.Count(s => s.Status != StepStatus.Skipped),
            c.AnticipatedStartDate,
            c.Recruiter?.Name,
            c.ServiceChannel?.Name,
            c.UnitHr?.Name);
    }

    private async Task<CandidateDetail> ToDetailAsync(Candidate c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var deadlines = await DeadlinesAsync(ct);
        var clearanceDays = await db.Settings
            .Where(s => s.Key == SettingKeys.ClearanceBusinessDaysBeforeStart).Select(s => s.Value).SingleOrDefaultAsync(ct);
        var history = await db.AuditEvents
            .Where(e => e.CandidateId == c.Id)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => new HistoryEntry(e.At, e.ActorName, e.Action, e.Detail))
            .ToListAsync(ct);

        return new CandidateDetail(
            c.Id, c.FirstName, c.LastName, c.Email, c.PositionTitle, c.Department, c.HiringManagerContact,
            c.AnticipatedStartDate,
            deadlines.ClearanceDeadline(c.AnticipatedStartDate),
            c.EmployeeId,
            Summary(c.Recruiter), Summary(c.ServiceChannel), Summary(c.UnitHr),
            new CandidateRequirements(
                c.RequiresDrugScreen, c.RequiresDmvPullNotice, c.RequiresFingerprinting, c.RequiresPositionDescription, c.EligibleForParking),
            c.I9Method, c.I9RepresentativeName, c.I9RepresentativeEmail, c.FingerprintAtiNumber,
            c.Qualifications.OrderBy(q => q.Id).Select(q => new QualificationInput(q.Kind, q.Name, q.DocumentToSubmit)).ToList(),
            c.Steps.OrderBy(s => s.Step!.Order).Select(s => new StepDetail(
                s.Step!.Key, s.Step.Order, s.Step.AuditReference, s.Step.Title, s.Step.Phase, s.Step.Owner, s.Step.Condition,
                s.Status, s.ActivatedAt, s.DueDate, s.DueTime, s.DueOverridden, CandidateJourney.IsOverdue(s, now),
                DescribeRule(s.Step.Deadline, clearanceDays), s.Step.DeadlineConfirmed, s.Step.DeadlineQuestion,
                s.Step.WatchForSender, s.Step.RequestMessageKey, s.Step.AcknowledgmentMessageKey, s.Step.VendorMessageKey,
                s.CompletedAt, s.CompletedBy?.Name, s.StaffNote,
                CandidateJourney.AllowedActions(s.Status))).ToList(),
            history,
            await messages.GetSentAsync(c.Id, ct),
            await db.CandidateAccessTokens.Where(t => t.CandidateId == c.Id).MaxAsync(t => t.ConsumedAt, ct),
            c.CreatedAt);
    }

    private async Task AuditAsync(int candidateId, StaffMember actor, string action, string detail, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            CandidateId = candidateId,
            At = clock.GetUtcNow(),
            ActorStaffId = actor.Id,
            ActorName = actor.Name,
            Action = action,
            Detail = detail,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A deadline rule in words, for staff deciding whether to trust or override it.</summary>
    public static string DescribeRule(DeadlineRule rule, string? clearanceDays)
    {
        string Unit(int n, DeadlineUnit unit) => unit switch
        {
            DeadlineUnit.Hours => $"{n} hours",
            DeadlineUnit.BusinessHours => $"{n} business hours",
            DeadlineUnit.CalendarDays => n == 1 ? "1 calendar day" : $"{n} calendar days",
            DeadlineUnit.BusinessDays => n == 1 ? "1 business day" : $"{n} business days",
            DeadlineUnit.Weeks => n == 1 ? "1 week" : $"{n} weeks",
            _ => $"{n} {unit}",
        };

        return rule.Anchor switch
        {
            DeadlineAnchor.None => "Set by staff",
            DeadlineAnchor.Activation => $"{Unit(rule.Amount, rule.Unit)} after the step starts",
            DeadlineAnchor.Clearance => $"Clearance deadline ({clearanceDays ?? "?"} business days before start)",
            DeadlineAnchor.BeforeStart => $"{Unit(rule.Amount, rule.Unit)} before the start date",
            DeadlineAnchor.AfterStart => $"{Unit(rule.Amount, rule.Unit)} after the start date",
            _ => rule.Anchor.ToString(),
        };
    }

    private static string ActionName(StepAction action) => action switch
    {
        StepAction.Activate => "activated",
        StepAction.MarkWaiting => "waiting",
        StepAction.Complete => "completed",
        StepAction.Block => "blocked",
        StepAction.Unblock => "unblocked",
        StepAction.Reopen => "reopened",
        _ => action.ToString().ToLowerInvariant(),
    };

    private static string FormatDue(DateOnly date, TimeOnly? time) =>
        time is { } t ? $"{date:yyyy-MM-dd} {t:HH:mm}" : $"{date:yyyy-MM-dd}";

    private static StaffSummary? Summary(StaffMember? s) => s is null ? null : new StaffSummary(s.Id, s.Name, s.Email, s.Role);
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string YesNo(bool value) => value ? "yes" : "no";

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
