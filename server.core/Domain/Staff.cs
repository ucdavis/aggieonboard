namespace Server.Core.Domain;

/// <summary>
/// A recruiting assistant, recruiter, onboarding specialist or L&amp;D contact. Candidates are
/// assigned to them, and messages are signed by them. This table is also the sign-in whitelist: a
/// UC Davis account whose email matches an active row gets staff roles; any other account gets none.
/// </summary>
public class StaffMember
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public StaffRole Role { get; set; }

    /// <summary>Inactive staff keep their history but cannot sign in or be assigned.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>May edit templates, settings and the staff list (Phase 6). Independent of <see cref="Role"/>.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>How messages offer this person as a contact: "Name at email (phone)".</summary>
    public string ContactLine => Phone is null ? $"{Name} at {Email}" : $"{Name} at {Email} ({Phone})";
}

/// <summary>
/// Role names, as constants so they can be used in [Authorize(Roles = ...)]. A staff member also
/// carries their <see cref="StaffRole"/> name ("OnboardingSpecialist") as a role.
/// </summary>
public static class AppRoles
{
    /// <summary>Every active staff member: the pipeline and candidate records.</summary>
    public const string Staff = "Staff";

    /// <summary>Templates, settings and the staff list.</summary>
    public const string Admin = "Admin";
}
