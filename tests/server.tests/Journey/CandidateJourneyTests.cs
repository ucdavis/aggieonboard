using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Scheduling;
using Server.Core.Seed;

namespace Server.Tests.Journey;

public class CandidateJourneyTests
{
    private static readonly DeadlineCalculator Deadlines =
        new(new BusinessCalendar(JourneyCatalog.Holidays().Select(h => h.Date)), clearanceBusinessDaysBeforeStart: 6);

    // Monday 2026-11-02, 10 a.m. Pacific.
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);

    private static Candidate NewCandidate(Action<Candidate>? configure = null)
    {
        var candidate = new Candidate { FirstName = "Test", AnticipatedStartDate = new DateOnly(2026, 11, 30) };
        configure?.Invoke(candidate);
        var id = 1;
        var definitions = JourneyCatalog.Steps().Select(d => { d.Id = id++; return d; });
        CandidateJourney.AddSteps(candidate, definitions);
        return candidate;
    }

    private static CandidateStep Step(Candidate c, string key) => c.Steps.Single(s => s.Step!.Key == key);

    [Fact]
    public void Activating_starts_the_clock_and_computes_the_due_date()
    {
        var candidate = NewCandidate();
        var references = Step(candidate, "references");

        CandidateJourney.Apply(candidate, references, StepAction.Activate, Monday, null, Deadlines);

        references.Status.Should().Be(StepStatus.ActionNeeded);
        references.ActivatedAt.Should().Be(Monday);
        references.DueDate.Should().Be(new DateOnly(2026, 11, 4));
    }

    [Fact]
    public void Completing_an_upcoming_step_records_when_it_happened()
    {
        var candidate = NewCandidate();
        var offer = Step(candidate, "offer");
        var staff = new StaffMember { Id = 7, Name = "Chris Alvarez" };

        CandidateJourney.Apply(candidate, offer, StepAction.Complete, Monday, staff, Deadlines);

        offer.Status.Should().Be(StepStatus.Complete);
        offer.ActivatedAt.Should().Be(Monday);
        offer.CompletedAt.Should().Be(Monday);
        offer.CompletedById.Should().Be(7);
    }

    [Fact]
    public void Reopening_clears_the_completion()
    {
        var candidate = NewCandidate();
        var offer = Step(candidate, "offer");
        CandidateJourney.Apply(candidate, offer, StepAction.Complete, Monday, new StaffMember { Id = 7 }, Deadlines);

        CandidateJourney.Apply(candidate, offer, StepAction.Reopen, Monday.AddHours(1), null, Deadlines);

        offer.Status.Should().Be(StepStatus.ActionNeeded);
        offer.CompletedAt.Should().BeNull();
        offer.CompletedById.Should().BeNull();
    }

    [Fact]
    public void Unblocking_returns_to_where_the_step_was()
    {
        var candidate = NewCandidate();
        var upcoming = Step(candidate, "offer");
        var started = Step(candidate, "references");
        CandidateJourney.Apply(candidate, started, StepAction.Activate, Monday, null, Deadlines);

        foreach (var step in new[] { upcoming, started })
        {
            CandidateJourney.Apply(candidate, step, StepAction.Block, Monday, null, Deadlines);
            CandidateJourney.Apply(candidate, step, StepAction.Unblock, Monday, null, Deadlines);
        }

        upcoming.Status.Should().Be(StepStatus.Upcoming);
        started.Status.Should().Be(StepStatus.ActionNeeded);
    }

    [Fact]
    public void A_move_the_status_does_not_allow_is_refused_with_a_reason()
    {
        var candidate = NewCandidate();
        var references = Step(candidate, "references");

        var act = () => CandidateJourney.Apply(candidate, references, StepAction.MarkWaiting, Monday, null, Deadlines);

        act.Should().Throw<JourneyRuleException>()
            .WithMessage("Reference check is upcoming; it cannot be marked waiting from there.");
        references.Status.Should().Be(StepStatus.Upcoming);
    }

    [Fact]
    public void A_skipped_step_has_no_moves()
    {
        var candidate = NewCandidate();

        Step(candidate, "drug-screen").Status.Should().Be(StepStatus.Skipped);
        CandidateJourney.AllowedActions(StepStatus.Skipped).Should().BeEmpty();
    }

    [Theory]
    [InlineData(StepStatus.Upcoming, new[] { StepAction.Activate, StepAction.Complete, StepAction.Block })]
    [InlineData(StepStatus.ActionNeeded, new[] { StepAction.MarkWaiting, StepAction.Complete, StepAction.Block })]
    [InlineData(StepStatus.Waiting, new[] { StepAction.Complete, StepAction.Block })]
    [InlineData(StepStatus.Blocked, new[] { StepAction.Complete, StepAction.Unblock })]
    [InlineData(StepStatus.Complete, new[] { StepAction.Reopen })]
    public void Offers_the_moves_each_status_allows(StepStatus status, StepAction[] expected)
    {
        CandidateJourney.AllowedActions(status).Should().Equal(expected);
    }

    [Fact]
    public void Turning_a_requirement_off_skips_its_open_step_and_clears_the_deadline()
    {
        var candidate = NewCandidate(c => c.RequiresDrugScreen = true);
        var drug = Step(candidate, "drug-screen");
        CandidateJourney.Apply(candidate, drug, StepAction.Activate, Monday, null, Deadlines);

        candidate.RequiresDrugScreen = false;
        var changed = CandidateJourney.ApplyConditions(candidate);

        changed.Should().ContainSingle().Which.Should().BeSameAs(drug);
        drug.Status.Should().Be(StepStatus.Skipped);
        drug.ActivatedAt.Should().BeNull();
        drug.DueDate.Should().BeNull();
    }

    [Fact]
    public void Turning_a_requirement_off_keeps_a_completed_step_as_history()
    {
        var candidate = NewCandidate(c => c.EligibleForParking = true);
        var parking = Step(candidate, "parking");
        CandidateJourney.Apply(candidate, parking, StepAction.Complete, Monday, null, Deadlines);

        candidate.EligibleForParking = false;
        CandidateJourney.ApplyConditions(candidate).Should().BeEmpty();

        parking.Status.Should().Be(StepStatus.Complete);
    }

    [Fact]
    public void Turning_a_requirement_on_brings_its_step_back_as_upcoming()
    {
        var candidate = NewCandidate();

        candidate.I9Method = I9Method.Remote;
        CandidateJourney.ApplyConditions(candidate);

        Step(candidate, "i9-remote").Status.Should().Be(StepStatus.Upcoming);
    }

    [Fact]
    public void A_new_start_date_moves_open_clearance_deadlines_but_not_hand_set_ones()
    {
        var candidate = NewCandidate(c => c.RequiresFingerprinting = true);
        var health = Step(candidate, "employee-health");
        var fingerprinting = Step(candidate, "fingerprinting");
        CandidateJourney.Apply(candidate, health, StepAction.Activate, Monday, null, Deadlines);
        CandidateJourney.Apply(candidate, fingerprinting, StepAction.Activate, Monday, null, Deadlines);
        CandidateJourney.OverrideDue(fingerprinting, new DateOnly(2026, 11, 20));
        health.DueDate.Should().Be(new DateOnly(2026, 11, 18));

        candidate.AnticipatedStartDate = new DateOnly(2026, 12, 7);
        CandidateJourney.RecomputeDues(candidate, Deadlines);

        // Six business days before Monday 12/7: Dec 4, 3, 2, 1, Nov 30, then past Thanksgiving to Wed 11/25.
        health.DueDate.Should().Be(new DateOnly(2026, 11, 25));
        fingerprinting.DueDate.Should().Be(new DateOnly(2026, 11, 20));
    }

    [Fact]
    public void Clearing_an_override_returns_to_the_rule()
    {
        var candidate = NewCandidate();
        var references = Step(candidate, "references");
        CandidateJourney.Apply(candidate, references, StepAction.Activate, Monday, null, Deadlines);
        CandidateJourney.OverrideDue(references, new DateOnly(2026, 11, 20));

        CandidateJourney.ClearDueOverride(candidate, references, Deadlines);

        references.DueOverridden.Should().BeFalse();
        references.DueDate.Should().Be(new DateOnly(2026, 11, 4));
    }

    [Fact]
    public void Overdue_means_still_owed_and_past_the_deadline()
    {
        var candidate = NewCandidate(c => c.RequiresDrugScreen = true);
        var references = Step(candidate, "references");
        var drug = Step(candidate, "drug-screen");
        CandidateJourney.Apply(candidate, references, StepAction.Activate, Monday, null, Deadlines);
        CandidateJourney.Apply(candidate, drug, StepAction.Activate, Monday, null, Deadlines);

        // References due end of Wed 11/4; drug screen due Wed 11/4 at 10 a.m.
        CandidateJourney.IsOverdue(references, Monday.AddDays(2)).Should().BeFalse("it is due at the end of today");
        CandidateJourney.IsOverdue(drug, Monday.AddDays(2).AddMinutes(1)).Should().BeTrue("its time has passed");
        CandidateJourney.IsOverdue(references, Monday.AddDays(3)).Should().BeTrue();

        CandidateJourney.Apply(candidate, references, StepAction.MarkWaiting, Monday.AddDays(3), null, Deadlines);
        CandidateJourney.IsOverdue(references, Monday.AddDays(3)).Should().BeFalse("the candidate has done their part");
    }

    [Fact]
    public void The_current_step_is_the_first_neither_complete_nor_skipped()
    {
        var candidate = NewCandidate();
        CandidateJourney.Apply(candidate, Step(candidate, "references"), StepAction.Complete, Monday, null, Deadlines);

        CandidateJourney.CurrentStep(candidate)!.Step!.Key.Should().Be("offer", "qualifications are skipped: none required");
    }
}
