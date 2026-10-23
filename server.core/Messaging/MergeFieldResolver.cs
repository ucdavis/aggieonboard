using System.Globalization;
using Server.Core.Domain;
using Server.Core.Scheduling;

namespace Server.Core.Messaging;

/// <summary>
/// A merge field's value as Markdown-safe text, or why there is none and how to fix that. The fix
/// is written for the staff member about to press Send.
/// </summary>
public sealed record ResolvedField(string? Value, string Fix);

/// <summary>Everything a message's fields can draw on, for one candidate at one moment.</summary>
public sealed record MergeContext(
    Candidate Candidate,
    StaffMember Sender,
    IReadOnlyDictionary<string, AppSetting> Settings,
    DeadlineCalculator Deadlines,
    IReadOnlyDictionary<string, Due?> DueByStep,
    DateTimeOffset Now,
    int PortalLinkLifetimeDays = 14);

/// <summary>Fills every field in <see cref="MergeFields.All"/>. A test holds the two lists together.</summary>
public static class MergeFieldResolver
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static IReadOnlyDictionary<string, ResolvedField> Resolve(MergeContext context)
    {
        var c = context.Candidate;
        const string details = "Add it under Edit details.";

        ResolvedField Text(string? value, string fix) =>
            new(string.IsNullOrWhiteSpace(value) ? null : MessageMarkdown.Escape(value.Trim()), fix);

        ResolvedField StepDue(string stepKey, string stepTitle) =>
            new(context.DueByStep.TryGetValue(stepKey, out var due) && due is { } d ? FormatDue(d) : null,
                $"Set a due date on “{stepTitle}”.");

        ResolvedField Setting(string key, bool url = false)
        {
            var setting = context.Settings.GetValueOrDefault(key);
            var fix = setting?.OpenQuestion is { } question
                ? $"The {key} setting has no value yet. Still open: {question}"
                : $"The {key} setting has no value yet.";
            if (url)
            {
                var safe = MessageMarkdown.SafeUrl(setting?.Value);
                return new(safe, setting?.Value is null ? fix : $"The {key} setting is not a usable http(s) link.");
            }
            return Text(setting?.Value, fix);
        }

        var clearance = context.Deadlines.ClearanceDeadline(c.AnticipatedStartDate);
        var licenses = c.Qualifications.Where(q => q.Kind != QualificationKind.Degree).ToList();
        var degrees = c.Qualifications.Where(q => q.Kind == QualificationKind.Degree).ToList();

        return new Dictionary<string, ResolvedField>
        {
            ["FirstName"] = Text(c.FirstName, details),
            ["PositionTitle"] = Text(c.PositionTitle, details),
            ["Department"] = Text(c.Department, details),
            ["StartDate"] = new(c.AnticipatedStartDate is { } start ? FormatDate(start) : null, "Add the anticipated start date under Edit details."),
            ["EmployeeId"] = Text(c.EmployeeId, "Add the employee ID under Edit details once the candidate is in UCPath."),
            ["HiringManagerContact"] = Text(c.HiringManagerContact, "Add the hiring manager contact under Edit details."),

            ["SenderName"] = Text(context.Sender.Name, "Your staff record has no name."),
            ["RecruitingAssistantContact"] = Text(c.RecruitingAssistant?.ContactDetails, "Assign a recruiting assistant under Edit details."),
            ["OnboardingSpecialistName"] = Text(c.OnboardingSpecialist?.Name, "Assign an onboarding specialist under Edit details."),
            ["OnboardingSpecialistContact"] = Text(c.OnboardingSpecialist?.ContactDetails, "Assign an onboarding specialist under Edit details."),

            ["ReferenceSurveyDeadline"] = ReferenceSurveyDeadline(context),
            ["QualificationsDeadline"] = StepDue("qualifications", "Qualification verification"),
            ["EmployeeDataDeadline"] = StepDue("employee-data", "Employee Data Gathering Form"),
            ["ClearanceDeadline"] = new(clearance is { } cd ? FormatDate(cd) : null, "Add the anticipated start date under Edit details."),
            ["DmvDeadline"] = StepDue("dmv", "DMV Employer Pull Notice"),
            ["PositionDescriptionDeadline"] = StepDue("position-description", "Position description"),

            ["LicenseList"] = new(Bullets(licenses), "Add the license or certification under Edit details."),
            ["DegreeList"] = new(Bullets(degrees), "Add the degree under Edit details."),
            ["QualificationKinds"] = new(QualificationKinds(c), "Add the qualifications being verified under Edit details."),
            ["RequirementList"] = new(RequirementList(c), "Turn on at least one additional requirement under Edit details."),

            ["QualificationSubmissionMethod"] = Setting(SettingKeys.QualificationSubmissionMethod),
            ["EmployeeDataFormUrl"] = Setting(SettingKeys.EmployeeDataFormUrl, url: true),
            ["EmployeeDataFormContact"] = Setting(SettingKeys.EmployeeDataFormContact),
            ["OehcFormUrl"] = Setting(SettingKeys.OehcFormUrl, url: true),
            ["FingerprintSchedulingUrl"] = Setting(SettingKeys.FingerprintSchedulingUrl, url: true),

            ["PortalLinkLifetime"] = new(context.PortalLinkLifetimeDays == 1 ? "1 day" : $"{context.PortalLinkLifetimeDays} days", ""),
        };
    }

    /// <summary>"Thursday, November 19, 2026": candidates read these, so nothing is abbreviated.</summary>
    public static string FormatDate(DateOnly date) => date.ToString("dddd, MMMM d, yyyy", English);

    /// <summary>"Monday, October 26, 2026, 4:54 p.m." for hour-based deadlines.</summary>
    public static string FormatDue(Due due) =>
        due.Time is { } time
            ? $"{FormatDate(due.Date)}, {time.ToString("h:mm", English)} {(time.Hour < 12 ? "a.m." : "p.m.")}"
            : FormatDate(due.Date);

    /// <summary>Counted from when the reference check started — or from now, when this message is what starts it.</summary>
    private static ResolvedField ReferenceSurveyDeadline(MergeContext context)
    {
        var setting = context.Settings.GetValueOrDefault(SettingKeys.ReferenceSurveyBusinessDays);
        var fix = $"The {SettingKeys.ReferenceSurveyBusinessDays} setting has no value yet."
            + (setting?.OpenQuestion is { } q ? $" Still open: {q}" : "");
        if (!int.TryParse(setting?.Value, out var days)) return new(null, fix);

        var references = context.Candidate.Steps.FirstOrDefault(s => s.Step?.Key == "references");
        var due = context.Deadlines.Compute(
            DeadlineRule.AfterActivation(days, DeadlineUnit.BusinessDays), references?.ActivatedAt ?? context.Now, null);
        return new(due is { } d ? FormatDue(d) : null, fix);
    }

    private static string? Bullets(IReadOnlyList<CandidateQualification> items) =>
        items.Count == 0
            ? null
            : string.Join("\n", items.Select(q =>
                $"- {MessageMarkdown.Escape(q.Name)}: {MessageMarkdown.Escape(q.DocumentToSubmit)}"));

    /// <summary>Message 5's "[license/certification/education]", in the candidate's terms.</summary>
    private static string? QualificationKinds(Candidate c)
    {
        var words = new[]
        {
            (QualificationKind.License, "license"),
            (QualificationKind.Certification, "certification"),
            (QualificationKind.Degree, "education"),
        }.Where(k => c.Qualifications.Any(q => q.Kind == k.Item1)).Select(k => k.Item2).ToList();
        return words.Count == 0 ? null : JoinWords(words);
    }

    /// <summary>Message 12's subject "[requirement]": the requirements whose sections it includes.</summary>
    private static string? RequirementList(Candidate c)
    {
        var words = new[]
        {
            (CandidateCondition.DmvPullNotice, "DMV Employer Pull Notice"),
            (CandidateCondition.Fingerprinting, "fingerprinting"),
            (CandidateCondition.PositionDescription, "position description"),
            (CandidateCondition.Parking, "complimentary parking"),
        }.Where(r => c.Meets(r.Item1)).Select(r => r.Item2).ToList();
        return words.Count == 0 ? null : JoinWords(words);
    }

    /// <summary>"a", "a and b", "a, b and c" — no serial comma, matching the audit's house style.</summary>
    private static string JoinWords(IReadOnlyList<string> words) =>
        words.Count == 1 ? words[0] : $"{string.Join(", ", words.Take(words.Count - 1))} and {words[^1]}";
}
