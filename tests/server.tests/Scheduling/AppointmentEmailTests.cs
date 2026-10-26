using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Messaging;
using Server.Core.Notification;
using Server.Core.Portal;
using Server.Core.Scheduling;
using Server.Core.Seed;
using Server.Tests.Messaging;

namespace Server.Tests.Scheduling;

/// <summary>Candidate booking (5b): who may book, the emails and invitations, reminders and the portal's view.</summary>
public class AppointmentEmailTests : IAsyncLifetime
{
    // Thursday 2026-10-22, 9 a.m. Pacific (PDT until November 1).
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.CreateInMemory();
    private readonly RecordingEmailService _email = new();
    private StaffMember _sample = null!;

    public async Task InitializeAsync()
    {
        await ReferenceDataSeeder.SeedAsync(_db, Now);
        await DevelopmentSeeder.SeedAsync(_db, Now);
        _sample = await _db.Staff.SingleAsync(s => s.Email == DevelopmentSeeder.SandboxStaffEmail);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private SchedulingService Scheduling(DateTimeOffset? at = null) =>
        new(_db, new FixedClock(at ?? Now), TestMessaging.Service(_db, new FixedClock(at ?? Now), _email));

    private AppointmentReminders Reminders(DateTimeOffset at) =>
        new(_db, new FixedClock(at), TestMessaging.Service(_db, new FixedClock(at), _email), Options.Create(new SchedulingOptions()));

    private async Task<Candidate> Candidate(string firstName) =>
        await JourneyData.CandidatesWithJourney(_db).SingleAsync(c => c.FirstName == firstName);

    private async Task<int> LocationId(string teamKey) =>
        await _db.Locations.Where(l => l.Team!.Key == teamKey).Select(l => l.Id).SingleAsync();

    private async Task<Appointment> BookedFor(string firstName) =>
        await _db.Appointments.SingleAsync(a => a.Candidate!.FirstName == firstName && a.Status == AppointmentStatus.Booked);

    private static DateTime At(DateOnly date, int hour, int minute = 0) => date.ToDateTime(new TimeOnly(hour, minute));

    private static string Ics(EmailMessage message) => Encoding.UTF8.GetString(message.Attachments.Single().Content);

    /// <summary>Morgan, asked to book fingerprinting (as message 12 would).</summary>
    private async Task<Candidate> MorganAskedForFingerprinting()
    {
        var morgan = await Candidate("Morgan");
        morgan.RequiresFingerprinting = true;
        CandidateJourney.ApplyConditions(morgan);
        morgan.Steps.Single(s => s.Step!.Key == "fingerprinting").Status = StepStatus.ActionNeeded;
        await _db.SaveChangesAsync();
        return morgan;
    }

    [Fact]
    public async Task A_candidate_books_only_a_step_they_have_been_asked_to_do()
    {
        var morgan = await Candidate("Morgan");
        morgan.RequiresFingerprinting = true;
        CandidateJourney.ApplyConditions(morgan);
        await _db.SaveChangesAsync();
        var monday = At(new DateOnly(2026, 10, 26), 9, 15);

        var options = (await Scheduling().GetOptionsAsync(morgan.Id, "fingerprinting", new(2026, 10, 22), new(2026, 11, 5), forCandidate: true))!;
        options.Unavailable.Should().Be("You can book Fingerprinting (Live Scan) once we ask you to. We'll email you when it's time.");
        var early = () => Scheduling().BookAsync(morgan.Id, "fingerprinting", 0, monday, SchedulingActor.Candidate);
        await early.Should().ThrowAsync<JourneyRuleException>();

        await MorganAskedForFingerprinting();
        var booked = await Scheduling().BookAsync(morgan.Id, "fingerprinting", await LocationId("health-pd"), monday, SchedulingActor.Candidate);

        booked.BookedBy.Should().Be("Candidate");
        (await Candidate("Morgan")).Steps.Single(s => s.Step!.Key == "fingerprinting").Status.Should().Be(StepStatus.Waiting);
        (await Scheduling().GetRosterAsync(_sample, DateOnly.FromDateTime(monday), await LocationId("health-pd")))
            .Single().Appointments.Should().ContainSingle(a => a.CandidateName == "Morgan Chen", "it's on the police department's roster");
    }

    [Fact]
    public async Task A_remote_I9_candidate_is_not_offered_the_in_person_desk()
    {
        var casey = await Candidate("Casey"); // remote I-9, step 8 waiting

        var options = (await Scheduling().GetOptionsAsync(casey.Id, "i9-in-person", new(2026, 10, 22), new(2026, 11, 5), forCandidate: true))!;

        options.Unavailable.Should().Be("You chose remote verification. To verify in person instead, contact your onboarding specialist.");
        (await Scheduling().GetPortalBookingsAsync(casey.Id)).Should().NotContainKey("i9");
    }

    [Fact]
    public async Task Booking_emails_a_confirmation_with_a_calendar_invitation()
    {
        var morgan = await MorganAskedForFingerprinting();

        var booked = await Scheduling().BookAsync(morgan.Id, "fingerprinting", await LocationId("health-pd"), At(new DateOnly(2026, 10, 26), 9, 15), SchedulingActor.Candidate);

        booked.Notice.Should().Be($"“Booked: Fingerprinting (Live Scan)” was emailed to {morgan.Email}.");
        var email = _email.Sent.Single();
        email.Subject.Should().Be("Booked: Fingerprinting (Live Scan)");
        email.ReplyTo.Should().Be(DevelopmentSeeder.SandboxStaffEmail, "Sample User is Morgan's onboarding specialist");
        email.TextBody.Should()
            .Contain("When: Monday, October 26, 2026, 9:15–9:30 a.m.")
            .And.Contain("Where: UC Davis Health Police Department, Sacramento")
            .And.Contain("Until Sunday, October 25, 2026, 9:15 a.m., you can change or cancel")
            .And.NotContain("..");

        var attachment = email.Attachments.Single();
        attachment.FileName.Should().Be("invite.ics");
        attachment.ContentType.Should().Be("text/calendar; method=REQUEST; charset=utf-8");
        var ics = Ics(email);
        ics.Should().Contain("METHOD:REQUEST\r\n")
            .And.Contain("DTSTART:20261026T161500Z\r\n", "9:15 a.m. PDT")
            .And.Contain("DTEND:20261026T163000Z\r\n")
            .And.Contain($"UID:{morgan.Id}-fingerprinting@aggieonboard\r\n")
            .And.Contain($"SEQUENCE:{booked.Id * 2}\r\n")
            .And.Contain("STATUS:CONFIRMED")
            .And.NotContain("/c/", "the invitation never carries a sign-in link");

        var logged = await _db.SentMessages.SingleAsync(m => m.CandidateId == morgan.Id && m.MessageKey == SystemMessages.AppointmentBooked);
        logged.SentByName.Should().Be("Candidate");
        (logged.Html + logged.Text).Should().NotContain("/c/").And.Contain("/portal");
    }

    [Fact]
    public async Task Moving_an_appointment_updates_the_same_calendar_event()
    {
        var casey = await Candidate("Casey");
        var original = await BookedFor("Casey");

        var moved = await Scheduling().BookAsync(casey.Id, "fingerprinting", await LocationId("campus-pd"), At(new DateOnly(2026, 10, 27), 13, 30), new SchedulingActor(_sample));

        var email = _email.Sent.Single();
        email.Subject.Should().Be("Changed: Fingerprinting (Live Scan)");
        email.ReplyTo.Should().Be("pat.kim@example.test", "Casey's onboarding specialist answers, whoever moved it");
        email.TextBody.Should().Contain("When: Tuesday, October 27, 2026, 1:30–2:00 p.m.")
            .And.Contain("Was: Friday, October 23, 2026, 9:00 a.m.");
        moved.Id.Should().BeGreaterThan(original.Id);
        Ics(email).Should().Contain($"UID:{casey.Id}-fingerprinting@aggieonboard\r\n").And.Contain($"SEQUENCE:{moved.Id * 2}\r\n");
    }

    [Fact]
    public async Task Cancelling_emails_a_cancellation_that_removes_the_event()
    {
        var avery = await Candidate("Avery");
        var welcome = await BookedFor("Avery"); // New Employee Welcome next Tuesday: before the cutoff

        await Scheduling().CancelAsync(welcome.Id, SchedulingActor.Candidate, null, asCandidateId: avery.Id);

        var email = _email.Sent.Single();
        email.Subject.Should().Be("Cancelled: New Employee Welcome");
        email.TextBody.Should().Contain("Book a new time from your onboarding checklist.");
        email.Attachments.Single().ContentType.Should().Be("text/calendar; method=CANCEL; charset=utf-8");
        Ics(email).Should().Contain("METHOD:CANCEL").And.Contain("STATUS:CANCELLED").And.Contain($"SEQUENCE:{welcome.Id * 2 + 1}\r\n");
        (await Candidate("Avery")).Steps.Single(s => s.Step!.Key == "new-employee-welcome").Status.Should().Be(StepStatus.ActionNeeded);
    }

    [Fact]
    public async Task A_failed_email_never_undoes_the_booking()
    {
        var morgan = await MorganAskedForFingerprinting();
        _email.Fail = true;

        var booked = await Scheduling().BookAsync(morgan.Id, "fingerprinting", await LocationId("health-pd"), At(new DateOnly(2026, 10, 26), 9, 15), new SchedulingActor(_sample));

        booked.Status.Should().Be(AppointmentStatus.Booked);
        booked.Notice.Should().StartWith("The “Booked: Fingerprinting (Live Scan)” email was not sent: The email server did not accept the message.");
        (await _db.Appointments.CountAsync(a => a.CandidateId == morgan.Id && a.Status == AppointmentStatus.Booked)).Should().Be(2);
        (await _db.AuditEvents.OrderByDescending(e => e.Id).FirstAsync()).Action.Should().Be("message.not-sent");
    }

    [Fact]
    public async Task Appointment_emails_cannot_be_sent_by_hand()
    {
        var preview = (await TestMessaging.Service(_db, new FixedClock(Now), _email).PreviewAsync((await Candidate("Casey")).Id, SystemMessages.AppointmentBooked, _sample))!;

        preview.CanSend.Should().BeFalse();
        preview.Blockers.Should().Contain(b => b.StartsWith("Appointment emails are sent by the schedule"));
    }

    [Fact]
    public async Task Reminders_go_the_day_before_once()
    {
        // Booked just now for tomorrow: the confirmation is reminder enough.
        var morgan = await MorganAskedForFingerprinting();
        await Scheduling().BookAsync(morgan.Id, "fingerprinting", await LocationId("health-pd"), At(new DateOnly(2026, 10, 23), 9, 15), new SchedulingActor(_sample));
        _email.Sent.Clear();

        (await Reminders(Now.AddHours(-1)).SendDueAsync()).Should().Be(0, "reminders start at 9 a.m.");
        (await Reminders(Now).SendDueAsync()).Should().Be(1);
        (await Reminders(Now.AddHours(2)).SendDueAsync()).Should().Be(0, "each appointment gets one");

        var reminder = _email.Sent.Single();
        reminder.Recipients.To.Should().Equal((await Candidate("Casey")).Email);
        reminder.Subject.Should().Be("Tomorrow: Fingerprinting (Live Scan)");
        reminder.TextBody.Should().Contain("Friday, October 23, 2026, 9:00–9:15 a.m.");
        reminder.Attachments.Should().BeEmpty();
        (await BookedFor("Casey")).ReminderSentAt.Should().Be(Now);
    }

    [Fact]
    public async Task The_portal_shows_each_booking_and_whether_it_can_still_change()
    {
        var scheduling = Scheduling();

        var casey = await scheduling.GetPortalBookingsAsync((await Candidate("Casey")).Id);
        casey["fingerprinting"].Should().BeEquivalentTo(new { CanBook = false, Appointment = new { CanChange = false, Status = AppointmentStatus.Booked } },
            "Casey's appointment is 24 hours away");

        var avery = await scheduling.GetPortalBookingsAsync((await Candidate("Avery")).Id);
        avery["new-employee-welcome"].Should().BeEquivalentTo(new { CanBook = true, Appointment = new { CanChange = true } });

        var riley = await scheduling.GetPortalBookingsAsync((await Candidate("Riley")).Id);
        riley["i9"].Should().BeEquivalentTo(new { CanBook = false, Appointment = new { Status = AppointmentStatus.Attended } });

        var alex = await scheduling.GetPortalBookingsAsync((await Candidate("Alex")).Id);
        alex.Should().BeEmpty("nothing is booked and nothing has been asked for yet");

        typeof(PortalAppointment).GetProperties().Select(p => p.Name).Should().NotContain(["BookedBy", "CancelReason"]);
    }
}
