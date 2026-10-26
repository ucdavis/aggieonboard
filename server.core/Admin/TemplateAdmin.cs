using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Messaging;

namespace Server.Core.Admin;

public sealed record TemplateSummary(
    string Key,
    string AuditReference,
    MessageSender Sender,
    string Subject,
    int Version,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    IReadOnlyList<string> Steps);

public sealed record TemplateSectionView(CandidateCondition Condition, string Body);

public sealed record TemplateVersionSummary(int Version, DateTimeOffset CreatedAt, string CreatedBy);

public sealed record TemplateFieldView(string Name, string Description);

public sealed record PreviewCandidate(int Id, string Name);

/// <summary>
/// One version of a message, with what the editor needs around it. <see cref="LatestVersion"/> is the
/// one that sends; saving is refused unless the edit started from it.
/// </summary>
public sealed record TemplateDetail(
    string Key,
    string AuditReference,
    MessageSender Sender,
    int Version,
    int LatestVersion,
    string Subject,
    IReadOnlyList<TemplateSectionView> Sections,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    IReadOnlyList<TemplateVersionSummary> Versions,
    IReadOnlyList<string> Steps,
    IReadOnlyList<TemplateFieldView> Fields,
    IReadOnlyList<PreviewCandidate> PreviewCandidates);

public sealed record TemplateEdit(string? Subject, IReadOnlyList<TemplateSectionView>? Sections, int BasedOnVersion);

public sealed record TemplatePreviewRequest(int CandidateId, string? Subject, IReadOnlyList<TemplateSectionView>? Sections);

/// <summary>
/// The template editor (Phase 6). An edit is a new version, never a rewrite, so every sent message
/// still traces to its exact wording (AGENTS.md, invariant 7); the newest version is what sends.
/// A version is saved only if every merge field in it is one the app can fill there (invariant 6).
/// Writing the words is communications' job (invariant 8); this only keeps them sendable.
/// </summary>
public sealed partial class TemplateAdmin(AppDbContext db, TimeProvider clock, MessageService messages)
{
    public const int SubjectMax = 300;
    public const int BodyMax = 20_000;

    public async Task<IReadOnlyList<TemplateSummary>> ListAsync(CancellationToken ct = default)
    {
        var latest = await LatestVersionsAsync(ct);
        var steps = await db.StepDefinitions.OrderBy(s => s.Order).ToListAsync(ct);
        return latest
            .OrderBy(t => SystemMessages.Keys.Contains(t.Key))
            .ThenBy(t => AuditOrder(t.AuditReference))
            .ThenBy(t => t.Key)
            .Select(t => new TemplateSummary(t.Key, t.AuditReference, t.Sender, t.Subject, t.Version, t.CreatedAt, t.CreatedBy, StepsFor(steps, t.Key)))
            .ToList();
    }

    /// <summary>The message at <paramref name="version"/>, or its latest version when null.</summary>
    public async Task<TemplateDetail?> GetAsync(string key, int? version = null, CancellationToken ct = default)
    {
        var versions = await db.MessageTemplates
            .Where(t => t.Key == key)
            .OrderByDescending(t => t.Version)
            .Select(t => new TemplateVersionSummary(t.Version, t.CreatedAt, t.CreatedBy))
            .ToListAsync(ct);
        if (versions.Count == 0) return null;

        var wanted = version ?? versions[0].Version;
        var template = await db.MessageTemplates.Include(t => t.Sections)
            .SingleOrDefaultAsync(t => t.Key == key && t.Version == wanted, ct);
        if (template is null) return null;

        var steps = await db.StepDefinitions.OrderBy(s => s.Order).ToListAsync(ct);
        var candidates = await db.Candidates
            .Where(c => c.ArchivedAt == null)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Select(c => new PreviewCandidate(c.Id, c.FirstName + " " + c.LastName))
            .ToListAsync(ct);

        return new TemplateDetail(
            template.Key, template.AuditReference, template.Sender, template.Version, versions[0].Version, template.Subject,
            template.Sections.OrderBy(s => s.Order).Select(s => new TemplateSectionView(s.Condition, s.Body)).ToList(),
            template.CreatedAt, template.CreatedBy, versions, StepsFor(steps, key),
            FieldsFor(template.Key, template.Sender).Select(f => new TemplateFieldView(f.Name, f.Description)).ToList(),
            candidates);
    }

