using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Messaging;
using Server.Core.Portal;
using Server.Core.Scheduling;
using Server.Core.Seed;
using Server.Tests.Messaging;

namespace Server.Tests.Journey;

/// <summary>Stages open on their own, emails wait until the new employee is away, and the portal shows only what's open.</summary>
public class JourneyFlowTests : IAsyncLifetime
{
    // Thursday 2026-10-22, 9 a.m. Pacific.
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

    private JourneyService Journey(DateTimeOffset at) => new(_db, new FixedClock(at), TestMessaging.Service(_db, new FixedClock(at), _email));
    private NewTaskEmails Emails(DateTimeOffset at) => new(_db, new FixedClock(at), TestMessaging.Service(_db, new FixedClock(at), _email));

    private PortalService Portal(DateTimeOffset at)
    {
        var clock = new FixedClock(at);
        var messages = TestMessaging.Service(_db, clock, _email);
        return new PortalService(_db, clock, new PortalTokens(_db, clock, TestMessaging.Portal()), messages,
            new SchedulingService(_db, clock, messages), new PortalAnswers(_db, clock, messages));
    }

    private async Task<int> IdOf(string firstName) => (await _db.Candidates.SingleAsync(c => c.FirstName == firstName)).Id;

    private async Task<StepStatus> StatusOf(string firstName, string key) =>
        (await _db.CandidateSteps.AsNoTracking().SingleAsync(s => s.Candidate!.FirstName == firstName && s.Step!.Key == key)).Status;

    [Fact]
    public async Task Completing_the_last_step_of_a_stage_opens_the_next_with_a_held_email()
    {
        var sam = await IdOf("Sam"); // on the offer (stage 3)

        await Journey(Now).ApplyStepActionAsync(sam, "offer", StepAction.Complete, null, _sample);

        (await StatusOf("Sam", "employee-data")).Should().Be(StepStatus.ActionNeeded);
        (await StatusOf("Sam", "background-check")).Should().Be(StepStatus.Upcoming, "stage 5 waits for stage 4");
        (await _db.TaskNotifications.SingleAsync(n => n.CandidateId == sam)).SettledAt.Should().BeNull();
        _email.Sent.Should().BeEmpty("the email waits for the new employee to be away");
    }

    [Fact]
    public async Task A_held_email_goes_only_after_the_new_employee_has_been_away_and_one_task_sends_its_own_message()
    {
        var recruiter = await _db.Staff.FirstAsync(s => s.Role == StaffRole.Recruiter);
        var jesse = (await Journey(Now).CreateCandidateAsync(new CandidateInput(
            "Jesse", "Rowe", "jesse.rowe@example.test", "Clinical Lab Scientist", "Pathology", null, null, null,
            recruiter.Id, _sample.Id, null, new CandidateRequirements(false, false, false, false, false), I9Method.NotChosen, []), _sample)).Id;

        (await Emails(Now.AddMinutes(30)).SendDueAsync()).Should().Be(0, "an hour hasn't passed");

        var candidate = await _db.Candidates.SingleAsync(c => c.Id == jesse);
        candidate.LastPortalActivityAt = Now.AddMinutes(40);
        await _db.SaveChangesAsync();
        (await Emails(Now.AddMinutes(61)).SendDueAsync()).Should().Be(0, "they were in the checklist 21 minutes ago");

        (await Emails(Now.AddMinutes(101)).SendDueAsync()).Should().Be(1);
        var email = _email.Sent.Single();
        email.Subject.Should().Be("Next step for Clinical Lab Scientist: Complete your reference check", "the references step sends message 1");
        email.ReplyTo.Should().Be(recruiter.Email, "the Recruiter group owns it");
        (await _db.TaskNotifications.SingleAsync(n => n.CandidateId == jesse)).Outcome.Should().Be("emailed");
    }

