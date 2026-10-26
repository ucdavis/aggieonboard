using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Scheduling;

namespace Server.Core.Admin;

public sealed record SettingView(
    string Key, string? Value, SettingKind Kind, string Description, bool IsConfirmed, string? OpenQuestion);

public sealed record SettingEdit(string? Value, bool IsConfirmed, string? OpenQuestion);

public sealed record StepDeadlineView(
    string Key,
    int Order,
    string AuditReference,
    string Title,
    DeadlineAnchor Anchor,
    int Amount,
    DeadlineUnit Unit,
    string Rule,
    bool Confirmed,
    string? Question);

public sealed record StepDeadlineEdit(DeadlineAnchor Anchor, int Amount, DeadlineUnit Unit, bool Confirmed, string? Question);

public sealed record HolidayView(DateOnly Date, string Name);

public sealed record HolidayInput(DateOnly? Date, string? Name);

/// <summary>What a change did besides itself: how many open due dates moved.</summary>
public sealed record AdminChangeResult(string Notice);

public sealed record AdminHistoryEntry(DateTimeOffset At, string ActorName, string Action, string Detail);

/// <summary>
/// The process facts the audit left open (AGENTS.md, invariant 5): settings, each step's deadline
/// rule, and the holiday calendar. Every change is recorded with who made it, and a change that
/// moves deadlines recomputes every open step that staff haven't set by hand, noting each move in
/// that candidate's history.
/// </summary>
public sealed class SettingsAdmin(AppDbContext db, TimeProvider clock)
{
    public const int TextMax = 1000;
    public const int IntegerMax = 365;

    public async Task<IReadOnlyList<SettingView>> ListSettingsAsync(CancellationToken ct = default)
    {
        var order = SettingKeys.All.Select((key, i) => (key, i)).ToDictionary(p => p.key, p => p.i);
        return (await db.Settings.ToListAsync(ct))
            .OrderBy(s => order.GetValueOrDefault(s.Key, int.MaxValue))
            .Select(s => new SettingView(s.Key, s.Value, s.Kind, s.Description, s.IsConfirmed, s.OpenQuestion))
            .ToList();
    }

    public async Task<AdminChangeResult?> UpdateSettingAsync(string key, SettingEdit edit, StaffMember actor, CancellationToken ct = default)
    {
        var setting = await db.Settings.SingleOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null) return null;

        var value = Blank(edit.Value);
        var errors = new Dictionary<string, string[]>();
        if (value is not null)
        {
            var problem = setting.Kind switch
            {
                SettingKind.Integer => int.TryParse(value, out var n) && n >= 0 && n <= IntegerMax
                    ? null : $"Enter a whole number from 0 to {IntegerMax}.",
                SettingKind.Url => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                    ? null : "Enter a full link, starting with https://.",
                _ => value.Length > TextMax ? $"Keep it to {TextMax} characters." : null,
            };
            if (problem is not null) errors["value"] = [problem];
        }
        if (value is null && edit.IsConfirmed) errors["isConfirmed"] = ["A confirmed setting needs a value."];
        if (Blank(edit.OpenQuestion)?.Length > TextMax) errors["openQuestion"] = [$"Keep it to {TextMax} characters."];
        if (errors.Count > 0) throw new JourneyValidationException(errors);

        var before = Describe(setting.Value, setting.IsConfirmed);
        setting.Value = value;
        setting.IsConfirmed = edit.IsConfirmed;
        setting.OpenQuestion = Blank(edit.OpenQuestion);
        var after = Describe(setting.Value, setting.IsConfirmed);
        Audit(actor, "setting.changed", $"{setting.Key}: {before} → {after}");
        await db.SaveChangesAsync(ct);

