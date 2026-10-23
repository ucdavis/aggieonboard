namespace Server.Core.Domain;

/// <summary>
/// One message as it went out: the exact subject, HTML and text, and the template version they
/// came from (AGENTS.md, invariant 7). Never edited or deleted.
/// </summary>
public class SentMessage
{
    public int Id { get; set; }
    public int CandidateId { get; set; }

    public int MessageTemplateId { get; set; }
    public MessageTemplate? Template { get; set; }

    /// <summary>Copied from the template so the log reads on its own.</summary>
    public string MessageKey { get; set; } = "";
    public string AuditReference { get; set; } = "";

    public string ToAddress { get; set; } = "";
    public string? ReplyTo { get; set; }
    public string Subject { get; set; } = "";
    public string Html { get; set; } = "";
    public string Text { get; set; } = "";

    public DateTimeOffset SentAt { get; set; }
    public int? SentByStaffId { get; set; }
    public string SentByName { get; set; } = "";

    /// <summary>Sent by the app as a consequence of a step move (message 5), not by pressing Send.</summary>
    public bool Automatic { get; set; }
}
