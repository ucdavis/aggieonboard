using Microsoft.AspNetCore.Mvc;
using Server.Core.Admin;
using Server.Services;

namespace Server.Controllers.Admin;

public class SettingsController(SettingsAdmin settings, IUserService users) : AdminControllerBase(users)
{
    [HttpGet("settings")]
    public async Task<IReadOnlyList<SettingView>> Settings(CancellationToken ct) => await settings.ListSettingsAsync(ct);

    [HttpPut("settings/{key}")]
    public Task<ActionResult<AdminChangeResult>> UpdateSetting(string key, SettingEdit edit, CancellationToken ct) =>
        AsAdmin<AdminChangeResult>(async me =>
            await settings.UpdateSettingAsync(key, edit, me, ct) is { } result ? result : NotFound(), ct);

    [HttpGet("steps")]
    public async Task<IReadOnlyList<StepDeadlineView>> Steps(CancellationToken ct) => await settings.ListStepDeadlinesAsync(ct);

    [HttpPut("steps/{key}/deadline")]
    public Task<ActionResult<AdminChangeResult>> UpdateStepDeadline(string key, StepDeadlineEdit edit, CancellationToken ct) =>
        AsAdmin<AdminChangeResult>(async me =>
            await settings.UpdateStepDeadlineAsync(key, edit, me, ct) is { } result ? result : NotFound(), ct);

    [HttpGet("holidays")]
    public async Task<IReadOnlyList<HolidayView>> Holidays(CancellationToken ct) => await settings.ListHolidaysAsync(ct);

    [HttpPost("holidays")]
    public Task<ActionResult<AdminChangeResult>> AddHoliday(HolidayInput input, CancellationToken ct) =>
        AsAdmin<AdminChangeResult>(async me => await settings.AddHolidayAsync(input, me, ct), ct);

    [HttpDelete("holidays/{date}")]
    public Task<ActionResult<AdminChangeResult>> RemoveHoliday(DateOnly date, CancellationToken ct) =>
        AsAdmin<AdminChangeResult>(async me =>
            await settings.RemoveHolidayAsync(date, me, ct) is { } result ? result : NotFound(), ct);

    /// <summary>Who changed shared data (templates, settings, deadlines, holidays, staff), newest first.</summary>
    [HttpGet("history")]
    public async Task<IReadOnlyList<AdminHistoryEntry>> History(CancellationToken ct) => await settings.HistoryAsync(100, ct);
}
