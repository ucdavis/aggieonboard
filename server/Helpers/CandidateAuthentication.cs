using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Portal;

namespace Server.Helpers;

/// <summary>
/// The candidate portal's own sign-in: a cookie issued by redeeming an emailed one-time link.
/// Separate from staff sign-in in every way (AGENTS.md, invariant 4). Its cookie is never the default
/// scheme, so staff endpoints ignore it; portal endpoints accept only it, through <see cref="Policy"/>.
/// </summary>
public static class CandidateAuthentication
{
    public const string Scheme = "Candidate";
    public const string Policy = "Candidate";

    /// <summary>The only place a portal request's candidate comes from.</summary>
    public const string CandidateIdClaim = "candidate_id";

    public static IServiceCollection AddCandidateAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var sessionDays = configuration.GetSection(PortalOptions.SectionName).GetValue<int?>(nameof(PortalOptions.SessionDays)) ?? 7;
        var cookieSuffix = configuration["Auth:LocalCookieSuffix"];

        services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.Cookie.Name = ".AggieOnboard.Candidate" + (string.IsNullOrEmpty(cookieSuffix) ? "" : $".{cookieSuffix}");
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(sessionDays);
            options.SlidingExpiration = true;

            // The portal is an API to this scheme: never redirect, least of all to staff sign-in.
            options.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };

            // A candidate archived after signing in loses the session at their next request.
            options.Events.OnValidatePrincipal = async ctx =>
            {
                var id = CandidateId(ctx.Principal);
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                if (id is null || !await db.Candidates.AnyAsync(c => c.Id == id && c.ArchivedAt == null))
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(Scheme);
                }
            };
        });

        services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(CandidateIdClaim));

        return services;
    }

    public static ClaimsPrincipal CreatePrincipal(int candidateId) =>
        new(new ClaimsIdentity([new Claim(CandidateIdClaim, candidateId.ToString())], Scheme));

    /// <summary>An admin viewing this candidate's checklist (Admin → Emulate). Read-only: see <see cref="Emulation"/>.</summary>
    public static ClaimsPrincipal CreateEmulatedPrincipal(int candidateId, string adminName) =>
        new(new ClaimsIdentity(
            [new Claim(CandidateIdClaim, candidateId.ToString()), new Claim(Emulation.ViewedByClaim, adminName)], Scheme));

    /// <summary>The admin viewing this checklist, or null when it's the candidate themself.</summary>
    public static string? ViewedBy(ClaimsPrincipal? principal) => principal?.FindFirst(Emulation.ViewedByClaim)?.Value;

    public static int? CandidateId(ClaimsPrincipal? principal) =>
        int.TryParse(principal?.FindFirst(CandidateIdClaim)?.Value, out var id) ? id : null;
}