    /// <summary>Unsaved wording, filled in for one candidate. Problems that would stop a save show as gaps, not errors.</summary>
    public async Task<MessagePreview?> PreviewAsync(string key, TemplatePreviewRequest request, StaffMember actor, CancellationToken ct = default)
    {
        var current = await LatestAsync(key, ct);
        if (current is null) return null;
        return await messages.PreviewDraftAsync(request.CandidateId, Unsaved(current, request.Subject, request.Sections), actor, ct);
    }

    /// <summary>Saves the edit as the next version, which sends from now on.</summary>
    public async Task<TemplateDetail?> SaveAsync(string key, TemplateEdit edit, StaffMember actor, CancellationToken ct = default)
    {
        var current = await LatestAsync(key, ct);
        if (current is null) return null;

        if (edit.BasedOnVersion != current.Version)
        {
            throw new JourneyRuleException(
                $"Version {current.Version} was saved by {current.CreatedBy} after you started editing. Reload to see it, then make your change again.");
        }
        Validate(current, edit);

        var subject = edit.Subject!.Trim();
        var sections = edit.Sections!.Select(s => new TemplateSectionView(s.Condition, Tidy(s.Body))).ToList();
        if (subject == current.Subject
            && sections.SequenceEqual(current.Sections.OrderBy(s => s.Order).Select(s => new TemplateSectionView(s.Condition, s.Body))))
        {
            throw new JourneyRuleException("Nothing has changed since version " + current.Version + ".");
        }

        var saved = Unsaved(current, subject, sections);
        saved.Version = current.Version + 1;
        saved.CreatedAt = clock.GetUtcNow();
        saved.CreatedBy = actor.Email;
        db.MessageTemplates.Add(saved);
        db.AuditEvents.Add(new AuditEvent
        {
            At = saved.CreatedAt,
            ActorStaffId = actor.Id == 0 ? null : actor.Id,
            ActorName = actor.Name,
            Action = "template.saved",
            Detail = $"{Describe(current)}: version {saved.Version} saved (was version {current.Version}).",
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Someone else saved the same version number a moment earlier (unique key and version).
            throw new JourneyRuleException($"Someone saved {Describe(current)} at the same moment. Reload to see their version.");
        }
        return await GetAsync(key, null, ct);
    }

    /// <summary>Field errors keyed as the editor names them: "subject", "sections", "sections[1].body".</summary>
    private static void Validate(MessageTemplate current, TemplateEdit edit)
    {
        var errors = new Dictionary<string, List<string>>();
        void Error(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        var allowed = FieldsFor(current.Key, current.Sender).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        void CheckText(string field, string text)
        {
            foreach (var name in MergeFields.In(text).Where(n => !allowed.Contains(n)))
            {
                Error(field, current.Sender == MessageSender.External
                    ? $"{{{{{name}}}}}: a vendor sends this message, so the app can't fill merge fields in it."
                    : MergeFields.AppointmentOnly.Contains(name)
                        ? $"{{{{{name}}}}} only has a value in appointment emails."
                        : MergeFields.NewTasksOnly.Contains(name)
                            ? $"{{{{{name}}}}} only has a value in the new-steps email."
                            : $"{{{{{name}}}}} isn't a field the app can fill. Use one from the list.");
            }
            if (current.Sender == MessageSender.App)
            {
                foreach (Match m in LeftoverPlaceholder().Matches(text))
                {
                    Error(field, $"{m.Value} looks like a placeholder. Use a merge field, or give it a link: {m.Value}(https://…).");
                }
            }
            foreach (Match link in LinkTarget().Matches(text))
            {
                var target = link.Groups[1].Value;
                if (!(target.StartsWith("https://") || target.StartsWith("http://") || target.StartsWith("mailto:") || target.StartsWith("{{")))
                {
                    Error(field, $"Links must start with https://, http:// or mailto: ({target}).");
                }
            }
            if (MalformedField().IsMatch(text))
            {
                Error(field, "A merge field is written {{FieldName}}, with no spaces or punctuation inside.");
            }
        }

        var subject = edit.Subject?.Trim() ?? "";
        if (subject.Length == 0) Error("subject", "The subject is required.");
        else if (subject.Length > SubjectMax) Error("subject", $"The subject must be {SubjectMax} characters or fewer.");
        else CheckText("subject", subject);

        var sections = edit.Sections ?? [];
        if (sections.Count == 0) Error("sections", "A message needs at least one section.");
        for (var i = 0; i < sections.Count; i++)
        {
            var field = $"sections[{i}].body";
            var body = sections[i].Body ?? "";
            if (!Enum.IsDefined(sections[i].Condition)) Error($"sections[{i}].condition", "Choose who this section is for.");
            if (body.Trim().Length == 0) Error(field, "This section is empty. Write something or remove it.");
            else if (body.Length > BodyMax) Error(field, $"A section must be {BodyMax:N0} characters or fewer.");
            else
            {
                CheckText(field, body);
                if (ConditionMarker().IsMatch(body)) Error(field, "Choose the section's audience instead of writing <!-- if: … --> markers.");
            }
        }

        if (errors.Count > 0) throw new JourneyValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }

    /// <summary>
    /// What a message may use: every field for an app message except the appointment ones, which only
    /// the appointment emails have; nothing for a vendor's wording, which the app never fills.
    /// </summary>
    private static IEnumerable<MergeFields.Field> FieldsFor(string key, MessageSender sender) =>
        sender == MessageSender.External ? []
        : MergeFields.All.Where(f =>
            (!MergeFields.AppointmentOnly.Contains(f.Name) || SystemMessages.Appointment.Contains(key))
            && (!MergeFields.NewTasksOnly.Contains(f.Name) || key == SystemMessages.NewTasks));

    private static MessageTemplate Unsaved(MessageTemplate current, string? subject, IReadOnlyList<TemplateSectionView>? sections) => new()
    {
        Key = current.Key,
        Version = current.Version,
        AuditReference = current.AuditReference,
        Sender = current.Sender,
        Subject = subject?.Trim() ?? "",
        Sections = (sections ?? [])
            .Select((s, i) => new MessageTemplateSection { Order = i + 1, Condition = s.Condition, Body = Tidy(s.Body) })
            .ToList(),
    };

    /// <summary>Stored as the seeds are: Unix line endings, no blank lines at either end, one closing newline.</summary>
    private static string Tidy(string? body) => (body ?? "").Replace("\r\n", "\n").Trim('\n').TrimEnd() + "\n";

    private async Task<MessageTemplate?> LatestAsync(string key, CancellationToken ct) =>
        await db.MessageTemplates.Include(t => t.Sections)
            .Where(t => t.Key == key)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(ct);

    private async Task<List<MessageTemplate>> LatestVersionsAsync(CancellationToken ct)
    {
        var all = await db.MessageTemplates.ToListAsync(ct);
        return all.GroupBy(t => t.Key).Select(g => g.MaxBy(t => t.Version)!).ToList();
    }

    private static IReadOnlyList<string> StepsFor(IEnumerable<StepDefinition> steps, string key) =>
        steps.Where(s => s.RequestMessageKey == key || s.AcknowledgmentMessageKey == key || s.VendorMessageKey == key)
            .Select(s => s.Title)
            .ToList();

    /// <summary>"11A" sorts after "10" and before "11B".</summary>
    private static (int, string) AuditOrder(string reference)
    {
        var digits = new string(reference.TakeWhile(char.IsDigit).ToArray());
        return (int.TryParse(digits, out var n) ? n : int.MaxValue, reference);
    }

    private static string Describe(MessageTemplate t) =>
        SystemMessages.Keys.Contains(t.Key) ? $"The {t.Key} message" : $"Message {t.AuditReference}";

    // As the seeding tests check: a [bracket] that isn't a Markdown link is a placeholder left in.
    [GeneratedRegex(@"\[[^\]\n]+\](?!\()")]
    private static partial Regex LeftoverPlaceholder();

    [GeneratedRegex(@"\{\{(?!\s*[A-Za-z][A-Za-z0-9]*\s*\}\})")]
    private static partial Regex MalformedField();

    // The page shows message bodies too, so a link can only go somewhere ordinary.
    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex LinkTarget();

    [GeneratedRegex(@"<!--\s*(if:|end)")]
    private static partial Regex ConditionMarker();
}
