namespace Server.Core.Messaging;

/// <summary>
/// Messages the app needs that the communications audit does not cover. Their wording is new, so
/// it is listed under "Copy to review" in docs/PLAN.md. They belong to no step.
/// </summary>
public static class SystemMessages
{
    /// <summary>A sign-in link to the portal, sent by staff or at the candidate's request.</summary>
    public const string PortalLink = "portal-link";

    public static readonly IReadOnlySet<string> Keys = new HashSet<string> { PortalLink };
}
