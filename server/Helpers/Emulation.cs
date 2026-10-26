using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Server.Helpers;

/// <summary>
/// Admin → Emulate: an admin sees the app exactly as one staff member or candidate does. Emulation
/// is read-only, so nothing is ever recorded, booked or emailed in someone else's name: every
/// change under <c>/api</c> is refused while it lasts, except stopping it.
/// </summary>
public static class Emulation
{
    /// <summary>On the staff cookie: the staff member an admin is viewing as. Honoured only while the signer-in is an active admin.</summary>
    public const string StaffClaim = "aggieonboard_emulating_staff";

    /// <summary>On the candidate cookie: the admin viewing that checklist.</summary>
    public const string ViewedByClaim = "aggieonboard_viewed_by";

    public const string StopPath = "/api/user/emulation/stop";

    public static int? EmulatedStaffId(ClaimsPrincipal? principal) =>
        int.TryParse(principal?.FindFirst(StaffClaim)?.Value, out var id) ? id : null;

    /// <summary>Re-issues the current staff cookie with (or without) the emulated staff member.</summary>
    public static async Task SetStaffAsync(HttpContext context, int? staffId)
    {
        var result = await context.AuthenticateAsync();
        if (!result.Succeeded || result.Ticket is null) return;

        var principal = new ClaimsPrincipal(result.Principal!.Identities.Select(i => i.Clone()));
        var identity = (ClaimsIdentity)principal.Identity!;
        foreach (var claim in identity.FindAll(StaffClaim).ToList()) identity.RemoveClaim(claim);
        if (staffId is { } id) identity.AddClaim(new Claim(StaffClaim, id.ToString()));

        await context.SignInAsync(result.Ticket.AuthenticationScheme, principal, result.Properties);
    }

    /// <summary>Refuses any change while an admin is viewing as someone else.</summary>
    public static IApplicationBuilder UseReadOnlyEmulation(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var changes = !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method));
            if (!changes || !request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments(StopPath))
            {
                await next();
                return;
            }

            string? viewing = null; // why the change is refused
            if (request.Path.StartsWithSegments("/api/portal"))
            {
                // Signing out of the portal is how an emulated checklist is closed.
                if (!request.Path.StartsWithSegments("/api/portal/sign-out"))
                {
                    var candidate = await context.AuthenticateAsync(CandidateAuthentication.Scheme);
                    viewing = CandidateAuthentication.ViewedBy(candidate.Principal) is null
                        ? null
                        : "You're viewing this checklist as an admin, so changes are turned off.";
                }
            }
            else if (EmulatedStaffId(context.User) is not null)
            {
                viewing = "You're viewing the app as someone else, so changes are turned off. Stop emulating to make changes as yourself.";
            }

            if (viewing is null)
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Viewing only",
                Detail = viewing,
            });
        });
}
