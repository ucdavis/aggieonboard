namespace Server.Core.Notification;

public sealed class EmailMessage
{
    public EmailRecipients Recipients { get; init; } = new();

    public string Subject { get; init; } = string.Empty;

    public string TextBody { get; init; } = string.Empty;

    public string HtmlBody { get; init; } = string.Empty;

    /// <summary>Where replies go for this message. Replaces the configured Smtp:ReplyToEmail when set.</summary>
    public string? ReplyTo { get; init; }

    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

/// <summary>A file sent with the message. <paramref name="ContentType"/> may carry parameters ("text/calendar; method=REQUEST").</summary>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);
