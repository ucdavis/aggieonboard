using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;

namespace Server.Core.Admin;

public sealed record StaffAdminView(
    int Id,
    string Name,
    string Email,
    string? Phone,
    StaffRole Role,
    bool IsActive,
    bool IsAdmin,
    IReadOnlyList<int> TeamIds,
    int AssignedCandidates);

public sealed record TeamView(int Id, string Key, string Name, IReadOnlyList<string> Locations);

public sealed record StaffList(IReadOnlyList<StaffAdminView> Staff, IReadOnlyList<TeamView> Teams);

public sealed record StaffInput(
    string? Name, string? Email, string? Phone, StaffRole Role, bool IsAdmin, bool IsActive, IReadOnlyList<int>? TeamIds);

public sealed record StaffSaved(StaffAdminView Member, string? Notice);

/// <summary>
/// The staff whitelist (Phase 6). Who may sign in, with which journey role, whether they are an admin,
/// and which schedule teams they belong to. Roles are re-read on every request, so a change here
/// applies at once. An admin can't lock themselves out, and someone can't change role while
/// candidates still name them in the old one.
/// </summary>
public sealed partial class StaffAdmin(AppDbContext db, TimeProvider clock)
{
    public async Task<StaffList> ListAsync(CancellationToken ct = default)
    {
        var staff = await db.Staff.Include(s => s.Teams).OrderBy(s => !s.IsActive).ThenBy(s => s.Name).ToListAsync(ct);
        var assigned = await AssignedCountsAsync(ct);
        var teams = await db.Teams.OrderBy(t => t.Name).ToListAsync(ct);
        var locations = await db.Locations.Select(l => new { l.TeamId, l.Name }).ToListAsync(ct);

        return new StaffList(
            staff.Select(s => View(s, assigned.GetValueOrDefault(s.Id))).ToList(),
            teams.Select(t => new TeamView(t.Id, t.Key, t.Name, locations.Where(l => l.TeamId == t.Id).Select(l => l.Name).ToList())).ToList());
    }

    public async Task<StaffSaved> CreateAsync(StaffInput input, StaffMember actor, CancellationToken ct = default)
    {
        await ValidateAsync(input, null, ct);
        var member = new StaffMember();
        Assign(member, input);
        member.Teams = (input.TeamIds ?? []).Distinct().Select(id => new TeamMembership { TeamId = id }).ToList();
        db.Staff.Add(member);
        await db.SaveChangesAsync(ct);

        Audit(actor, "staff.added", $"{member.Name} added: {Describe(member, await TeamNamesAsync(member, ct))}.");
        await db.SaveChangesAsync(ct);
        return new StaffSaved(View(member, 0), null);
    }

    public async Task<StaffSaved?> UpdateAsync(int id, StaffInput input, StaffMember actor, CancellationToken ct = default)
    {
        var member = await db.Staff.Include(s => s.Teams).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (member is null) return null;
        await ValidateAsync(input, id, ct);

        if (member.Id == actor.Id && (!input.IsAdmin || !input.IsActive))
        {
            throw new JourneyRuleException("You can't remove your own admin access or deactivate yourself. Ask another admin.");
        }

        var assignedAs = await AssignedAsync(member, ct);
        if (input.Role != member.Role && assignedAs > 0)
        {
            throw new JourneyRuleException(
                $"{member.Name} is the {Label(member.Role)} for {Count(assignedAs)}. Reassign them on each candidate's page first.");
        }

        var before = Describe(member, await TeamNamesAsync(member, ct));
        Assign(member, input);
        var wanted = (input.TeamIds ?? []).Distinct().ToHashSet();
        member.Teams.RemoveAll(m => !wanted.Contains(m.TeamId));
        foreach (var teamId in wanted.Where(t => member.Teams.All(m => m.TeamId != t)))
        {
            member.Teams.Add(new TeamMembership { TeamId = teamId, StaffMemberId = member.Id });
        }
        await db.SaveChangesAsync(ct);

        var after = Describe(member, await TeamNamesAsync(member, ct));
        if (after != before) Audit(actor, "staff.updated", $"{member.Name}: {before} → {after}.");
        await db.SaveChangesAsync(ct);

        var notice = !member.IsActive && assignedAs > 0
            ? $"{member.Name} can no longer sign in, but is still the {Label(member.Role)} for {Count(assignedAs)}. Reassign them on each candidate's page."
            : null;
        return new StaffSaved(View(member, assignedAs), notice);
    }

