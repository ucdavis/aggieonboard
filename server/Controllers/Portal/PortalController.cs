using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Server.Controllers.Staff;
using Server.Core.Portal;
using Server.Core.Scheduling;
using Server.Helpers;

namespace Server.Controllers.Portal;

public sealed record SignInRequest(string? Token);
public sealed record LinkRequest(string? Email);
public sealed record PortalBookingRequest(string TypeKey, int LocationId, DateTime Start);
public sealed record RepresentativeRequest(string? Name, string? Email);
public sealed record AtiNumberRequest(string? AtiNumber);

/// <summary>
/// The candidate portal's API. Only the candidate cookie authenticates here (<see cref="CandidateAuthentication.Policy"/>),
/// and the candidate is always the one in that cookie — never an ID from the URL or body.
/// </summary>
[ApiController]
[Route("api/portal")]
[Authorize(Policy = CandidateAuthentication.Policy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[JourneyErrors]
[PortalActivity]
public class PortalController(PortalService portal, PortalTokens tokens, SchedulingService scheduling, PortalAnswers answers) : ControllerBase
{
    /// <summary>The smallest time a link request takes, so its timing says nothing about whether the address is on file.</summary>
    private static readonly TimeSpan LinkRequestFloor = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Redeems an emailed link. Called from the "Continue" page, never by the link itself: email
    /// scanners open links, and a one-time link opened by a scanner would be spent before the candidate clicks.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(Program.PortalRateLimit)]
    [HttpPost("sign-in")]
    public async Task<IActionResult> SignIn(SignInRequest request, CancellationToken ct)
    {
        var result = await tokens.RedeemAsync(request.Token, ct);
        if (result.CandidateId is not { } candidateId)
        {
            var (reason, detail) = result.Failure switch
            {
                RedeemFailure.Expired => ("expired", "This link has expired."),
                RedeemFailure.AlreadyUsed => ("used", "This link has already been used."),
                _ => ("invalid", "This link isn't valid."),
            };
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "We couldn't sign you in.",
                Detail = detail,
                Extensions = { ["reason"] = reason },
            });
        }

        await HttpContext.SignInAsync(
            CandidateAuthentication.Scheme,
            CandidateAuthentication.CreatePrincipal(candidateId),
            new AuthenticationProperties { IsPersistent = true });
        return NoContent();
    }

    /// <summary>"Send me a new link." The same answer whatever the address (AGENTS.md, invariant 3).</summary>
    [AllowAnonymous]
    [EnableRateLimiting(Program.PortalRateLimit)]
    [HttpPost("link")]
    public async Task<IActionResult> RequestLink(LinkRequest request, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        await portal.RequestLinkAsync(request.Email, ct);
        var remaining = LinkRequestFloor - (DateTimeOffset.UtcNow - started);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("sign-out")]
    public async Task<IActionResult> SignOutOfPortal()
    {
        await HttpContext.SignOutAsync(CandidateAuthentication.Scheme);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<PortalView>> Me(CancellationToken ct) =>
        await portal.GetViewAsync(CurrentCandidate, ct) is { } view
            ? view with { ViewedBy = CandidateAuthentication.ViewedBy(User) }
            : Unauthorized();

    [HttpGet("messages/{sentId:int}")]
    public async Task<ActionResult<PortalMessage>> Message(int sentId, CancellationToken ct) =>
        await portal.GetMessageAsync(CurrentCandidate, sentId, ct) is { } message ? message : NotFound();

    /// <summary>Open times for one of the candidate's appointments. Refused for a step they haven't been asked to do yet.</summary>
    [HttpGet("appointments/{typeKey}/options")]
    public async Task<ActionResult<BookingOptions>> AppointmentOptions(
        string typeKey, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        await scheduling.GetOptionsAsync(CurrentCandidate, typeKey, from, to, ct, forCandidate: true) is { } options
            ? options
            : NotFound();

    /// <summary>Books, or moves the current booking of that type. The confirmation goes by email; the checklist reloads.</summary>
    [HttpPost("appointments")]
    public async Task<IActionResult> Book(PortalBookingRequest request, CancellationToken ct)
    {
        await scheduling.BookAsync(CurrentCandidate, request.TypeKey, request.LocationId, request.Start, SchedulingActor.Candidate, ct);
        return NoContent();
    }

    /// <summary>Cancels the candidate's own booking, until the change cutoff. Anyone else's is a 404.</summary>
    [HttpPost("appointments/{appointmentId:int}/cancel")]
    public async Task<IActionResult> CancelAppointment(int appointmentId, CancellationToken ct) =>
        await scheduling.CancelAsync(appointmentId, SchedulingActor.Candidate, null, asCandidateId: CurrentCandidate, ct) is null
            ? NotFound()
            : NoContent();

    /// <summary>A task's instructions: the text of its email, filled in. 404 for a task that isn't open or done.</summary>
    [HttpGet("tasks/{key}")]
    public async Task<ActionResult<PortalTaskBody>> TaskBody(string key, CancellationToken ct) =>
        await portal.GetTaskBodyAsync(CurrentCandidate, key, ct) is { } body ? body : NotFound();

    /// <summary>"I've done this", for a task done in another system.</summary>
    [HttpPost("tasks/{key}/done")]
    public async Task<IActionResult> TaskDone(string key, CancellationToken ct)
    {
        await answers.MarkDoneAsync(CurrentCandidate, key, ct);
        return NoContent();
    }

    /// <summary>Message 11A's "Remote": starts the representative step and emails 11B. In person is booking.</summary>
    [HttpPost("i9/remote")]
    public async Task<IActionResult> ChooseRemoteI9(CancellationToken ct)
    {
        await answers.ChooseRemoteI9Async(CurrentCandidate, ct);
        return NoContent();
    }

    /// <summary>Message 11B's answer. Field errors come back as a 400 keyed "name" and "email".</summary>
    [HttpPut("i9/representative")]
    public async Task<IActionResult> SetRepresentative(RepresentativeRequest request, CancellationToken ct)
    {
        await answers.SetRepresentativeAsync(CurrentCandidate, request.Name, request.Email, ct);
        return NoContent();
    }

    /// <summary>Message 12's ATI number, after the fingerprinting appointment.</summary>
    [HttpPut("fingerprinting/ati")]
    public async Task<IActionResult> SetAtiNumber(AtiNumberRequest request, CancellationToken ct)
    {
        await answers.SetAtiNumberAsync(CurrentCandidate, request.AtiNumber, ct);
        return NoContent();
    }

    /// <summary>The policy guarantees the claim; this reads nothing else.</summary>
    private int CurrentCandidate => CandidateAuthentication.CandidateId(User)!.Value;
}

/// <summary>
/// Notes when the candidate last used their checklist (not when an admin views it), so a newly
/// opened task is emailed only once they've been away (NewTaskEmails). At most one write a minute.
/// </summary>
public sealed class PortalActivityAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        await next();

        var user = context.HttpContext.User;
        if (CandidateAuthentication.CandidateId(user) is not { } id || CandidateAuthentication.ViewedBy(user) is not null) return;

        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<Server.Core.Data.AppDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var recent = now.AddMinutes(-1);
        await db.Candidates
            .Where(c => c.Id == id && (c.LastPortalActivityAt == null || c.LastPortalActivityAt < recent))
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.LastPortalActivityAt, now));
    }
}
