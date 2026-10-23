using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Server.Core.Portal;
using Server.Helpers;

namespace Server.Controllers.Portal;

public sealed record SignInRequest(string? Token);
public sealed record LinkRequest(string? Email);

/// <summary>
/// The candidate portal's API. Only the candidate cookie authenticates here (<see cref="CandidateAuthentication.Policy"/>),
/// and the candidate is always the one in that cookie — never an ID from the URL or body.
/// </summary>
[ApiController]
[Route("api/portal")]
[Authorize(Policy = CandidateAuthentication.Policy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PortalController(PortalService portal, PortalTokens tokens) : ControllerBase
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
    public async Task<IActionResult> SignOut()
    {
        await HttpContext.SignOutAsync(CandidateAuthentication.Scheme);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<PortalView>> Me(CancellationToken ct) =>
        await portal.GetViewAsync(CurrentCandidate, ct) is { } view ? view : Unauthorized();

    [HttpGet("messages/{sentId:int}")]
    public async Task<ActionResult<PortalMessage>> Message(int sentId, CancellationToken ct) =>
        await portal.GetMessageAsync(CurrentCandidate, sentId, ct) is { } message ? message : NotFound();

    /// <summary>The policy guarantees the claim; this reads nothing else.</summary>
    private int CurrentCandidate => CandidateAuthentication.CandidateId(User)!.Value;
}