    [Fact]
    public async Task A_held_email_with_a_gap_is_not_sent_and_says_why()
    {
        var sam = await IdOf("Sam");
        await Journey(Now).ApplyStepActionAsync(sam, "offer", StepAction.Complete, null, _sample);

        (await Emails(Now.AddHours(2)).SendDueAsync()).Should().Be(0);

        _email.Sent.Should().BeEmpty("message 7 needs the employee data form's due date, which staff enter");
        (await _db.TaskNotifications.SingleAsync(n => n.CandidateId == sam)).Outcome.Should().StartWith("The email about the new step was not sent.");
        (await _db.AuditEvents.AnyAsync(e => e.CandidateId == sam && e.Action == "message.not-sent")).Should().BeTrue();
    }

    [Fact]
    public async Task Several_tasks_opening_together_are_one_email_and_a_task_done_first_is_never_emailed()
    {
        var taylor = await IdOf("Taylor"); // on the employee data form (stage 4)
        await Journey(Now).ApplyStepActionAsync(taylor, "employee-data", StepAction.Complete, null, _sample);
        var opened = await _db.TaskNotifications.Include(n => n.CandidateStep).ThenInclude(s => s!.Step)
            .Where(n => n.CandidateId == taylor).Select(n => n.CandidateStep!.Step!.Key).ToListAsync();
        opened.Should().BeEquivalentTo("background-check", "employee-health", "i9");

        await Journey(Now).ApplyStepActionAsync(taylor, "background-check", StepAction.MarkWaiting, null, _sample);
        (await Emails(Now.AddHours(2)).SendDueAsync()).Should().Be(1);

        var email = _email.Sent.Single();
        email.Subject.Should().Be("Next steps in your UC Davis Health onboarding");
        email.TextBody.Should().Contain("- Schedule your Employee Health appointment").And.Contain("- Complete your I-9")
            .And.NotContain("background check");
        (await _db.TaskNotifications.Where(n => n.CandidateId == taylor).Select(n => n.Outcome).ToListAsync())
            .Should().BeEquivalentTo("done first", "emailed", "emailed");
    }

    [Fact]
    public async Task The_portal_shows_what_is_open_and_done_but_not_later_stages()
    {
        var view = (await Portal(Now).GetViewAsync(await IdOf("Taylor")))!;

        view.Steps.Select(s => s.Key).Should().Equal("references", "qualifications", "offer", "employee-data");
        view.Steps.Last().Should().Match<PortalStep>(s => s.TaskLabel == "Complete your employee information form" && s.Kind == PortalTaskKind.External);
        (view.StepsDone, view.StepsTotal).Should().Be((3, 9));
        view.Contact!.Name.Should().Be("Chris Alvarez", "the employee data form is the recruiter's");
    }

    [Fact]
    public async Task A_task_shows_its_own_part_of_a_shared_message_and_external_tasks_are_marked_done_by_the_new_employee()
    {
        var casey = await IdOf("Casey");
        var portal = Portal(Now);

        var fingerprinting = (await portal.GetTaskBodyAsync(casey, "fingerprinting"))!.Html;
        fingerprinting.Should().Contain("fingerprint background check").And.NotContain("DMV Employer Pull Notice");
        (await portal.GetTaskBodyAsync(casey, "background-check"))!.Html.Should().BeNull("a vendor step has its summary instead");
        (await portal.GetTaskBodyAsync(casey, "new-employee-welcome")).Should().BeNull("later stages stay hidden");

        var answers = new PortalAnswers(_db, new FixedClock(Now), TestMessaging.Service(_db, new FixedClock(Now), _email));
        await answers.MarkDoneAsync(casey, "position-description");
        (await StatusOf("Casey", "position-description")).Should().Be(StepStatus.Waiting);

        var book = () => answers.MarkDoneAsync(casey, "fingerprinting");
        await book.Should().ThrowAsync<JourneyRuleException>().WithMessage("This task is done right here in your checklist.");
    }
}
