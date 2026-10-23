using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Controllers.Staff;
using Server.Core.Domain;
using Server.Core.Scheduling;
using Server.Services;

namespace Server.Controllers.Schedule;

public sealed record AttendanceRequest(bool Attended);
public sealed record CancelRequest(string? Reason);

/// <summary>
/// The schedule, for the teams that run appointments: day rosters, attendance, and each location's
/// hours, sessions and closures. A member sees only their teams' locations; an admin sees all.
/// </summary>
[Authorize(Roles = AppRoles.Scheduling)]
[ApiController]
[Route("api/schedule")]
[JourneyErrors]
public class ScheduleController(SchedulingService scheduling, IUserService users) : ControllerBase
{
    [HttpGet("locations")]
    public Task<ActionResult<IReadOnlyList<LocationSummary>>> Locations(CancellationToken ct) =>
        AsMember<IReadOnlyList<LocationSummary>>(async me => Ok(await scheduling.GetManagedLocationsAsync(me, ct)), ct);

    /// <summary>Who is coming on <paramref name="date"/> (default today), at every location this person runs or one of them.</summary>
    [HttpGet("roster")]
    public Task<ActionResult<IReadOnlyList<RosterLocation>>> Roster([FromQuery] DateOnly? date, [FromQuery] int? locationId, CancellationToken ct) =>
        AsMember<IReadOnlyList<RosterLocation>>(async me =>
        {
            var day = date ?? DateOnly.FromDateTime(BusinessCalendar.ToPacific(DateTimeOffset.UtcNow));
            return Ok(await scheduling.GetRosterAsync(me, day, locationId, ct));
        }, ct);

    [HttpPost("appointments/{id:int}/attendance")]
    public Task<ActionResult<AppointmentSummary>> Attendance(int id, AttendanceRequest request, CancellationToken ct) =>
        ForAppointment(id, me => scheduling.MarkAttendanceAsync(id, request.Attended, me, ct), ct);

    [HttpPost("appointments/{id:int}/cancel")]
    public Task<ActionResult<AppointmentSummary>> Cancel(int id, CancelRequest request, CancellationToken ct) =>
        ForAppointment(id, me => scheduling.CancelAsync(id, new SchedulingActor(me), request.Reason, ct: ct), ct);

    [HttpGet("locations/{id:int}/availability")]
    public Task<ActionResult<LocationAvailability>> Availability(int id, CancellationToken ct) =>
        ForLocation(id, _ => scheduling.GetAvailabilityAsync(id, ct), ct);

    [HttpPut("locations/{id:int}/hours")]
    public Task<ActionResult<LocationAvailability>> Hours(int id, IReadOnlyList<HoursInput> hours, CancellationToken ct) =>
        ForLocation(id, _ => scheduling.ReplaceHoursAsync(id, hours, ct), ct);

    [HttpPost("locations/{id:int}/sessions")]
    public Task<ActionResult<LocationAvailability>> AddSession(int id, SessionInput session, CancellationToken ct) =>
        ForLocation(id, _ => scheduling.AddSessionAsync(id, session, ct), ct);

    [HttpDelete("locations/{id:int}/sessions/{sessionId:int}")]
    public Task<ActionResult<LocationAvailability>> RemoveSession(int id, int sessionId, CancellationToken ct) =>
        ForLocation(id, _ => scheduling.RemoveSessionAsync(id, sessionId, ct), ct);

    [HttpPost("locations/{id:int}/closures")]
    public Task<ActionResult<LocationAvailability>> AddClosure(int id, ClosureInput closure, CancellationToken ct) =>
        ForLocation(id, _ => scheduling.AddClosureAsync(id, closure, ct), ct);

    [HttpDelete("locations/{id:int}/closures/{closureId:int}")]
    public Task<ActionResult<LocationAvailability>> RemoveClosure(int id, int closureId, CancellationToken ct) =>
        ForLocation(id, _ => scheduling.RemoveClosureAsync(id, closureId, ct), ct);

    private async Task<ActionResult<T>> AsMember<T>(Func<StaffMember, Task<ActionResult<T>>> action, CancellationToken ct)
    {
        var me = await users.GetStaffMemberAsync(User, ct);
        return me is null ? Forbid() : await action(me);
    }

    /// <summary>Someone else's location is a 404, not a 403: its existence is none of their business.</summary>
    private Task<ActionResult<T>> ForLocation<T>(int locationId, Func<StaffMember, Task<T?>> action, CancellationToken ct) where T : class =>
        AsMember<T>(async me =>
            await scheduling.CanManageAsync(me, locationId, ct) && await action(me) is { } result ? Ok(result) : NotFound(), ct);

    private Task<ActionResult<T>> ForAppointment<T>(int appointmentId, Func<StaffMember, Task<T?>> action, CancellationToken ct) where T : class =>
        AsMember<T>(async me =>
            await scheduling.CanManageAppointmentAsync(me, appointmentId, ct) && await action(me) is { } result ? Ok(result) : NotFound(), ct);
}
