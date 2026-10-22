using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Seed;

namespace Server.Tests.Journey;

public class JourneyServiceTests : IAsyncLifetime
{
    // Thursday 2026-10-22, 9 a.m. Pacific: the fixtures' "now".
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.CreateInMemory();
    private JourneyService _journey = null!;
    private StaffMember _me = null!;

    public async Task InitializeAsync()
    {
        await ReferenceDataSeeder.SeedAsync(_db, Now);
        await DevelopmentSeeder.SeedAsync(_db, Now);
        _journey = new JourneyService(_db, new FixedClock(Now));
        _me = await _db.Staff.SingleAsync(s => s.Email == DevelopmentSeeder.SandboxStaffEmail);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<int> IdOf(string firstName) => (await _db.Candidates.SingleAsync(c => c.FirstName == firstName)).Id;

    private async Task<CandidateInput> InputFor(string firstName)
    {
        var detail = (await _journey.GetCandidateAsync(await IdOf(firstName)))!;
        return new CandidateInput(
            detail.FirstName, detail.LastName, detail.Email, detail.PositionTitle, detail.Department,
            detail.HiringManagerContact, detail.AnticipatedStartDate, detail.EmployeeId,
            detail.RecruitingAssistant?.Id, detail.Recruiter?.Id, detail.OnboardingSpecialist?.Id,
            detail.Requirements, detail.I9Method, detail.Qualifications);
    }

    [Fact]
    public async Task The_pipeline_puts_overdue_candidates_first()
    {
        var rows = await _journey.GetPipelineAsync();

        rows.Should().HaveCount(8);
        rows[0].Name.Should().Be("Taylor Nguyen");
        rows[0].IsOverdue.Should().BeTrue();
        rows[0].CurrentStepKey.Should().Be("employee-data");
        rows.Where(r => r.IsOverdue).Should().ContainSingle();
    }

    [Fact]
    public async Task The_pipeline_counts_progress_over_the_steps_that_apply()
    {
        var sam = (await _journey.GetPipelineAsync()).Single(r => r.Name == "Sam Patel");

        sam.ApplicableSteps.Should().Be(8, "15 steps less qualifications, drug screen, remote I-9, DMV, fingerprinting, PD and parking");
        sam.CompletedSteps.Should().Be(1);
        sam.CurrentStepKey.Should().Be("offer");
        sam.Phase.Should().Be(JourneyPhase.TalentAcquisition);
    }

    [Fact]
    public async Task Mine_narrows_to_candidates_assigned_to_me()
    {
        var rows = await _journey.GetPipelineAsync(_me.Id);

        rows.Select(r => r.Name).Should().BeEquivalentTo("Morgan Chen", "Riley Johnson");
    }

    [Fact]
    public async Task Creating_a_candidate_lays_out_the_journey_and_records_who_did_it()
    {
        var ra = await _db.Staff.FirstAsync(s => s.Role == StaffRole.RecruitingAssistant);
        var input = new CandidateInput(
            "Jesse", "Rowe", "jesse.rowe@example.test", "Clinical Lab Scientist", "Pathology", null, null, null,
            ra.Id, null, null,
            new CandidateRequirements(DrugScreen: true, false, false, false, false), I9Method.NotChosen,
            [new QualificationInput(QualificationKind.Degree, "Bachelor of Science in Clinical Laboratory Science", null)]);

        var detail = await _journey.CreateCandidateAsync(input, _me);

        detail.Steps.Should().HaveCount(15);
        detail.Steps.Single(s => s.Key == "drug-screen").Status.Should().Be(StepStatus.Upcoming);
        detail.Steps.Single(s => s.Key == "qualifications").Status.Should().Be(StepStatus.Upcoming);
        detail.Steps.Single(s => s.Key == "dmv").Status.Should().Be(StepStatus.Skipped);
        detail.Qualifications.Single().DocumentToSubmit.Should().Be(CandidateQualification.DegreeDocument);
        detail.History.Should().ContainSingle().Which.ActorName.Should().Be("Sample User");
    }

    [Fact]
    public async Task Invalid_details_are_refused_field_by_field()
    {
        var recruiter = await _db.Staff.FirstAsync(s => s.Role == StaffRole.Recruiter);
        var input = new CandidateInput(
            " ", "Rowe", "not-an-email", "Clinical Lab Scientist", "", null, null, null,
            recruiter.Id, null, null,
            new CandidateRequirements(false, false, false, false, false), I9Method.NotChosen,
            [new QualificationInput(QualificationKind.License, "Clinical Laboratory Scientist License", " ")]);

        var act = () => _journey.CreateCandidateAsync(input, _me);

        var errors = (await act.Should().ThrowAsync<JourneyValidationException>()).Which.Errors;
        errors.Keys.Should().BeEquivalentTo(
            "firstName", "email", "department", "recruitingAssistantId", "qualifications[0].documentToSubmit");
        errors["recruitingAssistantId"].Should().ContainSingle().Which.Should().Be($"{recruiter.Name} is not a recruiting assistant.");
        (await _db.Candidates.CountAsync()).Should().Be(8);
    }

    [Fact]
    public async Task Editing_requirements_adds_steps_and_says_what_changed()
    {
        var input = await InputFor("Alex") with { Requirements = new CandidateRequirements(true, false, false, false, false) };

        var detail = (await _journey.UpdateCandidateAsync(await IdOf("Alex"), input, _me))!;

        detail.Steps.Single(s => s.Key == "drug-screen").Status.Should().Be(StepStatus.Upcoming);
        detail.History[0].Detail.Should().Be("Drug screen: no → yes; Drug screen: now upcoming.");
    }

    [Fact]
    public async Task Editing_nothing_writes_no_history()
    {
        var id = await IdOf("Alex");

        var detail = (await _journey.UpdateCandidateAsync(id, await InputFor("Alex"), _me))!;

        detail.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Reassigning_names_people_in_the_history()
    {
        var other = await _db.Staff.SingleAsync(s => s.Name == "Pat Kim");
        var input = await InputFor("Morgan") with { OnboardingSpecialistId = other.Id };

        var detail = (await _journey.UpdateCandidateAsync(await IdOf("Morgan"), input, _me))!;

        detail.OnboardingSpecialist!.Name.Should().Be("Pat Kim");
        detail.History[0].Detail.Should().Be("Onboarding specialist: Sample User → Pat Kim.");
    }

    [Fact]
    public async Task A_step_move_returns_the_new_state_and_its_history()
    {
        var detail = (await _journey.ApplyStepActionAsync(await IdOf("Alex"), "references", StepAction.MarkWaiting, "References entered.", _me))!;

        var references = detail.Steps.Single(s => s.Key == "references");
        references.Status.Should().Be(StepStatus.Waiting);
        references.AvailableActions.Should().Equal(StepAction.Complete, StepAction.Block);
        detail.History[0].Should().BeEquivalentTo(new
        {
            ActorName = "Sample User",
            Action = "step.waiting",
            Detail = "Reference check: action needed → waiting. References entered.",
        });
    }

    [Fact]
    public async Task A_refused_move_changes_nothing()
    {
        var id = await IdOf("Alex");

        var act = () => _journey.ApplyStepActionAsync(id, "offer", StepAction.Reopen, null, _me);

        await act.Should().ThrowAsync<JourneyRuleException>().WithMessage("Offer is upcoming*");
        (await _db.AuditEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Unknown_candidates_and_steps_are_not_found()
    {
        (await _journey.GetCandidateAsync(9999)).Should().BeNull();
        (await _journey.ApplyStepActionAsync(await IdOf("Alex"), "no-such-step", StepAction.Activate, null, _me)).Should().BeNull();
    }

    [Fact]
    public async Task Due_dates_can_be_set_by_hand_and_handed_back_to_the_rule()
    {
        var id = await IdOf("Alex");

        var set = (await _journey.SetStepDueAsync(id, "references", new DateOnly(2026, 10, 30), null, _me))!;
        set.Steps.Single(s => s.Key == "references").Should().BeEquivalentTo(new { DueDate = new DateOnly(2026, 10, 30), DueOverridden = true });

        var cleared = (await _journey.SetStepDueAsync(id, "references", null, null, _me))!;
        cleared.Steps.Single(s => s.Key == "references").Should().BeEquivalentTo(new { DueDate = new DateOnly(2026, 10, 23), DueOverridden = false });
        cleared.History[0].Detail.Should().Be("Reference check: due date back to its rule (2026-10-23)");
    }

    [Fact]
    public async Task A_completed_step_has_no_due_date_to_change()
    {
        var act = async () => await _journey.SetStepDueAsync(await IdOf("Jordan"), "references", new DateOnly(2026, 10, 30), null, _me);

        await act.Should().ThrowAsync<JourneyRuleException>();
    }

    [Fact]
    public async Task Detail_explains_each_deadline_rule()
    {
        var detail = (await _journey.GetCandidateAsync(await IdOf("Morgan")))!;

        detail.ClearanceDeadline.Should().Be(new DateOnly(2026, 11, 10));
        detail.Steps.Single(s => s.Key == "drug-screen").DeadlineRule.Should().Be("48 business hours after the step starts");
        detail.Steps.Single(s => s.Key == "employee-health").DeadlineRule.Should().Be("Clearance deadline (6 business days before start)");
        detail.Steps.Single(s => s.Key == "dmv").DeadlineRule.Should().Be("Set by staff");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
