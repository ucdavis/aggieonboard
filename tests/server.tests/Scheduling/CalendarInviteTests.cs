using System.Text;
using FluentAssertions;
using Server.Core.Scheduling;

namespace Server.Tests.Scheduling;

public class CalendarInviteTests
{
    private static CalendarEvent Event(DateTime start, string location = "Rancho Cordova, 10850 White Rock Road") => new(
        "7-i9-in-person@aggieonboard", 14, start, start.AddMinutes(15), "UC Davis Health: In-person I-9 verification",
        location, "Change or cancel from your onboarding checklist: https://onboard.example.test/portal",
        "UC Davis Health", "onboarding@example.test", "Casey Brooks", "casey@example.test");

    [Fact]
    public void Pacific_times_are_written_in_UTC_on_either_side_of_daylight_saving()
    {
        var now = new DateTimeOffset(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

        CalendarInvite.Create(Event(new DateTime(2026, 10, 28, 9, 15, 0)), now, cancel: false)
            .Should().Contain("DTSTART:20261028T161500Z\r\n").And.Contain("DTSTAMP:20261022T160000Z\r\n");
        CalendarInvite.Create(Event(new DateTime(2026, 11, 4, 9, 15, 0)), now, cancel: false)
            .Should().Contain("DTSTART:20261104T171500Z\r\n", "standard time from November 1");
    }

    [Fact]
    public void Text_is_escaped_and_long_lines_are_folded()
    {
        var ics = CalendarInvite.Create(Event(new DateTime(2026, 10, 28, 9, 15, 0), "Room 1; Building B, Sacramento"), DateTimeOffset.UnixEpoch, cancel: false);

        ics.Should().Contain(@"LOCATION:Room 1\; Building B\, Sacramento");
        ics.Should().EndWith("END:VCALENDAR\r\n");
        foreach (var line in ics.Split("\r\n"))
        {
            Encoding.UTF8.GetByteCount(line).Should().BeLessThanOrEqualTo(75);
        }
        ics.Replace("\r\n ", "").Should().Contain("DESCRIPTION:Change or cancel from your onboarding checklist: https://onboard.example.test/portal\r\n");
    }

    [Fact]
    public void A_cancellation_cancels_the_same_event()
    {
        var ics = CalendarInvite.Create(Event(new DateTime(2026, 10, 28, 9, 15, 0)), DateTimeOffset.UnixEpoch, cancel: true);

        ics.Should().Contain("METHOD:CANCEL\r\n").And.Contain("STATUS:CANCELLED\r\n").And.Contain("UID:7-i9-in-person@aggieonboard\r\n");
        CalendarInvite.ContentType(cancel: true).Should().Be("text/calendar; method=CANCEL; charset=utf-8");
    }
}
