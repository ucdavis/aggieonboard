using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Scheduling;
using Server.Core.Seed;
using Server.Tests.Messaging;

namespace Server.Tests.Scheduling;

public class SchedulingServiceTests : IAsyncLifetime
{
    // Thursday 2026-10-22, 9 a.m. Pacific.
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 22);

    private readonly AppDbContext _db = TestDbContextFactory.CreateInMemory();
    private readonly RecordingEmailService _email = new();
    private SchedulingService _scheduling = null!;
    private StaffMember _sample = null!;
    private StaffMember _scheduler = null!;

    public async Task InitializeAsync()
    {
        await ReferenceDataSeeder.SeedAsync(_db, Now);
        await DevelopmentSeeder.SeedAsync(_db, Now);
        _scheduling = new SchedulingService(_db, new FixedClock(Now), TestMessaging.Service(_db, new FixedClock(Now), _email));
        _sample = await _db.Staff.SingleAsync(s => s.Email == DevelopmentSeeder.SandboxStaffEmail);
        _scheduler = await _db.Staff.SingleAsync(s => s.Email == DevelopmentSeeder.SandboxSchedulerEmail);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<Candidate> Candidate(string firstName) =>
        await JourneyData.CandidatesWithJourney(_db).SingleAsync(c => c.FirstName == firstName);

    private async Task<int> LocationId(string teamKey) =>
        await _db.Locations.Where(l => l.Team!.Key == teamKey).Select(l => l.Id).SingleAsync();

    private static DateTime At(DateOnly date, int hour, int minute = 0) => date.ToDateTime(new TimeOnly(hour, minute));

    private async Task<StepStatus> StepStatusOf(string firstName, string stepKey) =>
        (await Candidate(firstName)).Steps.Single(s => s.Step!.Key == stepKey).Status;

    [Fact]
    public async Task Options_respect_notice_and_the_clearance_deadline()
    {
        var casey = await Candidate("Casey");

        var options = (await _scheduling.GetOptionsAsync(casey.Id, "fingerprinting", Today, Today.AddDays(30)))!;

        options.Unavailable.Should().BeNull();
        options.Latest.Should().Be(new DateOnly(2026, 11, 3), "Casey starts 11/12; clearance is six business days earlier");
        options.Locations.Select(l => l.Name).Should().Equal(
            "UC Davis Campus Police Department, Davis", "UC Davis Health Police Department, Sacramento");
        var slots = options.Locations.SelectMany(l => l.Slots).ToList();
        slots.Should().NotBeEmpty();
        slots.Should().OnlyContain(s => s.Start >= At(Today, 9).AddHours(24) && DateOnly.FromDateTime(s.Start) <= options.Latest);
        options.Current.Should().NotBeNull("the sandbox already booked Casey at Health PD");
    }

    [Fact]
    public async Task A_window_anchored_to_the_start_date_needs_one()
    {
        var alex = await Candidate("Alex");

        var options = (await _scheduling.GetOptionsAsync(alex.Id, "i9-in-person", Today, Today.AddDays(14)))!;

        options.Unavailable.Should().Be("In-person I-9 verification can be booked once Alex has an anticipated start date.");
        options.Locations.Should().BeEmpty();
    }

    [Fact]
    public async Task New_employee_welcome_opens_on_the_start_date()
    {
        var taylor = await Candidate("Taylor");

        var options = (await _scheduling.GetOptionsAsync(taylor.Id, "new-employee-welcome", Today, Today.AddDays(60)))!;

        options.Earliest.Should().Be(At(taylor.AnticipatedStartDate!.Value, 0));
        options.Locations.Single().Slots.Should().OnlyContain(s => DateOnly.FromDateTime(s.Start) >= taylor.AnticipatedStartDate);
    }

    [Fact]
    public async Task Booking_moves_the_step_and_shows_on_the_location_roster()
    {
        var morgan = await Candidate("Morgan");
        morgan.RequiresFingerprinting = true;
        CandidateJourney.ApplyConditions(morgan);
        await _db.SaveChangesAsync();
        var monday = new DateOnly(2026, 10, 26);

        var booked = await _scheduling.BookAsync(morgan.Id, "fingerprinting", await LocationId("health-pd"), At(monday, 9, 15), new SchedulingActor(_sample));

        booked.Status.Should().Be(AppointmentStatus.Booked);
        booked.End.Should().Be(At(monday, 9, 30));
        (await StepStatusOf("Morgan", "fingerprinting")).Should().Be(StepStatus.Waiting, "booking was the candidate's part");

        var roster = await _scheduling.GetRosterAsync(_scheduler, monday, null);
        roster.Single(r => r.Name.StartsWith("UC Davis Health")).Appointments.Should().ContainSingle(a => a.CandidateName == "Morgan Chen");

        (await _db.AuditEvents.OrderByDescending(e => e.Id).FirstAsync(e => e.Action.StartsWith("appointment."))).Detail
            .Should().Be("Fingerprinting (Live Scan) booked for Monday, October 26 at 9:15 a.m. at UC Davis Health Police Department, Sacramento.");
    }

    [Fact]
    public async Task The_last_seat_goes_to_one_candidate_only()
    {
        var riley = await Candidate("Riley");
        var avery = await Candidate("Avery");
        foreach (var c in new[] { riley, avery })
        {
            c.RequiresFingerprinting = true;
            CandidateJourney.ApplyConditions(c);
        }
        riley.AnticipatedStartDate = avery.AnticipatedStartDate = new DateOnly(2026, 12, 7);
        await _db.SaveChangesAsync();
        var slot = At(new DateOnly(2026, 10, 27), 10);
        var location = await LocationId("health-pd");

        await _scheduling.BookAsync(riley.Id, "fingerprinting", location, slot, new SchedulingActor(_sample));
        var second = () => _scheduling.BookAsync(avery.Id, "fingerprinting", location, slot, new SchedulingActor(_sample));

        await second.Should().ThrowAsync<JourneyRuleException>().WithMessage("That time was just taken. Choose another.");
    }

    [Fact]
    public async Task Booking_again_moves_the_appointment()
    {
        var casey = await Candidate("Casey");
        var original = await _db.Appointments.SingleAsync(a => a.CandidateId == casey.Id && a.Status == AppointmentStatus.Booked);

        var moved = await _scheduling.BookAsync(casey.Id, "fingerprinting", await LocationId("campus-pd"), At(new DateOnly(2026, 10, 27), 13, 30), new SchedulingActor(_sample));

        (await _db.Appointments.SingleAsync(a => a.Id == original.Id)).Should().BeEquivalentTo(new { Status = AppointmentStatus.Cancelled, CancelReason = "Rescheduled" });
        moved.LocationName.Should().Be("UC Davis Campus Police Department, Davis");
        (await _db.AuditEvents.OrderByDescending(e => e.Id).FirstAsync(e => e.Action.StartsWith("appointment."))).Action.Should().Be("appointment.changed");
    }

    [Fact]
    public async Task A_time_outside_the_hours_cannot_be_booked()
    {
        var casey = await Candidate("Casey");

        var healthPd = await LocationId("health-pd");
        var act = () => _scheduling.BookAsync(casey.Id, "fingerprinting", healthPd, At(new DateOnly(2026, 10, 27), 9, 7), new SchedulingActor(_sample));

        await act.Should().ThrowAsync<JourneyRuleException>().WithMessage("That time isn't available. Choose another.");
    }

    [Fact]
    public async Task Booking_in_person_I9_settles_the_I9_method()
    {
        var casey = await Candidate("Casey");
        casey.Steps.Single(s => s.Step!.Key == "i9").Status = StepStatus.ActionNeeded;
        await _db.SaveChangesAsync();

        await _scheduling.BookAsync(casey.Id, "i9-in-person", await LocationId("i9-desk"), At(new DateOnly(2026, 10, 28), 8, 15), new SchedulingActor(_sample));

        (await Candidate("Casey")).I9Method.Should().Be(I9Method.InPerson);
        (await StepStatusOf("Casey", "i9-remote")).Should().Be(StepStatus.Skipped);
        (await StepStatusOf("Casey", "i9")).Should().Be(StepStatus.Waiting);
    }

    [Fact]
    public async Task A_candidate_cannot_change_within_the_cutoff_but_staff_can()
    {
        var morgan = await Candidate("Morgan");
        var appointment = await _db.Appointments.SingleAsync(a => a.CandidateId == morgan.Id && a.Status == AppointmentStatus.Booked);

        var asCandidate = () => _scheduling.CancelAsync(appointment.Id, SchedulingActor.Candidate, null, asCandidateId: morgan.Id);
        await asCandidate.Should().ThrowAsync<JourneyRuleException>().WithMessage("Appointments can be changed online until 24 hours before.*");

        var cancelled = (await _scheduling.CancelAsync(appointment.Id, new SchedulingActor(_sample), "Candidate called in sick"))!;
        cancelled.Status.Should().Be(AppointmentStatus.Cancelled);
        (await StepStatusOf("Morgan", "employee-health")).Should().Be(StepStatus.ActionNeeded, "the candidate has to rebook");
    }

    [Fact]
    public async Task A_candidate_can_cancel_only_their_own()
    {
        var morgan = await Candidate("Morgan");
        var appointment = await _db.Appointments.SingleAsync(a => a.CandidateId == morgan.Id && a.Status == AppointmentStatus.Booked);

        (await _scheduling.CancelAsync(appointment.Id, SchedulingActor.Candidate, null, asCandidateId: (await Candidate("Alex")).Id))
            .Should().BeNull();
    }

    [Fact]
    public async Task Attendance_completes_the_step_only_where_it_should()
    {
        var morgan = await Candidate("Morgan");
        var oehc = await _db.Appointments.SingleAsync(a => a.CandidateId == morgan.Id && a.Status == AppointmentStatus.Booked);

        await _scheduling.MarkAttendanceAsync(oehc.Id, attended: true, _sample);

        (await StepStatusOf("Morgan", "employee-health")).Should().Be(StepStatus.Waiting, "the OEHC clearance still follows");
        (await _db.Appointments.SingleAsync(a => a.Id == oehc.Id)).Status.Should().Be(AppointmentStatus.Attended);
    }

    [Fact]
    public async Task A_no_show_sends_the_step_back()
    {
        var morgan = await Candidate("Morgan");
        var oehc = await _db.Appointments.SingleAsync(a => a.CandidateId == morgan.Id && a.Status == AppointmentStatus.Booked);

        await _scheduling.MarkAttendanceAsync(oehc.Id, attended: false, _sample);

        (await StepStatusOf("Morgan", "employee-health")).Should().Be(StepStatus.ActionNeeded);
        (await _db.AuditEvents.OrderByDescending(e => e.Id).FirstAsync()).Action.Should().Be("appointment.no-show");
    }

    [Fact]
    public async Task Attendance_cannot_be_recorded_before_the_day()
    {
        var casey = await Candidate("Casey");
        var tomorrow = await _db.Appointments.SingleAsync(a => a.CandidateId == casey.Id && a.Status == AppointmentStatus.Booked);

        var act = () => _scheduling.MarkAttendanceAsync(tomorrow.Id, attended: true, _sample);

        await act.Should().ThrowAsync<JourneyRuleException>().WithMessage("Attendance can be recorded from the day of the appointment.");
    }

    [Fact]
    public async Task Team_members_see_only_their_teams_locations()
    {
        (await _scheduling.GetManagedLocationsAsync(_scheduler)).Select(l => l.TeamName)
            .Should().BeEquivalentTo("UC Davis Health Police Department", "UC Davis Campus Police Department");
        (await _scheduling.GetManagedLocationsAsync(_sample)).Should().HaveCount(5, "Sample User is an admin");

        var morganOehc = await _db.Appointments.Where(a => a.Location!.Team!.Key == "oehc").Select(a => a.Id).SingleAsync();
        (await _scheduling.CanManageAppointmentAsync(_scheduler, morganOehc)).Should().BeFalse();
    }

    [Fact]
    public async Task Hours_are_validated()
    {
        var location = await LocationId("campus-pd");

        var backwards = () => _scheduling.ReplaceHoursAsync(location, [new HoursInput(DayOfWeek.Monday, new(12, 0), new(9, 0), 15, 1)]);
        await backwards.Should().ThrowAsync<JourneyRuleException>().WithMessage("Monday hours end before they start.");

        var updated = (await _scheduling.ReplaceHoursAsync(location, [new HoursInput(DayOfWeek.Friday, new(9, 0), new(10, 0), 20, 2)]))!;
        updated.Hours.Should().Equal(new HoursInput(DayOfWeek.Friday, new(9, 0), new(10, 0), 20, 2));
    }

    [Fact]
    public async Task Closing_a_day_or_removing_a_session_with_bookings_is_refused()
    {
        var oehc = await LocationId("oehc");
        var bookedDay = DateOnly.FromDateTime((await _db.Appointments.SingleAsync(a => a.LocationId == oehc)).Start);
        var close = () => _scheduling.AddClosureAsync(oehc, new ClosureInput(bookedDay, "Staff training"));
        await close.Should().ThrowAsync<JourneyRuleException>().WithMessage("1 candidate is booked that day. Cancel or move them first.");

        var learning = await LocationId("learning");
        var session = (await _scheduling.GetAvailabilityAsync(learning))!.Sessions.First(s => s.Booked > 0);
        var remove = () => _scheduling.RemoveSessionAsync(learning, session.Id);
        await remove.Should().ThrowAsync<JourneyRuleException>().WithMessage("1 candidate is booked into this session.*");
    }

    [Fact]
    public async Task A_closure_takes_the_day_out_of_the_options()
    {
        var casey = await Candidate("Casey");
        var campus = await LocationId("campus-pd");
        var tuesday = new DateOnly(2026, 10, 27);
        await _scheduling.AddClosureAsync(campus, new ClosureInput(tuesday, "Range qualification"));

        var options = (await _scheduling.GetOptionsAsync(casey.Id, "fingerprinting", tuesday, tuesday))!;

        options.Locations.Single(l => l.LocationId == campus).Slots.Should().BeEmpty();
    }
}
