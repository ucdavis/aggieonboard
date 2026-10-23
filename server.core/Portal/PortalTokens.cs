using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Portal;

public enum RedeemFailure
{
    /// <summary>No such link. Also what a malformed token gets.</summary>
    Unknown,
    Expired,
    AlreadyUsed,

    /// <summary>The candidate was archived after the link was sent.</summary>
    Unavailable,
}

public sealed record RedeemResult(int? CandidateId, RedeemFailure? Failure);

/// <summary>A token and its hash, between creating it and recording it.</summary>
public sealed record PendingToken(string Token, string Hash);

/// <summary>Issues and redeems one-time portal links.</summary>
public sealed class PortalTokens(AppDbContext db, TimeProvider clock, IOptions<PortalOptions> options)
{
    /// <summary>
    /// A new token, not yet recorded. <see cref="Record"/> it only once the email carrying it has been
    /// accepted, so a failed send leaves no working link behind.
    /// </summary>
    public static PendingToken Create()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new PendingToken(token, Hash(token));
    }

    public void Record(int candidateId, PendingToken pending)
    {
        var now = clock.GetUtcNow();
        db.CandidateAccessTokens.Add(new CandidateAccessToken
        {
            CandidateId = candidateId,
            TokenHash = pending.Hash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(options.Value.LinkLifetimeDays),
        });
    }

    /// <summary>Whether another link may go to this candidate now (<see cref="PortalOptions.LinksPerHour"/>).</summary>
    public async Task<bool> MayIssueAsync(int candidateId, CancellationToken ct = default)
    {
        var since = clock.GetUtcNow().AddHours(-1);
        var recent = await db.CandidateAccessTokens.CountAsync(t => t.CandidateId == candidateId && t.CreatedAt > since, ct);
        return recent < options.Value.LinksPerHour;
    }

    /// <summary>Uses the link: valid once, before it expires, for a candidate still in the pipeline.</summary>
    public async Task<RedeemResult> RedeemAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return new(null, RedeemFailure.Unknown);

        var hash = Hash(token.Trim());
        var stored = await db.CandidateAccessTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        var now = clock.GetUtcNow();
        if (stored is null) return new(null, RedeemFailure.Unknown);
        if (stored.ConsumedAt is not null) return new(null, RedeemFailure.AlreadyUsed);
        if (stored.ExpiresAt <= now) return new(null, RedeemFailure.Expired);

        var available = await db.Candidates.AnyAsync(c => c.Id == stored.CandidateId && c.ArchivedAt == null, ct);
        if (!available) return new(null, RedeemFailure.Unavailable);

        stored.ConsumedAt = now;
        await db.SaveChangesAsync(ct);
        return new(stored.CandidateId, null);
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
