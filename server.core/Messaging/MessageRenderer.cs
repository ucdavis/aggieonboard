using System.Text.RegularExpressions;
using Server.Core.Domain;

namespace Server.Core.Messaging;

/// <summary>A field a message needs that has no value yet, and what to do about it.</summary>
public sealed record MissingField(string Name, string Description, string Fix);

/// <summary>A message filled in for one candidate. <see cref="BodyHtml"/> is the body only, before the email layout.</summary>
public sealed record RenderedMessage(
    string Subject,
    string Markdown,
    string BodyHtml,
    string Text,
    IReadOnlyList<MissingField> Missing);

/// <summary>
/// Keeps the sections whose conditions the candidate meets, then fills the merge fields. A field
/// with no value is shown as "[missing: …]" — the preview makes the gap visible exactly where it
/// falls — and listed in <see cref="RenderedMessage.Missing"/>, which blocks sending.
/// </summary>
public static partial class MessageRenderer
{
    public static RenderedMessage Render(
        MessageTemplate template, Candidate candidate, IReadOnlyDictionary<string, ResolvedField> fields)
    {
        var missing = new Dictionary<string, MissingField>();
        var descriptions = MergeFields.All.ToDictionary(f => f.Name, f => f.Description);

        string Fill(string text) => Placeholder().Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (fields.TryGetValue(name, out var field) && field.Value is { } value) return value;

            var description = descriptions.GetValueOrDefault(name, name);
            missing.TryAdd(name, new MissingField(name, description, field?.Fix ?? $"{name} is not a field the app can fill."));
            return MessageMarkdown.Escape($"[missing: {description.TrimEnd('.')}]");
        });

        var markdown = string.Join("\n", template.Sections
            .OrderBy(s => s.Order)
            .Where(s => candidate.Meets(s.Condition))
            .Select(s => Fill(s.Body).TrimEnd('\n') + "\n"));
        var subject = MessageMarkdown.Unescape(Fill(template.Subject));

        return new RenderedMessage(subject, markdown, MessageMarkdown.ToHtml(markdown), MessageMarkdown.ToText(markdown), missing.Values.ToList());
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9]*)\s*\}\}")]
    private static partial Regex Placeholder();
}
