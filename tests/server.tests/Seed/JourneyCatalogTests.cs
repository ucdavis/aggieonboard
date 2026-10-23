using System.Text.RegularExpressions;
using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Messaging;
using Server.Core.Seed;

namespace Server.Tests.Seed;

/// <summary>
/// The seeded journey hangs together: every message a step names exists, every app message uses
/// only fields the app can fill, and none still carries the audit document's [placeholders].
/// </summary>
public class JourneyCatalogTests
{
    private static readonly IReadOnlyList<StepDefinition> Steps = JourneyCatalog.Steps();
    private static readonly IReadOnlyList<MessageTemplateFile> Messages = JourneyCatalog.Messages();

    private static IEnumerable<MessageTemplateFile> AppMessages => Messages.Where(m => m.Sender == MessageSender.App);

    [Fact]
    public void Embeds_every_message_from_the_audit()
    {
        Messages.Where(m => !SystemMessages.Keys.Contains(m.Key)).Select(m => m.AuditReference)
            .Should().Equal("1", "2", "4", "5", "7", "8", "9", "10", "11A", "11B", "12", "13");
        Messages.Select(m => m.Key).Should().Contain(SystemMessages.Keys);
        Messages.Select(m => m.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Steps_have_unique_keys_and_orders()
    {
        Steps.Select(s => s.Key).Should().OnlyHaveUniqueItems();
        Steps.Select(s => s.Order).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_message_a_step_names_exists_with_the_right_sender()
    {
        var byKey = Messages.ToDictionary(m => m.Key);
        foreach (var step in Steps)
        {
            foreach (var key in new[] { step.RequestMessageKey, step.AcknowledgmentMessageKey }.OfType<string>())
            {
                byKey.Should().ContainKey(key, $"step '{step.Key}' sends it");
                byKey[key].Sender.Should().Be(MessageSender.App, $"step '{step.Key}' sends '{key}' itself");
            }
            if (step.VendorMessageKey is { } vendor)
            {
                byKey.Should().ContainKey(vendor);
                byKey[vendor].Sender.Should().Be(MessageSender.External);
                step.WatchForSender.Should().NotBeNullOrEmpty($"the portal tells the candidate who '{vendor}' comes from");
            }
        }
    }

    [Fact]
    public void Every_audit_message_is_used_by_some_step()
    {
        var used = Steps.SelectMany(s => new[] { s.RequestMessageKey, s.AcknowledgmentMessageKey, s.VendorMessageKey }).OfType<string>().ToHashSet();

        Messages.Select(m => m.Key).Where(key => !SystemMessages.Keys.Contains(key)).Should().OnlyContain(key => used.Contains(key));
    }

    [Fact]
    public void App_messages_use_only_known_merge_fields()
    {
        foreach (var message in AppMessages)
        {
            var fields = MergeFields.In(message.Subject).Concat(message.Sections.SelectMany(s => MergeFields.In(s.Body)));
            fields.Should().OnlyContain(f => MergeFields.Names.Contains(f), $"'{message.Key}' must be fillable");
        }
    }

    [Fact]
    public void App_messages_keep_no_bracketed_placeholders_from_the_audit_document()
    {
        // A [bracket] not followed by (url) is a Markdown link only when it has one; anything else
        // is a placeholder the conversion missed.
        var leftover = new Regex(@"\[[^\]]+\](?!\()");
        foreach (var message in AppMessages)
        {
            var text = message.Subject + "\n" + string.Join("\n", message.Sections.Select(s => s.Body));
            leftover.Matches(text).Select(m => m.Value).Should().BeEmpty($"'{message.Key}' should use merge fields");
        }
    }

    [Fact]
    public void Vendor_messages_are_kept_in_the_audit_wording()
    {
        // Proposed wording for SkillSurvey, Universal and Quest. The app never fills or sends them.
        Messages.Where(m => m.Sender == MessageSender.External)
            .SelectMany(m => m.Sections)
            .Should().OnlyContain(s => MergeFields.In(s.Body).Count == 0);
    }

    [Fact]
    public void Message_12_has_a_section_per_additional_requirement()
    {
        var message = Messages.Single(m => m.Key == "additional-requirements");

        message.Sections.Select(s => s.Condition).Where(c => c != CandidateCondition.Always).Should().Equal(
            CandidateCondition.DmvPullNotice,
            CandidateCondition.Fingerprinting,
            CandidateCondition.PositionDescription,
            CandidateCondition.Parking);
        Steps.Where(s => s.RequestMessageKey == "additional-requirements").Select(s => s.Key)
            .Should().Equal("dmv", "fingerprinting", "position-description", "parking");
    }

    [Fact]
    public void Unconfirmed_deadlines_say_what_is_being_asked()
    {
        Steps.Where(s => !s.DeadlineConfirmed).Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.DeadlineQuestion));
    }

    [Fact]
    public void Seeds_every_setting_key_once()
    {
        JourneyCatalog.Settings().Select(s => s.Key).Should().BeEquivalentTo(SettingKeys.All);
        JourneyCatalog.Settings().Where(s => !s.IsConfirmed).Should().OnlyContain(s => s.OpenQuestion != null);
    }
}
