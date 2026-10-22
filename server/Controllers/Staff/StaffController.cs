using Microsoft.AspNetCore.Mvc;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Services;

namespace Server.Controllers.Staff;

public sealed record StepActionRequest(string? Reason);
public sealed record StepDueRequest(DateOnly? Date, TimeOnly? Time);
public sealed record StepNoteRequest(string? Note);

public class StaffController(JourneyService journey, IUserService users) : StaffControllerBase
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
