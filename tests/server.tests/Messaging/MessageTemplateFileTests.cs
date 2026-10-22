using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Messaging;

namespace Server.Tests.Messaging;

public class MessageTemplateFileTests
{
    private const string Front = "---\nkey: sample\naudit: 4\nsender: App\nsubject: Hello {{FirstName}}\n---\n\n";

    [Fact]
    public void Splits_the_body_into_always_and_conditional_sections()
    {
        var file = MessageTemplateFile.Parse(
            Front + "Intro\n\n<!-- if: HasDegrees -->\n**Education**\n\n{{DegreeList}}\n<!-- end -->\n\nClosing\n", "sample.md");

        file.Key.Should().Be("sample");
        file.AuditReference.Should().Be("4");
        file.Sender.Should().Be(MessageSender.App);
        file.Subject.Should().Be("Hello {{FirstName}}");
        file.Sections.Should().Equal(
            new MessageTemplateFile.Section(CandidateCondition.Always, "Intro\n"),
            new MessageTemplateFile.Section(CandidateCondition.HasDegrees, "**Education**\n\n{{DegreeList}}\n"),
            new MessageTemplateFile.Section(CandidateCondition.Always, "Closing\n"));
    }

    [Fact]
    public void Accepts_windows_line_endings()
    {
        var file = MessageTemplateFile.Parse((Front + "Body\n").Replace("\n", "\r\n"), "sample.md");

        file.Sections.Should().ContainSingle().Which.Body.Should().Be("Body\n");
    }

    [Theory]
    [InlineData("<!-- if: Nonsense -->\nx\n<!-- end -->", "unknown condition")]
    [InlineData("<!-- if: Always -->\nx\n<!-- end -->", "unknown condition")]
    [InlineData("<!-- if: Parking -->\n<!-- if: DrugScreen -->\nx\n<!-- end -->", "cannot nest")]
    [InlineData("x\n<!-- end -->", "without a matching")]
    [InlineData("<!-- if: Parking -->\nx", "never closed")]
    public void Rejects_malformed_sections(string body, string error)
    {
        var act = () => MessageTemplateFile.Parse(Front + body, "sample.md");

        act.Should().Throw<FormatException>().WithMessage($"*{error}*");
    }

    [Fact]
    public void Requires_front_matter_fields()
    {
        var act = () => MessageTemplateFile.Parse("---\nkey: sample\naudit: 4\nsender: App\n---\n\nBody", "sample.md");

        act.Should().Throw<FormatException>().WithMessage("*'subject'*");
    }

    [Fact]
    public void Lists_merge_fields_once_each_in_order_of_use()
    {
        MergeFields.In("{{FirstName}}, your {{PositionTitle}} start, {{ FirstName }}").Should().Equal("FirstName", "PositionTitle");
    }
}
