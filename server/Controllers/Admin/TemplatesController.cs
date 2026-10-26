using Microsoft.AspNetCore.Mvc;
using Server.Core.Admin;
using Server.Core.Messaging;
using Server.Services;

namespace Server.Controllers.Admin;

public class TemplatesController(TemplateAdmin templates, IUserService users) : AdminControllerBase(users)
{
    [HttpGet("templates")]
    public async Task<IReadOnlyList<TemplateSummary>> List(CancellationToken ct) => await templates.ListAsync(ct);

    /// <summary>The latest version, or <c>?version=</c> an earlier one.</summary>
    [HttpGet("templates/{key}")]
    public async Task<ActionResult<TemplateDetail>> Get(string key, [FromQuery] int? version, CancellationToken ct) =>
        await templates.GetAsync(key, version, ct) is { } detail ? detail : NotFound();

    [HttpPost("templates/{key}/preview")]
    public Task<ActionResult<MessagePreview>> Preview(string key, TemplatePreviewRequest request, CancellationToken ct) =>
        AsAdmin<MessagePreview>(async me =>
            await templates.PreviewAsync(key, request, me, ct) is { } preview ? preview : NotFound(), ct);

    /// <summary>Saves the next version. 409 when someone saved one since <c>basedOnVersion</c>.</summary>
    [HttpPost("templates/{key}/versions")]
    public Task<ActionResult<TemplateDetail>> Save(string key, TemplateEdit edit, CancellationToken ct) =>
        AsAdmin<TemplateDetail>(async me =>
            await templates.SaveAsync(key, edit, me, ct) is { } detail ? detail : NotFound(), ct);
}
