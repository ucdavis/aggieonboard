namespace Server.Core.Domain;

/// <summary>
/// A recruiting assistant, recruiter, onboarding specialist or L&amp;D contact. Candidates are
/// assigned to them, and messages are signed by them. Phase 2 makes this the sign-in whitelist.
/// </summary>
public class StaffMember
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public StaffRole Role { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>How messages offer this person as a contact: "Name at email (phone)".</summary>
    public string ContactLine => Phone is null ? $"{Name} at {Email}" : $"{Name} at {Email} ({Phone})";
}
