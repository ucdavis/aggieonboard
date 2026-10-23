using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Messaging;
using Server.Core.Scheduling;

namespace Server.Tests.Messaging;

public class MessageMarkdownTests
{
    [Fact]
    public void Keeps_single_line_breaks_for_signatures()
    {
        MessageMarkdown.ToHtml("Thank you,\nDana Ortiz\nTalent Acquisition\n")
            .Should().Contain("Thank you,<br />\nDana Ortiz<br />\nTalent Acquisition");
    }

    [Fact]
    public void Renders_no_raw_html_even_from_a_template()
    {
        MessageMarkdown.ToHtml("Hi <script>alert(1)</script>\n").Should().NotContain("<script>");
    }

    [Fact]
    public void An_escaped_value_stays_literal_text()
    {
        var value = MessageMarkdown.Escape("Surgery *Main* [Annex] <b>");

        var html = MessageMarkdown.ToHtml($"Department: {value}\n");

        html.Should().Contain("Surgery *Main* [Annex] &lt;b&gt;").And.NotContain("<em>");
        MessageMarkdown.Unescape(value).Should().Be("Surgery *Main* [Annex] <b>");
    }

    [Fact]
    public void Plain_text_keeps_link_targets_and_drops_markers()
    {
        var text = MessageMarkdown.ToText("Submit the **[OEHC form](https://example.test/oehc) immediately**:\n- a\\_b\n");

        text.Should().Be("Submit the OEHC form (https://example.test/oehc) immediately:\n- a_b\n");
    }

    [Theory]
    [InlineData("https://example.test/form", "https://example.test/form")]
    [InlineData(" https://example.test/a (b) ", "https://example.test/a%20%28b%29")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("example.test/form", null)]
    [InlineData(null, null)]
    public void Only_absolute_http_links_are_usable(string? value, string? expected)
    {
        MessageMarkdown.SafeUrl(value).Should().Be(expected);
    }

    [Fact]
    public void Every_merge_field_has_a_resolver()
    {
        var candidate = new Candidate { FirstName = "Test" };
        var fields = MergeFieldResolver.Resolve(new MergeContext(
            candidate, new StaffMember { Name = "Dana Ortiz" }, new Dictionary<string, AppSetting>(),
            new DeadlineCalculator(new BusinessCalendar([]), 6), new Dictionary<string, Due?>(), DateTimeOffset.UnixEpoch));

        fields.Keys.Should().BeEquivalentTo(MergeFields.Names);
    }

    [Theory]
    [InlineData(2026, 11, 19, null, null, "Thursday, November 19, 2026")]
    [InlineData(2026, 10, 26, 16, 54, "Monday, October 26, 2026, 4:54 p.m.")]
    [InlineData(2026, 10, 26, 9, 5, "Monday, October 26, 2026, 9:05 a.m.")]
    public void Dates_are_written_out_for_candidates(int y, int m, int d, int? hour, int? minute, string expected)
    {
        var due = new Due(new DateOnly(y, m, d), hour is { } h ? new TimeOnly(h, minute!.Value) : null);

        MergeFieldResolver.FormatDue(due).Should().Be(expected);
    }
}