        // The clearance deadline moves every clearance-anchored due date; the recompute reads the saved value.
        var moved = key == SettingKeys.ClearanceBusinessDaysBeforeStart
            ? await RecomputeDuesAsync(actor, $"{setting.Key} changed", ct)
            : 0;
        await db.SaveChangesAsync(ct);
        return new AdminChangeResult(Notice($"{setting.Key} saved.", moved));
    }

    public async Task<IReadOnlyList<StepDeadlineView>> ListStepDeadlinesAsync(CancellationToken ct = default)
    {
        var clearanceDays = await ClearanceDaysAsync(ct);
        return (await db.StepDefinitions.OrderBy(s => s.Order).ToListAsync(ct))
            .Select(s => new StepDeadlineView(
                s.Key, s.Order, s.AuditReference, s.Title, s.Deadline.Anchor, s.Deadline.Amount, s.Deadline.Unit,
                JourneyService.DescribeRule(s.Deadline, clearanceDays), s.DeadlineConfirmed, s.DeadlineQuestion))
            .ToList();
    }

    public async Task<AdminChangeResult?> UpdateStepDeadlineAsync(
        string key, StepDeadlineEdit edit, StaffMember actor, CancellationToken ct = default)
    {
        var step = await db.StepDefinitions.SingleOrDefaultAsync(s => s.Key == key, ct);
        if (step is null) return null;

        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(edit.Anchor)) errors["anchor"] = ["Choose what the deadline counts from."];
        if (!Enum.IsDefined(edit.Unit)) errors["unit"] = ["Choose a unit."];
        if (edit.Anchor == DeadlineAnchor.Activation && (edit.Amount < 1 || edit.Amount > 1000))
        {
            errors["amount"] = ["Enter a whole number from 1 to 1,000."];
        }
        if (edit.Anchor is DeadlineAnchor.BeforeStart or DeadlineAnchor.AfterStart)
        {
            if (edit.Amount < 0 || edit.Amount > IntegerMax) errors["amount"] = [$"Enter a whole number from 0 to {IntegerMax}."];
            if (edit.Unit is DeadlineUnit.Hours or DeadlineUnit.BusinessHours) errors["unit"] = ["Count days or weeks from the start date."];
        }
        if (Blank(edit.Question)?.Length > TextMax) errors["question"] = [$"Keep it to {TextMax} characters."];
        if (errors.Count > 0) throw new JourneyValidationException(errors);

        // Anchors without an amount are stored in their one form, so equal rules compare equal.
        var rule = edit.Anchor switch
        {
            DeadlineAnchor.None => DeadlineRule.None,
            DeadlineAnchor.Clearance => DeadlineRule.Clearance,
            _ => new DeadlineRule(edit.Anchor, edit.Amount, edit.Unit),
        };
        var clearanceDays = await ClearanceDaysAsync(ct);
        var before = $"{JourneyService.DescribeRule(step.Deadline, clearanceDays)}{(step.DeadlineConfirmed ? " (confirmed)" : "")}";
        var ruleChanged = rule != step.Deadline;
        if (ruleChanged) step.Deadline = rule;
        step.DeadlineConfirmed = edit.Confirmed;
        step.DeadlineQuestion = Blank(edit.Question);
        var after = $"{JourneyService.DescribeRule(step.Deadline, clearanceDays)}{(step.DeadlineConfirmed ? " (confirmed)" : "")}";
        Audit(actor, "step-deadline.changed", $"{step.Title}: {before} → {after}");

        // Saved first so the recompute reads the new rule.
        await db.SaveChangesAsync(ct);
        var moved = ruleChanged ? await RecomputeDuesAsync(actor, $"{step.Title}'s deadline rule changed", ct) : 0;
        await db.SaveChangesAsync(ct);
        return new AdminChangeResult(Notice($"{step.Title} saved.", moved));
    }

    public async Task<IReadOnlyList<HolidayView>> ListHolidaysAsync(CancellationToken ct = default) =>
        await db.Holidays.OrderBy(h => h.Date).Select(h => new HolidayView(h.Date, h.Name)).ToListAsync(ct);

    public async Task<AdminChangeResult> AddHolidayAsync(HolidayInput input, StaffMember actor, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        var name = Blank(input.Name);
        if (input.Date is null) errors["date"] = ["Choose the date."];
        else if (await db.Holidays.AnyAsync(h => h.Date == input.Date, ct)) errors["date"] = ["That date is already a holiday."];
        if (name is null) errors["name"] = ["Name the holiday."];
        else if (name.Length > 200) errors["name"] = ["Keep it to 200 characters."];
        if (errors.Count > 0) throw new JourneyValidationException(errors);

        db.Holidays.Add(new Holiday { Date = input.Date!.Value, Name = name! });
        Audit(actor, "holiday.added", $"Holiday added: {name}, {input.Date:dddd, MMMM d, yyyy}.");
        await db.SaveChangesAsync(ct);
        var moved = await RecomputeDuesAsync(actor, $"{name} added as a holiday", ct);
        await db.SaveChangesAsync(ct);
        return new AdminChangeResult(Notice($"{name} added.", moved));
    }

    public async Task<AdminChangeResult?> RemoveHolidayAsync(DateOnly date, StaffMember actor, CancellationToken ct = default)
    {
        var holiday = await db.Holidays.SingleOrDefaultAsync(h => h.Date == date, ct);
        if (holiday is null) return null;

        db.Holidays.Remove(holiday);
        Audit(actor, "holiday.removed", $"Holiday removed: {holiday.Name}, {date:dddd, MMMM d, yyyy}.");
        await db.SaveChangesAsync(ct);
        var moved = await RecomputeDuesAsync(actor, $"{holiday.Name} removed as a holiday", ct);
        await db.SaveChangesAsync(ct);
        return new AdminChangeResult(Notice($"{holiday.Name} removed.", moved));
    }

    /// <summary>Changes to settings, deadlines, holidays, templates and staff: the shared data, newest first.</summary>
    public async Task<IReadOnlyList<AdminHistoryEntry>> HistoryAsync(int take = 100, CancellationToken ct = default) =>
        await db.AuditEvents
            .Where(e => e.CandidateId == null)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Take(take)
            .Select(e => new AdminHistoryEntry(e.At, e.ActorName, e.Action, e.Detail))
            .ToListAsync(ct);

    /// <summary>
    /// Recomputes every open, staff-untouched due date of every active candidate under the current
    /// rules, and notes each one that moved in that candidate's history. Returns how many moved.
    /// </summary>
    private async Task<int> RecomputeDuesAsync(StaffMember actor, string reason, CancellationToken ct)
    {
        var deadlines = await JourneyData.LoadDeadlinesAsync(db, ct);
        var candidates = await JourneyData.CandidatesWithJourney(db).Where(c => c.ArchivedAt == null).ToListAsync(ct);
        var moved = 0;
        foreach (var candidate in candidates)
        {
            var before = candidate.Steps.ToDictionary(s => s.Id, s => (s.DueDate, s.DueTime));
            CandidateJourney.RecomputeDues(candidate, deadlines);
            foreach (var step in candidate.Steps.Where(s => before[s.Id] != (s.DueDate, s.DueTime)))
            {
                moved++;
                db.AuditEvents.Add(new AuditEvent
                {
                    CandidateId = candidate.Id,
                    At = clock.GetUtcNow(),
                    ActorStaffId = StaffId(actor),
                    ActorName = actor.Name,
                    Action = "step.due-recomputed",
                    Detail = $"{step.Step!.Title}: due {FormatDue(before[step.Id])} → {FormatDue((step.DueDate, step.DueTime))} ({reason})",
                });
            }
        }
        return moved;
    }

    private async Task<string?> ClearanceDaysAsync(CancellationToken ct) =>
        await db.Settings.Where(s => s.Key == SettingKeys.ClearanceBusinessDaysBeforeStart).Select(s => s.Value).SingleOrDefaultAsync(ct);

    private void Audit(StaffMember actor, string action, string detail) =>
        db.AuditEvents.Add(new AuditEvent
        {
            At = clock.GetUtcNow(),
            ActorStaffId = StaffId(actor),
            ActorName = actor.Name,
            Action = action,
            Detail = detail,
        });

    private static string Notice(string saved, int moved) => moved switch
    {
        0 => saved,
        1 => $"{saved} 1 open due date moved; it's noted in that candidate's history.",
        _ => $"{saved} {moved} open due dates moved; each is noted in that candidate's history.",
    };

    private static string Describe(string? value, bool confirmed) =>
        (value is null ? "no value" : $"“{value}”") + (confirmed ? " (confirmed)" : "");

    private static string FormatDue((DateOnly? Date, TimeOnly? Time) due) =>
        due.Date is not { } date ? "none" : due.Time is { } t ? $"{date:yyyy-MM-dd} {t:HH:mm}" : $"{date:yyyy-MM-dd}";

    private static int? StaffId(StaffMember actor) => actor.Id == 0 ? null : actor.Id;
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
