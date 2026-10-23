namespace Server.Core.Messaging;

/// <summary>The email layout's model for a candidate message (Views/Messages/CandidateMessage_mjml.cshtml).</summary>
public sealed class CandidateMessageModel
{
    public const string TemplatePath = "/Views/Messages/CandidateMessage_mjml.cshtml";

    /// <summary>The rendered Markdown body. Safe to emit raw: Markdown HTML is disabled and every value is escaped.</summary>
    public string BodyHtml { get; init; } = "";

    /// <summary>The inbox preview line.</summary>
    public string PreviewText { get; init; } = "";

    /// <summary>Every candidate message's button. Copy to review (docs/PLAN.md).</summary>
    public const string PortalButtonText = "View your onboarding checklist";

    /// <summary>The portal button. No button when null (no Portal:BaseUrl).</summary>
    public string? ButtonUrl { get; init; }
    public string? ButtonText { get; init; }
}
