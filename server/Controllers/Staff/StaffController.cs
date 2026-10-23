using Microsoft.AspNetCore.Mvc;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Messaging;
using Server.Core.Scheduling;
using Server.Services;

namespace Server.Controllers.Staff;

public sealed record StepActionRequest(string? Reason);
public sealed record StepDueRequest(DateOnly? Date, TimeOnly? Time);
public sealed record StepNoteRequest(string? Note);
public sealed record BookingRequest(string TypeKey, int LocationId, DateTime Start);
public sealed record CancelAppointmentRequest(string? Reason);

public class StaffController(JourneyService journey, MessageService messages, SchedulingService scheduling, IUserService users)
    : StaffControllerBase
{
    [HttpGet("members")]
    public async Task<IReadOnlyList<StaffSummary>> Members(CancellationToken ct) => await journey.GetStaffAsync(ct);

    /// <summary>The pipeline. <c>mine=true</c> narrows to candidates assigned to the signed-in staff member.</summary>
    [HttpGet("pipeline")]
    public async Task<ActionResult<IReadOnlyList<PipelineRow>>> Pipeline([FromQuery] bool mine, CancellationToken ct)
    {
        if (!mine) return Ok(await journey.GetPipelineAsync(null, ct));

        var me = await users.GetStaffMemberAsync(User, ct);
        return me is null ? Forbid() : Ok(await journey.GetPipelineAsync(me.Id, ct));
    }

    [HttpGet("candidates/{id:int}")]
    public async Task<ActionResult<CandidateDetail>> Candidate(int id, CancellationToken ct) =>
        await journey.GetCandidateAsync(id, ct) is { } detail ? detail : NotFound();

    [HttpPost("candidates")]
    public Task<ActionResult<CandidateDetail>> Create(CandidateInput input, CancellationToken ct) =>
        AsStaff(async me =>
        {
            var detail = await journey.CreateCandidateAsync(input, me, ct);
            return CreatedAtAction(nameof(Candidate), new { id = detail.Id }, detail);
        }, ct);

    [HttpPut("candidates/{id:int}")]
    public Task<ActionResult<CandidateDetail>> Update(int id, CandidateInput input, CancellationToken ct) =>
        AsStaff(async me => Found(await journey.UpdateCandidateAsync(id, input, me, ct)), ct);

    /// <summary>One move on one step: activate, markWaiting, complete, block, unblock or reopen.</summary>
    // Not {action}: MVC reserves that route value for the controller action's own name.
    [HttpPost("candidates/{id:int}/steps/{key}/actions/{move}")]
    public Task<ActionResult<CandidateDetail>> StepAction(
        int id, string key, string move, StepActionRequest? request, CancellationToken ct)
    {
        if (!Enum.TryParse<StepAction>(move, ignoreCase: true, out var stepAction) || !Enum.IsDefined(stepAction))
        {
            return Task.FromResult<ActionResult<CandidateDetail>>(NotFound());
        }
        return AsStaff(async me => Found(await journey.ApplyStepActionAsync(id, key, stepAction, request?.Reason, me, ct)), ct);
    }

    /// <summary>Set a due date by hand; send a null date to return the step to its rule.</summary>
    [HttpPut("candidates/{id:int}/steps/{key}/due")]
    public Task<ActionResult<CandidateDetail>> StepDue(int id, string key, StepDueRequest request, CancellationToken ct) =>
        AsStaff(async me => Found(await journey.SetStepDueAsync(id, key, request.Date, request.Time, me, ct)), ct);

    [HttpPut("candidates/{id:int}/steps/{key}/note")]
    public Task<ActionResult<CandidateDetail>> StepNote(int id, string key, StepNoteRequest request, CancellationToken ct) =>
        AsStaff(async me => Found(await journey.SetStepNoteAsync(id, key, request.Note, me, ct)), ct);

    /// <summary>The message filled in for this candidate, with anything that stops it being sent.</summary>
    [HttpGet("candidates/{id:int}/messages/{key}/preview")]
    public async Task<ActionResult<MessagePreview>> PreviewMessage(int id, string key, CancellationToken ct)
    {
        var me = await users.GetStaffMemberAsync(User, ct);
        if (me is null) return Forbid();
        return await messages.PreviewAsync(id, key, me, ct) is { } preview ? preview : NotFound();
    }

    /// <summary>Sends the message and starts the steps it asks for. 409 while anything is missing.</summary>
    [HttpPost("candidates/{id:int}/messages/{key}/send")]
    public Task<ActionResult<CandidateDetail>> SendMessage(int id, string key, CancellationToken ct) =>
        AsStaff(async me => await messages.SendAsync(id, key, me, ct) ? Found(await journey.GetCandidateAsync(id, ct)) : NotFound(), ct);

    /// <summary>Emails the candidate a link to their portal checklist.</summary>
    [HttpPost("candidates/{id:int}/portal-link")]
    public Task<ActionResult<CandidateDetail>> SendPortalLink(int id, CancellationToken ct) =>
        AsStaff(async me => await messages.SendAsync(id, SystemMessages.PortalLink, me, ct)
            ? Found(await journey.GetCandidateAsync(id, ct))
            : NotFound(), ct);

    /// <summary>The appointment types that apply to the candidate, each with its current and past bookings.</summary>
    [HttpGet("candidates/{id:int}/appointments")]
    public async Task<IReadOnlyList<CandidateAppointmentType>> Appointments(int id, CancellationToken ct) =>
        await scheduling.GetCandidateAppointmentsAsync(id, ct);

    /// <summary>Open times for one type within the candidate's window, between two dates.</summary>
    [HttpGet("candidates/{id:int}/appointments/{typeKey}/options")]
    public async Task<ActionResult<BookingOptions>> AppointmentOptions(
        int id, string typeKey, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        await scheduling.GetOptionsAsync(id, typeKey, from, to, ct) is { } options ? options : NotFound();

    /// <summary>Books on the candidate's behalf, replacing their current booking of that type.</summary>
    [HttpPost("candidates/{id:int}/appointments")]
    public async Task<ActionResult<AppointmentSummary>> Book(int id, BookingRequest request, CancellationToken ct)
    {
        var me = await users.GetStaffMemberAsync(User, ct);
        if (me is null) return Forbid();
        return await scheduling.BookAsync(id, request.TypeKey, request.LocationId, request.Start, new SchedulingActor(me), ct);
    }

    [HttpPost("candidates/{id:int}/appointments/{appointmentId:int}/cancel")]
    public async Task<ActionResult<AppointmentSummary>> CancelAppointment(
        int id, int appointmentId, CancelAppointmentRequest request, CancellationToken ct)
    {
        var me = await users.GetStaffMemberAsync(User, ct);
        if (me is null) return Forbid();
        // asCandidateId scopes the appointment to this candidate; staff are never held to the cutoff.
        return await scheduling.CancelAsync(appointmentId, new SchedulingActor(me), request.Reason, asCandidateId: id, ct)
            is { } cancelled ? cancelled : NotFound();
    }

    /// <summary>A message exactly as it went out.</summary>
    [HttpGet("candidates/{id:int}/sent/{sentId:int}")]
    public async Task<ActionResult<SentMessageView>> SentMessage(int id, int sentId, CancellationToken ct) =>
        await messages.GetSentMessageAsync(id, sentId, ct) is { } sent ? sent : NotFound();

    /// <summary>
    /// Changes are made as a named staff member, for the history. The Staff role is re-derived from
    /// the whitelist on every request, so a missing row here means it changed mid-request.
    /// </summary>
    private async Task<ActionResult<CandidateDetail>> AsStaff(
        Func<StaffMember, Task<ActionResult<CandidateDetail>>> change, CancellationToken ct)
    {
        var me = await users.GetStaffMemberAsync(User, ct);
        return me is null ? Forbid() : await change(me);
    }

    private ActionResult<CandidateDetail> Found(CandidateDetail? detail) => detail is null ? NotFound() : detail;
}
