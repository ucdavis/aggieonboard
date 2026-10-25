using System.Globalization;
using System.Text;

namespace Server.Core.Scheduling;

/// <summary>What one calendar invitation describes. Start and end are Pacific wall-clock times.</summary>
public sealed record CalendarEvent(
    string Uid,
    int Sequence,
    DateTime Start,
    DateTime End,
    string Summary,
    string Location,
    string Description,
    string OrganizerName,
    string OrganizerEmail,
    string AttendeeName,
    string AttendeeEmail);

/// <summary>
/// An iCalendar (RFC 5545) invitation for an appointment email. A booking or change is a REQUEST and
/// a cancellation a CANCEL of the same UID, so calendar apps move or remove the event they already
/// have instead of adding another. A higher SEQUENCE is what tells them which version is newer.
/// </summary>
public static class CalendarInvite
{
    public const string FileName = "invite.ics";

    public static string ContentType(bool cancel) => $"text/calendar; method={(cancel ? "CANCEL" : "REQUEST")}; charset=utf-8";

    public static string Create(CalendarEvent e, DateTimeOffset now, bool cancel)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//UC Davis Health//AggieOnboard//EN",
            "CALSCALE:GREGORIAN",
            $"METHOD:{(cancel ? "CANCEL" : "REQUEST")}",
            "BEGIN:VEVENT",
            $"UID:{Escape(e.Uid)}",
            $"SEQUENCE:{e.Sequence}",
            $"DTSTAMP:{Utc(now)}",
            $"DTSTART:{Utc(e.Start)}",
            $"DTEND:{Utc(e.End)}",
            $"SUMMARY:{Escape(e.Summary)}",
            $"LOCATION:{Escape(e.Location)}",
            $"DESCRIPTION:{Escape(e.Description)}",
            $"ORGANIZER;CN={Parameter(e.OrganizerName)}:mailto:{e.OrganizerEmail}",
            $"ATTENDEE;CN={Parameter(e.AttendeeName)};ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED;RSVP=FALSE:mailto:{e.AttendeeEmail}",
            $"STATUS:{(cancel ? "CANCELLED" : "CONFIRMED")}",
            "TRANSP:OPAQUE",
            "END:VEVENT",
            "END:VCALENDAR",
        };
        var ics = new StringBuilder();
        foreach (var line in lines) Fold(ics, line);
        return ics.ToString();
    }

    /// <summary>A Pacific wall-clock time as UTC, so no VTIMEZONE block is needed.</summary>
    private static string Utc(DateTime pacific) =>
        Utc(new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(pacific, DateTimeKind.Unspecified), BusinessCalendar.Pacific)));

    private static string Utc(DateTimeOffset at) => at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string text) => text
        .Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
        .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

    /// <summary>A parameter value is quoted, and may not itself contain a quote or a line break.</summary>
    private static string Parameter(string text) =>
        $"\"{text.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ")}\"";

    /// <summary>Lines longer than 75 octets continue on the next line after a space (RFC 5545 §3.1).</summary>
    private static void Fold(StringBuilder ics, string line)
    {
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > 75)
            {
                ics.Append("\r\n ");
                octets = 1;
            }
            ics.Append(rune.ToString());
            octets += size;
        }
        ics.Append("\r\n");
    }
}
