using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Services;

public interface IUserService
{
    Task<ClaimsPrincipal?> UpdateUserPrincipalIfNeeded(ClaimsPrincipal principal);

    /// <summary>The active staff member this sign-in belongs to, or null for anyone not on the whitelist.</summary>
    Task<StaffMember?> GetStaffMemberAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}

public class UserService : IUserService
{
    private readonly ILogger<UserService> _logger;
    private readonly AppDbContext _dbContext;

    public UserService(ILogger<UserService> logger, AppDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <summary>
    /// The sign-in email: Entra's <c>preferred_username</c> (the UC Davis login), falling back to
    /// an email claim. The sandbox personas carry <c>preferred_username</c> too.
    /// </summary>
    public static string? EmailOf(ClaimsPrincipal principal) =>
        principal.FindFirst("preferred_username")?.Value
        ?? principal.FindFirst(ClaimTypes.Email)?.Value
        ?? principal.FindFirst("email")?.Value;

    public async Task<StaffMember?> GetStaffMemberAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var email = EmailOf(principal)?.Trim();
        if (string.IsNullOrEmpty(email)) return null;

        // Emails are compared case-insensitively: Entra may report "NDLewis@ucdavis.edu".
        var normalized = email.ToLowerInvariant();
        return await _dbContext.Staff.SingleOrDefaultAsync(s => s.IsActive && s.Email.ToLower() == normalized, ct);
    }

    /// <summary>
    /// Staff roles come from the whitelist on every request, so deactivating someone takes effect
    /// at once. Anyone not on it gets no roles: signed in, but unable to use the staff app.
    /// </summary>
    private async Task<List<string>> GetRolesForUser(ClaimsPrincipal principal)
    {
        var staff = await GetStaffMemberAsync(principal);
        if (staff is null) return [];

        // A team member signs in to the schedule only; everyone else also gets the pipeline.
        var roles = new List<string> { staff.Role.ToString() };
        if (staff.Role != StaffRole.TeamMember) roles.Add(AppRoles.Staff);
        if (staff.IsAdmin || await _dbContext.Set<TeamMembership>().AnyAsync(m => m.StaffMemberId == staff.Id))
        {
            roles.Add(AppRoles.Scheduling);
        }
        if (staff.IsAdmin) roles.Add(AppRoles.Admin);
        return roles;
    }

    public async Task<ClaimsPrincipal?> UpdateUserPrincipalIfNeeded(ClaimsPrincipal principal)
    {
        // Application roles are authoritative for both sign-in and cookie validation.
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return null; // can't update without user ID
        }

        // get user's roles
        // might want to cache w/ IMemoryCache to avoid DB hits on every request, but we'll skip that for simplicity
        var currentRoles = await GetRolesForUser(principal);

        // compare roles to existing claims, only update if different
        var existingRoles = principal.Identities
            .SelectMany(identity => identity.FindAll(identity.RoleClaimType))
            .Select(claim => claim.Value)
            .ToList();
        var changed = currentRoles.Count != existingRoles.Count ||
                      currentRoles.Except(existingRoles).Any();

        if (!changed) { return null; } // no change

        // Clone each identity to preserve claim mappings and metadata without changing the input.
        var updatedPrincipal = new ClaimsPrincipal(principal.Identities.Select(identity => identity.Clone()));

        foreach (var identity in updatedPrincipal.Identities)
        {
            foreach (var roleClaim in identity.FindAll(identity.RoleClaimType).ToList())
            {
                identity.RemoveClaim(roleClaim);
            }
        }

        var primaryIdentity = (ClaimsIdentity)updatedPrincipal.Identity!;
        foreach (var role in currentRoles)
        {
            primaryIdentity.AddClaim(new Claim(primaryIdentity.RoleClaimType, role));
        }

        return updatedPrincipal;
    }
}
