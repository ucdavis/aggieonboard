using System.Text.RegularExpressions;

namespace Server.Core.Journey;

/// <summary>
/// The answers messages 11B and 12 used to ask for by reply: the remote I-9 representative and the
/// fingerprinting ATI number. Staff enter them from an emailed reply and candidates in the portal;
/// both go through these checks, and errors are keyed by the field that carries the value.
/// </summary>
public static partial class CandidateAnswers
{
    public const int AtiNumberMax = 30;

    /// <summary>
    /// Adds an error for a representative that isn't a name and an email address. Both blank is no
    /// representative, unless <paramref name="required"/>.
    /// </summary>
    public static void ValidateRepresentative(
        string? name, string? email, Action<string, string> error, string nameField, string emailField, bool required = false)
    {
        var hasName = !string.IsNullOrWhiteSpace(name);
        var hasEmail = !string.IsNullOrWhiteSpace(email);
        if (!hasName && (hasEmail || required)) error(nameField, "Enter the representative's full name.");
        if (hasName && name!.Trim().Length > 200) error(nameField, "The name must be 200 characters or fewer.");
        if (!hasEmail && (hasName || required)) error(emailField, "Enter the representative's email address.");
        if (hasEmail && (email!.Trim().Length > 320 || !EmailPattern().IsMatch(email.Trim())))
        {
            error(emailField, "Enter an email address like name@example.com.");
        }
    }

    /// <summary>
    /// The ATI as stored: spaces removed, upper case. Step 11: its exact format isn't confirmed, so only
    /// letters, digits and hyphens are required.
    /// </summary>
    public static string? NormalizeAtiNumber(string? value)
    {
        var compact = value?.Replace(" ", "").Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(compact) ? null : compact;
    }

    public static void ValidateAtiNumber(string? normalized, Action<string, string> error, string field)
    {
        if (normalized is null) return;
        if (normalized.Length > AtiNumberMax || !AtiPattern().IsMatch(normalized))
        {
            error(field, $"Enter the ATI number using only letters, numbers and hyphens, up to {AtiNumberMax}.");
        }
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^[A-Z0-9-]+$")]
    private static partial Regex AtiPattern();
}
