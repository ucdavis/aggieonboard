namespace Server.Core.Portal;

/// <summary>Portal links and sessions. Bound from the "Portal" configuration section.</summary>
public sealed class PortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>
    /// The app's public address, for links in emails ("https://onboard.example.edu"). Falls back to
    /// Notification:BaseUrl. Without it, emails go out with no portal button and sign-in links cannot be sent.
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>How long an emailed link works if never used.</summary>
    public int LinkLifetimeDays { get; set; } = 14;

    /// <summary>How long a portal session lasts without a visit (sliding).</summary>
    public int SessionDays { get; set; } = 7;

    /// <summary>At most this many links are issued to one candidate per hour, however often they are requested.</summary>
    public int LinksPerHour { get; set; } = 3;

    public string? SignInUrl(string token) =>
        string.IsNullOrWhiteSpace(BaseUrl) ? null : $"{BaseUrl.TrimEnd('/')}/c/{token}";

    /// <summary>Where a logged copy's button points instead of the one-time link.</summary>
    public string? PortalUrl => string.IsNullOrWhiteSpace(BaseUrl) ? null : $"{BaseUrl.TrimEnd('/')}/portal";
}
