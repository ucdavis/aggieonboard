namespace Server.Core.Domain;

/// <summary>
/// One version of a message the journey sends — or, for <see cref="MessageSender.External"/>, the
/// wording communications proposed to a vendor. Versions are rows: an edit adds a version and never
/// rewrites one, so a sent message can always be traced to the exact text it came from. Steps name
/// the messages they use by <see cref="Key"/>; one message can serve several steps (message 12).
/// </summary>
public class MessageTemplate
{
    public int Id { get; set; }

    /// <summary>Stable identifier ("qualifications-received"). Unique together with <see cref="Version"/>.</summary>
    public string Key { get; set; } = "";

    public int Version { get; set; }

    /// <summary>The message number in the communications audit ("5", "11A").</summary>
    public string AuditReference { get; set; } = "";

    public MessageSender Sender { get; set; }

    /// <summary>Subject line, with merge fields.</summary>
    public string Subject { get; set; } = "";

    public List<MessageTemplateSection> Sections { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Who wrote this version: a staff email, or "seed" for the audit's wording.</summary>
    public string CreatedBy { get; set; } = "";
}

/// <summary>
/// A run of a message's body, in Markdown with <c>{{Field}}</c> merge fields. A section with a
/// condition is left out for candidates it does not apply to — message 12 is four such sections.
/// </summary>
public class MessageTemplateSection
{
    public int Id { get; set; }
    public int MessageTemplateId { get; set; }
    public int Order { get; set; }
    public CandidateCondition Condition { get; set; }
    public string Body { get; set; } = "";
}

public enum MessageSender
{
    /// <summary>AggieOnboard sends it.</summary>
    App,

    /// <summary>A vendor sends it. Kept as the proposed wording; the app never sends it.</summary>
    External,
}
