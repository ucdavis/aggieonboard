using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Services;

namespace Server.Controllers;

public sealed record EmulationStatus(string SignedInAs, string ViewingAs, StaffRole Role);

/// <summary>
/// Whether an admin is viewing the app as someone else, and how they stop. Outside /api/admin on
/// purpose: while emulating a non-admin the Admin role is gone, and stopping must still work.
/// </summary>
[Authorize]
[ApiController]
[Route("api/user/emulation")]
public class EmulationStatusController(IUserService users, AppDbContext db, TimeProvider clock) : ControllerBase
{
    /// <summary>The banner's facts, or 204 when nobody is being emulated.</summary>
    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var signedIn = await users.GetSignedInStaffMemberAsync(User, ct);
        var viewing = await users.GetStaffMemberAsync(User, ct);
        if (signedIn is null || viewing is null || signedIn.Id == viewing.Id) return NoContent();
        return Ok(new EmulationStatus(signedIn.Name, viewing.Name, viewing.Role));
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop(CancellationToken ct)
    {
        var signedIn = await users.GetSignedInStaffMemberAsync(User, ct);
        var viewing = await users.GetStaffMemberAsync(User, ct);
        await Emulation.SetStaffAsync(HttpContext, null);
        if (signedIn is not null && viewing is not null && signedIn.Id != viewing.Id)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                At = clock.GetUtcNow(),
                ActorStaffId = signedIn.Id,
                ActorName = signedIn.Name,
                Action = "emulation.stopped",
                Detail = $"{signedIn.Name} stopped viewing the app as {viewing.Name}.",
            });
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }
}