    private async Task ValidateAsync(StaffInput input, int? id, CancellationToken ct)
    {
        var errors = new Dictionary<string, List<string>>();
        void Error(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        var name = input.Name?.Trim() ?? "";
        var email = input.Email?.Trim() ?? "";
        if (name.Length == 0) Error("name", "Name is required.");
        else if (name.Length > 200) Error("name", "Name must be 200 characters or fewer.");
        if (email.Length == 0) Error("email", "Email is required.");
        else if (email.Length > 320 || !EmailPattern().IsMatch(email)) Error("email", "Enter an email address like name@ucdavis.edu.");
        else
        {
            var normalized = email.ToLowerInvariant();
            if (await db.Staff.AnyAsync(s => s.Id != id && s.Email.ToLower() == normalized, ct))
            {
                Error("email", "Someone on the staff list already has this email.");
            }
        }
        if (input.Phone?.Trim().Length > 40) Error("phone", "Phone must be 40 characters or fewer.");
        if (!Enum.IsDefined(input.Role)) Error("role", "Choose a role.");

        var teamIds = (input.TeamIds ?? []).Distinct().ToList();
        if (teamIds.Count > 0 && await db.Teams.CountAsync(t => teamIds.Contains(t.Id), ct) != teamIds.Count)
        {
            Error("teamIds", "Choose teams from the list.");
        }

        if (errors.Count > 0) throw new JourneyValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }

    private static void Assign(StaffMember member, StaffInput input)
    {
        member.Name = input.Name!.Trim();
        member.Email = input.Email!.Trim();
        member.Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();
        member.Role = input.Role;
        member.IsAdmin = input.IsAdmin;
        member.IsActive = input.IsActive;
    }

    /// <summary>Active candidates who name this person in the role they hold now.</summary>
    private async Task<int> AssignedAsync(StaffMember member, CancellationToken ct)
    {
        var active = db.Candidates.Where(c => c.ArchivedAt == null);
        return member.Role switch
        {
            StaffRole.RecruitingAssistant => await active.CountAsync(c => c.RecruitingAssistantId == member.Id, ct),
            StaffRole.Recruiter => await active.CountAsync(c => c.RecruiterId == member.Id, ct),
            StaffRole.OnboardingSpecialist => await active.CountAsync(c => c.OnboardingSpecialistId == member.Id, ct),
            _ => 0,
        };
    }

    private async Task<Dictionary<int, int>> AssignedCountsAsync(CancellationToken ct)
    {
        var rows = await db.Candidates.Where(c => c.ArchivedAt == null)
            .Select(c => new { c.RecruitingAssistantId, c.RecruiterId, c.OnboardingSpecialistId })
            .ToListAsync(ct);
        return rows.SelectMany(r => new[] { r.RecruitingAssistantId, r.RecruiterId, r.OnboardingSpecialistId })
            .OfType<int>()
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private async Task<List<string>> TeamNamesAsync(StaffMember member, CancellationToken ct)
    {
        var ids = member.Teams.Select(m => m.TeamId).ToList();
        return await db.Teams.Where(t => ids.Contains(t.Id)).OrderBy(t => t.Name).Select(t => t.Name).ToListAsync(ct);
    }

    private static string Describe(StaffMember m, IReadOnlyList<string> teams) =>
        $"{m.Email}, {Label(m.Role)}{(m.IsAdmin ? ", admin" : "")}{(m.IsActive ? "" : ", inactive")}"
        + (teams.Count > 0 ? $", teams: {string.Join(", ", teams)}" : "");

    private static string Label(StaffRole role) => role switch
    {
        StaffRole.RecruitingAssistant => "recruiting assistant",
        StaffRole.Recruiter => "recruiter",
        StaffRole.OnboardingSpecialist => "onboarding specialist",
        StaffRole.LearningAndDevelopment => "Learning and Development contact",
        StaffRole.TeamMember => "team member",
        _ => role.ToString(),
    };

    private static string Count(int n) => n == 1 ? "1 candidate" : $"{n} candidates";

    private static StaffAdminView View(StaffMember s, int assigned) => new(
        s.Id, s.Name, s.Email, s.Phone, s.Role, s.IsActive, s.IsAdmin, s.Teams.Select(t => t.TeamId).Order().ToList(), assigned);

    private void Audit(StaffMember actor, string action, string detail) =>
        db.AuditEvents.Add(new AuditEvent
        {
            At = clock.GetUtcNow(),
            ActorStaffId = actor.Id == 0 ? null : actor.Id,
            ActorName = actor.Name,
            Action = action,
            Detail = detail,
        });

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
