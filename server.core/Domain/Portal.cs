namespace Server.Core.Domain;

/// <summary>
/// A one-time sign-in link for the candidate portal. Only the SHA-256 hash is stored; the token
/// itself exists only in the email it was sent in (AGENTS.md, invariant 3).
/// </summary>
public class CandidateAccessToken
{
    public int Id { get; set; }
    public int CandidateId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the token. Unique.</summary>
    public string TokenHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the link was used to sign in. A used link never works again.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}
