using Microsoft.AspNetCore.Mvc;
using Server.Core.Admin;
using Server.Services;

namespace Server.Controllers.Admin;

/// <summary>The staff whitelist and schedule-team membership. Changes apply on each person's next request.</summary>
public class StaffAdminController(StaffAdmin staff, IUserService users) : AdminControllerBase(users)
{
    [HttpGet("staff")]
    public async Task<StaffList> List(CancellationToken ct) => await staff.ListAsync(ct);

    [HttpPost("staff")]
    public Task<ActionResult<StaffSaved>> Create(StaffInput input, CancellationToken ct) =>
        AsAdmin<StaffSaved>(async me => await staff.CreateAsync(input, me, ct), ct);

    [HttpPut("staff/{id:int}")]
    public Task<ActionResult<StaffSaved>> Update(int id, StaffInput input, CancellationToken ct) =>
        AsAdmin<StaffSaved>(async me => await staff.UpdateAsync(id, input, me, ct) is { } saved ? saved : NotFound(), ct);
}
