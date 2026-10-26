using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Controllers.Staff;
using Server.Core.Domain;
using Server.Services;

namespace Server.Controllers.Admin;

/// <summary>
/// Everything under <c>/api/admin</c>: templates, settings, holidays and the staff list. Admins only
/// (<see cref="StaffMember.IsAdmin"/>, re-read from the whitelist on every request). Errors follow
/// the staff API: 409 for a refused change, 400 with field errors for bad input.
/// </summary>
[Authorize(Roles = AppRoles.Admin)]
[ApiController]
[Route("api/admin")]
[JourneyErrors]
public abstract class AdminControllerBase(IUserService users) : ControllerBase
{
    /// <summary>Changes are recorded as a named admin. A missing row means the whitelist changed mid-request.</summary>
    protected async Task<ActionResult<T>> AsAdmin<T>(Func<StaffMember, Task<ActionResult<T>>> change, CancellationToken ct)
    {
        var me = await users.GetStaffMemberAsync(User, ct);
        return me is null ? Forbid() : await change(me);
    }
}
