using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Portal;
using Server.Core.Scheduling;
using Server.Core.Seed;
using Server.Tests.Messaging;

namespace Server.Tests.Portal;

/// <summary>The answers 11A, 11B and 12 used to ask for by reply, given in the portal (5c).</summary>
public class PortalAnswersTests : IAsyncLifetime
{
    // Thursday 2026-10-22, 9 a.m. Pacific. Casey's fingerprinting is the next morning at 9.
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterCaseysFingerprinting = new(2026, 10, 23, 16, 30, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.CreateInMemory();
    private readonly RecordingEmailService _email = new();

    public async Task InitializeAsync()
    {
        await ReferenceDataSeeder.SeedAsync(_db, Now);
        await DevelopmentSeeder.SeedAsync(_db, Now);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private PortalAnswers Answers(DateTimeOffset? at = null) =>
        new(_db, new FixedClock(at ?? Now), TestMessaging.Service(_db, new FixedClock(at ?? Now), _email));

    private PortalService Portal(DateTimeOffset? at = null)
    {
        var clock = new FixedClock(at ?? Now);
        var messages = TestMessaging.Service(_db, clock, _email);
        return new PortalService(_db, clock, new PortalTokens(_db, clock, TestMessaging.Portal()), messages,
            new SchedulingService(_db, clock, messages), new PortalAnswers(_db, clock, messages));
    }

    private async Task<Candidate> Candidate(string firstName) =>
        await JourneyData.CandidatesWithJourney(_db).SingleAsync(c => c.FirstName == firstName);

    private static CandidateStep Step(Candidate candidate, string key) => candidate.Steps.Single(s => s.Step!.Key == key);

    private async Task<PortalStep> PortalStep(string firstName, string key, DateTimeOffset? at = null) =>
        (await Portal(at).GetViewAsync((await Candidate(firstName)).Id))!.Steps.Single(s => s.Key == key);

    /// <summary>Morgan, sent message 11A.</summary>
    private async Task<Candidate> MorganAskedAboutTheI9()
    {
        var morgan = await Candidate("Morgan");
        Step(morgan, "i9").Status = StepStatus.ActionNeeded;
        await _db.SaveChangesAsync();
        return morgan;
    }

    [Fact]
    public async Task Choosing_remote_starts_the_representative_step_and_emails_11B()
    {
        var morgan = await MorganAskedAboutTheI9();
        (await PortalStep("Morgan", "i9")).I9Choice.Should().Be(new PortalI9Choice(I9Method.NotChosen, CanChoose: true));

        await Answers().ChooseRemoteI9Async(morgan.Id);

        morgan = await Candidate("Morgan");
        morgan.I9Method.Should().Be(I9Method.Remote);
        Step(morgan, "i9").Status.Should().Be(StepStatus.Waiting);
        Step(morgan, "i9-remote").Status.Should().Be(StepStatus.ActionNeeded);
        Step(morgan, "i9-remote").DueDate.Should().NotBeNull("its clearance deadline starts with it");

        var sent = _email.Sent.Single();
        sent.Subject.Should().Be("Next step: Remote I-9 verification");
        sent.ReplyTo.Should().Be(DevelopmentSeeder.SandboxStaffEmail, "it is signed by Morgan's specialist");
        sent.TextBody.Should().Contain("In your onboarding checklist, enter the **full name**".Replace("**", ""));
        var logged = await _db.SentMessages.Include(m => m.Template).SingleAsync(m => m.CandidateId == morgan.Id);
        logged.Template!.Version.Should().Be(2);
        logged.SentByName.Should().Be("Candidate");

        (await _db.AuditEvents.Where(e => e.CandidateId == morgan.Id && e.ActorName == "Candidate").Select(e => e.Action).ToListAsync())
            .Should().Contain(["answer.i9-method", "step.waiting", "step.activated", "message.sent"]);

        var i9 = await PortalStep("Morgan", "i9");
        i9.I9Choice.Should().Be(new PortalI9Choice(I9Method.Remote, CanChoose: false));
        i9.Booking.Should().BeNull("a remote candidate isn't offered the in-person desk");
        (await PortalStep("Morgan", "i9-remote")).Representative.Should().Be(new PortalRepresentative(null, null));
    }

    [Fact]
    public async Task The_choice_waits_for_message_11A()
    {
        var morgan = await Candidate("Morgan");
        Step(morgan, "i9").Status = StepStatus.Upcoming;
        await _db.SaveChangesAsync();

        (await Portal().GetViewAsync(morgan.Id))!.Steps.Should().NotContain(s => s.Key == "i9", "it hasn't opened");
        var choose = () => Answers().ChooseRemoteI9Async(morgan.Id);
        await choose.Should().ThrowAsync<JourneyRuleException>().WithMessage("You can choose how to verify your documents once we ask you to*");
        _email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task An_in_person_booking_is_cancelled_before_choosing_remote()
    {
        var morgan = await MorganAskedAboutTheI9();
        var desk = await _db.Locations.Where(l => l.Team!.Key == "i9-desk").Select(l => l.Id).SingleAsync();
        var messages = TestMessaging.Service(_db, new FixedClock(Now), _email);
        await new SchedulingService(_db, new FixedClock(Now), messages)
            .BookAsync(morgan.Id, "i9-in-person", desk, new DateTime(2026, 10, 28, 9, 0, 0), SchedulingActor.Candidate);

        (await PortalStep("Morgan", "i9")).I9Choice.Should().Be(new PortalI9Choice(I9Method.InPerson, CanChoose: false));
        var choose = () => Answers().ChooseRemoteI9Async(morgan.Id);
        await choose.Should().ThrowAsync<JourneyRuleException>().WithMessage("Cancel your in-person appointment first*");

        var booked = await _db.Appointments.SingleAsync(a =>
            a.CandidateId == morgan.Id && a.AppointmentType!.StepKey == "i9" && a.Status == AppointmentStatus.Booked);
        await new SchedulingService(_db, new FixedClock(Now), messages).CancelAsync(booked.Id, SchedulingActor.Candidate, null, morgan.Id);

        (await PortalStep("Morgan", "i9")).I9Choice!.CanChoose.Should().BeTrue();
        await Answers().ChooseRemoteI9Async(morgan.Id);
        (await Candidate("Morgan")).I9Method.Should().Be(I9Method.Remote);
    }

    [Fact]
    public async Task The_representative_is_checked_saved_and_can_be_corrected()
    {
        var casey = await Candidate("Casey");

        var blank = () => Answers().SetRepresentativeAsync(casey.Id, " ", "not-an-email");
        (await blank.Should().ThrowAsync<JourneyValidationException>()).Which.Errors.Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["name"] = ["Enter the representative's full name."],
            ["email"] = ["Enter an email address like name@example.com."],
        });

        await Answers().SetRepresentativeAsync(casey.Id, "  Jesse Brooks ", "jesse@example.test ");

        casey = await Candidate("Casey");
        (casey.I9RepresentativeName, casey.I9RepresentativeEmail).Should().Be(("Jesse Brooks", "jesse@example.test"));
        Step(casey, "i9-remote").Status.Should().Be(StepStatus.Waiting);
        (await PortalStep("Casey", "i9-remote")).Representative.Should().Be(new PortalRepresentative("Jesse Brooks", "jesse@example.test"));

        await Answers().SetRepresentativeAsync(casey.Id, "Jo Park", "jo@example.test");
        (await _db.AuditEvents.Where(e => e.CandidateId == casey.Id && e.Action == "answer.i9-representative").Select(e => e.Detail).ToListAsync())
            .Should().Equal(
                "Entered their I-9 representative in the portal: Jesse Brooks (jesse@example.test).",
                "Changed their I-9 representative in the portal from Jesse Brooks (jesse@example.test) to Jo Park (jo@example.test).");
    }

    [Fact]
    public async Task Only_a_remote_candidate_names_a_representative()
    {
        var morgan = await MorganAskedAboutTheI9();

        (await Portal().GetViewAsync(morgan.Id))!.Steps.Should().NotContain(s => s.Key == "i9-remote", "it doesn't apply to Morgan");
        var set = () => Answers().SetRepresentativeAsync(morgan.Id, "Jesse Brooks", "jesse@example.test");
        await set.Should().ThrowAsync<JourneyRuleException>();
    }

    [Fact]
    public async Task The_ATI_number_is_asked_for_once_the_fingerprinting_appointment_has_started()
    {
        var casey = await Candidate("Casey");

        (await PortalStep("Casey", "fingerprinting")).AtiNumber.Should().BeNull("the appointment is tomorrow");
        var early = () => Answers().SetAtiNumberAsync(casey.Id, "A1B2C3D4E5");
        await early.Should().ThrowAsync<JourneyRuleException>().WithMessage("You can enter your ATI number after your fingerprinting appointment.");

        (await PortalStep("Casey", "fingerprinting", AfterCaseysFingerprinting)).AtiNumber.Should().Be(new PortalAtiNumber(null));
        var invalid = () => Answers(AfterCaseysFingerprinting).SetAtiNumberAsync(casey.Id, "A1#2");
        (await invalid.Should().ThrowAsync<JourneyValidationException>()).Which.Errors.Should().ContainKey("atiNumber");

        await Answers(AfterCaseysFingerprinting).SetAtiNumberAsync(casey.Id, " a1b2 c3d4e5 ");

        casey = await Candidate("Casey");
        casey.FingerprintAtiNumber.Should().Be("A1B2C3D4E5");
        Step(casey, "fingerprinting").Status.Should().Be(StepStatus.Waiting, "clearance follows; staff complete the step");
        (await PortalStep("Casey", "fingerprinting", AfterCaseysFingerprinting)).AtiNumber.Should().Be(new PortalAtiNumber("A1B2C3D4E5"));
    }

    [Fact]
    public async Task Staff_enter_the_same_answers_from_an_emailed_reply_with_the_same_checks()
    {
        var casey = await JourneyData.CandidatesWithJourney(_db).SingleAsync(c => c.FirstName == "Casey");
        var sample = await _db.Staff.SingleAsync(s => s.Email == DevelopmentSeeder.SandboxStaffEmail);
        var journey = new JourneyService(_db, new FixedClock(Now), TestMessaging.Service(_db, new FixedClock(Now), _email));
        var detail = (await journey.GetCandidateAsync(casey.Id))!;
        CandidateInput Input(string? name, string? email, string? ati) => new(
            detail.FirstName, detail.LastName, detail.Email, detail.PositionTitle, detail.Department, detail.HiringManagerContact,
            detail.AnticipatedStartDate, detail.EmployeeId, detail.Recruiter?.Id, detail.ServiceChannel?.Id,
            detail.UnitHr?.Id, detail.Requirements, detail.I9Method, detail.Qualifications, name, email, ati);

        var halfDone = () => journey.UpdateCandidateAsync(casey.Id, Input(null, "jesse@example.test", "bad ati!"), sample);
        (await halfDone.Should().ThrowAsync<JourneyValidationException>()).Which.Errors.Keys
            .Should().BeEquivalentTo("i9RepresentativeName", "fingerprintAtiNumber");

        var saved = (await journey.UpdateCandidateAsync(casey.Id, Input("Jesse Brooks", "jesse@example.test", "a1b2-c3"), sample))!;

        (saved.I9RepresentativeName, saved.I9RepresentativeEmail, saved.FingerprintAtiNumber)
            .Should().Be(("Jesse Brooks", "jesse@example.test", "A1B2-C3"));
        saved.History.First().Detail.Should().Contain("I-9 representative").And.Contain("ATI number");
    }
}
