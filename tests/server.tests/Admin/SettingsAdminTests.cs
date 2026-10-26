using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Admin;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Seed;
using Server.Tests.Messaging;

namespace Server.Tests.Admin;

public class SettingsAdminTests : IAsyncLifetime
{
    // Thursday 2026-10-22, 9 a.m. Pacific.
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.CreateInMemory();
    private StaffMember _admin = null!;

    public async Task InitializeAsync()
    {
        await ReferenceDataSeeder.SeedAsync(_db, Now);
        await DevelopmentSeeder.SeedAsync(_db, Now);
        _admin = await _db.Staff.SingleAsync(s => s.Email == DevelopmentSeeder.SandboxStaffEmail);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private SettingsAdmin Admin() => new(_db, new FixedClock(Now));

    private async Task<CandidateStep> StepOf(string firstName, string key) =>
        await _db.CandidateSteps.Include(s => s.Step).AsNoTracking()
            .SingleAsync(s => s.Candidate!.FirstName == firstName && s.Step!.Key == key);

    [Fact]
    public async Task A_setting_is_checked_against_its_kind()
    {
        async Task<IReadOnlyDictionary<string, string[]>> Refused(string key, SettingEdit edit)
        {
            var save = () => Admin().UpdateSettingAsync(key, edit, _admin);
            return (await save.Should().ThrowAsync<JourneyValidationException>()).Which.Errors;
        }

        (await Refused(SettingKeys.ClearanceBusinessDaysBeforeStart, new("six", false, null)))["value"]
            .Should().Equal("Enter a whole number from 0 to 365.");
        (await Refused(SettingKeys.OehcFormUrl, new("oehc.example.test/form", false, null)))["value"]
            .Should().Equal("Enter a full link, starting with https://.");
        (await Refused(SettingKeys.OehcFormUrl, new(" ", true, null)))["isConfirmed"]
            .Should().Equal("A confirmed setting needs a value.");

        var previous = (await _db.Settings.AsNoTracking().SingleAsync(s => s.Key == SettingKeys.OehcFormUrl)).Value;
        var saved = await Admin().UpdateSettingAsync(SettingKeys.OehcFormUrl, new("https://oehc.example.test/form", true, ""), _admin);

        saved!.Notice.Should().Be("OehcFormUrl saved.");
        var setting = await _db.Settings.AsNoTracking().SingleAsync(s => s.Key == SettingKeys.OehcFormUrl);
        (setting.Value, setting.IsConfirmed, setting.OpenQuestion).Should().Be(("https://oehc.example.test/form", true, null));
        (await Admin().HistoryAsync()).Single().Detail.Should().Be(
            $"OehcFormUrl: {(previous is null ? "no value" : $"“{previous}”")} → “https://oehc.example.test/form” (confirmed)");
    }

    [Fact]
    public async Task Changing_the_clearance_days_moves_open_clearance_deadlines_but_not_ones_staff_set()
    {
        var before = await StepOf("Casey", "fingerprinting");
        var overridden = await _db.CandidateSteps.Include(s => s.Step).SingleAsync(s => s.Candidate!.FirstName == "Casey" && s.Step!.Key == "i9");
        CandidateJourney.OverrideDue(overridden, new DateOnly(2026, 11, 2));
        await _db.SaveChangesAsync();

        var result = await Admin().UpdateSettingAsync(SettingKeys.ClearanceBusinessDaysBeforeStart, new("8", false, "SSO: confirm."), _admin);

        var after = await StepOf("Casey", "fingerprinting");
        after.DueDate.Should().BeBefore(before.DueDate!.Value, "two more business days before the same start date");
        (await StepOf("Casey", "i9")).DueDate.Should().Be(new DateOnly(2026, 11, 2));
        result!.Notice.Should().MatchRegex(@"^ClearanceBusinessDaysBeforeStart saved\. \d+ open due dates moved; each is noted in that candidate's history\.$");
        (await _db.AuditEvents.Where(e => e.CandidateId == after.CandidateId && e.Action == "step.due-recomputed").Select(e => e.Detail).ToListAsync())
            .Should().Contain($"Fingerprinting (Live Scan): due {before.DueDate:yyyy-MM-dd} → {after.DueDate:yyyy-MM-dd} (ClearanceBusinessDaysBeforeStart changed)");
    }

    [Fact]
    public async Task A_step_deadline_rule_is_checked_and_recomputes_open_due_dates()
    {
        async Task<IReadOnlyDictionary<string, string[]>> Refused(StepDeadlineEdit edit)
        {
            var save = () => Admin().UpdateStepDeadlineAsync("drug-screen", edit, _admin);
            return (await save.Should().ThrowAsync<JourneyValidationException>()).Which.Errors;
        }
        (await Refused(new(DeadlineAnchor.Activation, 0, DeadlineUnit.Hours, false, null)))["amount"]
            .Should().Equal("Enter a whole number from 1 to 1,000.");
        (await Refused(new(DeadlineAnchor.BeforeStart, 2, DeadlineUnit.BusinessHours, false, null)))["unit"]
            .Should().Equal("Count days or weeks from the start date.");

        var before = await StepOf("Morgan", "drug-screen");
        var result = await Admin().UpdateStepDeadlineAsync(
            "drug-screen", new(DeadlineAnchor.Activation, 5, DeadlineUnit.BusinessDays, true, null), _admin);

        result!.Notice.Should().StartWith("Drug screen saved. ");
        var after = await StepOf("Morgan", "drug-screen");
        after.DueDate.Should().NotBe(before.DueDate);
        after.DueTime.Should().BeNull("business days end at the end of the day");
        var rule = (await Admin().ListStepDeadlinesAsync()).Single(s => s.Key == "drug-screen");
        (rule.Rule, rule.Confirmed, rule.Question).Should().Be(("5 business days after the step starts", true, null));
    }

    [Fact]
    public async Task Holidays_are_added_once_and_removed_and_move_business_day_deadlines()
    {
        var before = await StepOf("Morgan", "drug-screen"); // 48 business hours from yesterday
        var holiday = before.DueDate!.Value;

        var added = await Admin().AddHolidayAsync(new(holiday, "Sandbox closure"), _admin);
        added.Notice.Should().StartWith("Sandbox closure added. ");
        (await StepOf("Morgan", "drug-screen")).DueDate.Should().BeAfter(holiday);

        var twice = () => Admin().AddHolidayAsync(new(holiday, "Again"), _admin);
        (await twice.Should().ThrowAsync<JourneyValidationException>()).Which.Errors["date"].Should().Equal("That date is already a holiday.");

        await Admin().RemoveHolidayAsync(holiday, _admin);
        (await StepOf("Morgan", "drug-screen")).DueDate.Should().Be(holiday);
        (await Admin().HistoryAsync()).Select(h => h.Action).Should().Equal("holiday.removed", "holiday.added");
    }
}
