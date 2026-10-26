using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Helpers;
using Server.Services;

namespace Server.Controllers.Admin;

public sealed record EmulationStaff(int Id, string Name, string Email, StaffRole Role, bool IsAdmin, IReadOnlyList<string> Teams);

public sealed record EmulationCandidate(int Id, string Name, string PositionTitle, string? CurrentStep);

public sealed record EmulationTargets(IReadOnlyList<EmulationStaff> Staff, IReadOnlyList<EmulationCandidate> Candidates);

/// <summary>
/// Admin → Emulate: see the app as any active staff member, or any candidate's checklist. Read-only
/// (<see cref="Emulation"/>); starting each is recorded with who did it.
/// </summary>
public class EmulationController(AppDbContext db, IUserService users, TimeProvider clock) : AdminControllerBase(users)
{
    [HttpGet("emulation")]
    public async Task<EmulationTargets> Targets(CancellationToken ct)
    {
        var staff = await db.Staff.Include(s => s.Teams).ThenInclude(m => m.Team)
            .Where(s => s.IsActive).OrderBy(s => s.Role).ThenBy(s => s.Name).ToListAsync(ct);
        var candidates = await JourneyData.CandidatesWithJourney(db).Where(c => c.ArchivedAt == null)
            .OrderBy(c => c.FirstName).ThenBy(c => c.LastName).ToListAsync(ct);

        return new EmulationTargets(
            staff.Select(s => new EmulationStaff(s.Id, s.Name, s.Email, s.Role, s.IsAdmin,
                s.Teams.Select(m => m.Team!.Name).Order().ToList())).ToList(),
            candidates.Select(c => new EmulationCandidate(c.Id, $"{c.FirstName} {c.LastName}", c.PositionTitle,
                CandidateJourney.CurrentStep(c)?.Step!.Title)).ToList());
    }

    /// <summary>From the next request on, the app is shown as this staff member until <see cref="Emulation.StopPath"/>.</summary>
    [HttpPost("emulation/staff/{id:int}")]
    public async Task<IActionResult> AsStaff(int id, CancellationToken ct)
    {
        var me = await Users.GetSignedInStaffMemberAsync(User, ct);
        if (me is null) return Forbid();
        if (Emulation.EmulatedStaffId(User) is not null) return Problem("Stop the current emulation first.", statusCode: 409);
        var target = await db.Staff.SingleOrDefaultAsync(s => s.Id == id && s.IsActive, ct);
        if (target is null) return NotFound();
        if (target.Id == me.Id) return Problem("That's you.", statusCode: 409);

        await Emulation.SetStaffAsync(HttpContext, target.Id);
        Audit(me, null, "emulation.started", $"{me.Name} started viewing the app as {target.Name} (read-only).");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Opens this candidate's checklist in the portal, read-only. The staff sign-in is untouched.</summary>
    [HttpPost("emulation/candidates/{id:int}")]
    public async Task<IActionResult> AsCandidate(int id, CancellationToken ct)
    {
        var me = await Users.GetSignedInStaffMemberAsync(User, ct);
        if (me is null) return Forbid();
        var candidate = await db.Candidates.SingleOrDefaultAsync(c => c.Id == id && c.ArchivedAt == null, ct);
        if (candidate is null) return NotFound();

        await HttpContext.SignInAsync(CandidateAuthentication.Scheme,
            CandidateAuthentication.CreateEmulatedPrincipal(candidate.Id, me.Name),
            new AuthenticationProperties { IsPersistent = false });
        Audit(me, candidate.Id, "emulation.checklist", $"{me.Name} viewed this checklist as {candidate.FirstName} (read-only).");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private void Audit(StaffMember actor, int? candidateId, string action, string detail) =>
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
