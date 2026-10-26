using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Helpers;

namespace Server.Controllers;

/// <summary>A candidate the sandbox can open the portal as, without an emailed link.</summary>
public sealed record SandboxCandidate(int Id, string Name, string PositionTitle);

public sealed record LocalLoginModel(string ReturnUrl, IReadOnlyList<SandboxCandidate> Candidates);

[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccountController(IConfiguration configuration, IHostEnvironment environment, AppDbContext db) : Controller
{
    [HttpGet("login")]
    public async Task<IActionResult> Login(string? returnUrl)
    {
        var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        if (LocalAuthentication.IsEnabled(configuration, environment))
        {
            var candidates = await db.Candidates
                .Where(c => c.ArchivedAt == null)
                .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
                .Select(c => new SandboxCandidate(c.Id, c.FirstName + " " + c.LastName, c.PositionTitle))
                .ToListAsync();
            return View("LocalLogin", new LocalLoginModel(safeReturnUrl, candidates));
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(safeReturnUrl);
        }

        return Challenge(new AuthenticationProperties { RedirectUri = safeReturnUrl },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost("login/local")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LocalLogin(string? persona, string? returnUrl)
    {
        if (!LocalAuthentication.IsEnabled(configuration, environment))
        {
            return NotFound();
        }

        var principal = LocalAuthentication.CreatePrincipal(persona);
        if (principal == null)
        {
            return BadRequest("Choose one of the listed sandbox users.");
        }

        await HttpContext.SignInAsync(LocalAuthentication.Scheme, principal);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    /// <summary>
    /// Sandbox only: opens the portal as one fictional candidate, so their checklist can be reviewed
    /// without an emailed link. It issues the same candidate cookie a redeemed link would, so the
    /// portal still sees exactly one candidate (AGENTS.md, invariant 2). Never available outside
    /// Development (<see cref="LocalAuthentication.IsEnabled"/> throws there).
    /// </summary>
    [HttpPost("login/candidate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CandidateLogin(int candidateId)
    {
        if (!LocalAuthentication.IsEnabled(configuration, environment))
        {
            return NotFound();
        }
        if (!await db.Candidates.AnyAsync(c => c.Id == candidateId && c.ArchivedAt == null))
        {
            return BadRequest("Choose one of the listed candidates.");
        }

        await HttpContext.SignInAsync(
            CandidateAuthentication.Scheme,
            CandidateAuthentication.CreatePrincipal(candidateId),
            new AuthenticationProperties { IsPersistent = true });
        return LocalRedirect("/portal");
    }

    [HttpPost("logout/local")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LocalLogout()
    {
        if (!LocalAuthentication.IsEnabled(configuration, environment))
        {
            return NotFound();
        }

        await HttpContext.SignOutAsync(LocalAuthentication.Scheme);
        return LocalRedirect("/login");
    }
}
