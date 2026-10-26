using System.Text.RegularExpressions;
using Server.Core.Domain;

namespace Server.Core.Messaging;

/// <summary>
/// A message template as a Markdown file: front matter (key, audit, sender, subject, and optionally
/// version, 1 when absent), then the body. A run of the body between <c>&lt;!-- if: Condition --&gt;</c> and <c>&lt;!-- end --&gt;</c>
/// is a conditional section; everything else is always included. This is how the audit's wording
/// is kept readable in the repository and seeded verbatim.
/// </summary>
public sealed partial record MessageTemplateFile(
    string Key,
    string AuditReference,
    MessageSender Sender,
    string Subject,
    IReadOnlyList<MessageTemplateFile.Section> Sections,
    int Version = 1)
{
    public sealed record Section(CandidateCondition Condition, string Body);

    public static MessageTemplateFile Parse(string text, string name)
    {
        text = text.Replace("\r\n", "\n");
        var front = FrontMatter().Match(text);
        if (!front.Success) throw new FormatException($"{name}: missing front matter.");

        var fields = front.Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .ToDictionary(p => p[0].Trim(), p => p.Length > 1 ? p[1].Trim() : "");

        string Required(string field) =>
            fields.TryGetValue(field, out var value) && value.Length > 0
                ? value
                : throw new FormatException($"{name}: front matter is missing '{field}'.");

        if (!Enum.TryParse<MessageSender>(Required("sender"), out var sender))
        {
            throw new FormatException($"{name}: unknown sender '{fields["sender"]}'.");
        }

        var version = 1;
        if (fields.TryGetValue("version", out var v) && (!int.TryParse(v, out version) || version < 1))
        {
            throw new FormatException($"{name}: version must be a whole number from 1.");
        }

        return new MessageTemplateFile(
            Required("key"),
            Required("audit"),
            sender,
            Required("subject"),
            ParseSections(text[front.Length..], name),
            version);
    }

    private static List<Section> ParseSections(string body, string name)
    {
        var sections = new List<Section>();
        var condition = CandidateCondition.Always;
        var start = 0;

        void Add(int end)
        {
            var chunk = body[start..end].Trim('\n');
            if (chunk.Trim().Length > 0) sections.Add(new Section(condition, chunk + "\n"));
        }

        foreach (Match marker in Marker().Matches(body))
        {
            var opens = marker.Groups[1].Success;
            if (opens == (condition != CandidateCondition.Always))
            {
                throw new FormatException(opens
                    ? $"{name}: sections cannot nest ('{marker.Value}')."
                    : $"{name}: '<!-- end -->' without a matching '<!-- if: … -->'.");
            }

            Add(marker.Index);
            if (opens && (!Enum.TryParse(marker.Groups[1].Value, out condition) || condition == CandidateCondition.Always))
            {
                throw new FormatException($"{name}: unknown condition '{marker.Groups[1].Value}'.");
            }
            if (!opens) condition = CandidateCondition.Always;
            start = marker.Index + marker.Length;
        }

        if (condition != CandidateCondition.Always) throw new FormatException($"{name}: section is never closed.");
        Add(body.Length);
        return sections;
    }

    [GeneratedRegex(@"\A---\n(.*?)\n---\n", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();

    [GeneratedRegex(@"<!--\s*(?:if:\s*([A-Za-z]+)|end)\s*-->")]
    private static partial Regex Marker();
}
