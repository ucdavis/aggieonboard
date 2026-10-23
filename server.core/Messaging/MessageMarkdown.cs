using System.Text.RegularExpressions;
using Markdig;

namespace Server.Core.Messaging;

/// <summary>
/// The Markdown dialect messages are written in, and its two outputs. Raw HTML is disabled, so
/// neither a template nor a merged value can inject markup; single line breaks are kept, because
/// the audit's signatures ("Thank you,\n[Name]") depend on them.
/// </summary>
public static partial class MessageMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()
        .Build();

    public static string ToHtml(string markdown) => Markdown.ToHtml(markdown, Pipeline);

    /// <summary>
    /// The plain-text alternative: links as "text (url)", emphasis markers and escapes removed.
    /// Every message the app writes uses only paragraphs, bullets, bold and links.
    /// </summary>
    public static string ToText(string markdown)
    {
        var text = Link().Replace(markdown, m => $"{m.Groups[1].Value} ({m.Groups[2].Value})");
        text = text.Replace("**", "");
        return Escaped().Replace(text, "$1").Trim() + "\n";
    }

    /// <summary>Undoes <see cref="Escape"/>, for text that is not Markdown (a subject line).</summary>
    public static string Unescape(string value) => Escaped().Replace(value, "$1");

    /// <summary>A merged value as literal text: nothing in it can become Markdown syntax.</summary>
    public static string Escape(string value) => Special().Replace(value, @"\$0");

    /// <summary>
    /// A merged value used as a link target: absolute http(s) only, with the characters that would
    /// end a Markdown link destination percent-encoded. Anything else is not a usable link.
    /// </summary>
    public static string? SafeUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        return uri.AbsoluteUri.Replace("(", "%28").Replace(")", "%29").Replace(" ", "%20");
    }

    [GeneratedRegex(@"[\\`*_\[\]<>#|~]")]
    private static partial Regex Special();

    [GeneratedRegex(@"\\([\\`*_\[\]<>#|~!(){}+\-.])")]
    private static partial Regex Escaped();

    [GeneratedRegex(@"\[((?:\\.|[^\]\\])*)\]\(([^)\s]+)\)")]
    private static partial Regex Link();
}
