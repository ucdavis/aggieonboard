using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Domain;
using Server.Core.Seed;

namespace Server.Tests.Seed;

public class SeedingTests
{
    // A Thursday morning in Pacific time, so relative fixture dates are predictable.
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reference_data_seeds_once_and_keeps_later_edits()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        await ReferenceDataSeeder.SeedAsync(db, Now);

        var step = await db.StepDefinitions.SingleAsync(s => s.Key == "drug-screen");
        step.Title = "Drug screen (edited)";
        var setting = await db.Settings.SingleAsync(s => s.Key == SettingKeys.ClearanceBusinessDaysBeforeStart);
        setting.Value = "7";
        await db.SaveChangesAsync();

        await ReferenceDataSeeder.SeedAsync(db, Now.AddDays(1));

        (await db.StepDefinitions.CountAsync()).Should().Be(JourneyCatalog.Steps().Count);
        (await db.MessageTemplates.CountAsync()).Should().Be(JourneyCatalog.Messages().Count);
        (await db.Settings.CountAsync()).Should().Be(SettingKeys.All.Length);
        (await db.Holidays.CountAsync()).Should().Be(JourneyCatalog.Holidays().Count);
        (await db.StepDefinitions.SingleAsync(s => s.Key == "drug-screen")).Title.Should().Be("Drug screen (edited)");
        (await db.Settings.SingleAsync(s => s.Key == SettingKeys.ClearanceBusinessDaysBeforeStart)).Value.Should().Be("7");
    }

    [Fact]
    public async Task Seeded_messages_are_version_one_with_ordered_sections()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        await ReferenceDataSeeder.SeedAsync(db, Now);

        var message = await db.MessageTemplates.Include(m => m.Sections).SingleAsync(m => m.Key == "qualifications-request");

        message.Version.Should().Be(1);
        message.CreatedBy.Should().Be(ReferenceDataSeeder.SeedAuthor);
        message.Sections.OrderBy(s => s.Order).Select(s => s.Condition).Should().Equal(
            CandidateCondition.Always, CandidateCondition.HasLicenses, CandidateCondition.HasDegrees, CandidateCondition.Always);
    }

    [Fact]
    public async Task Production_settings_leave_unanswered_values_empty()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        await ReferenceDataSeeder.SeedAsync(db, Now);

        (await db.Settings.SingleAsync(s => s.Key == SettingKeys.OehcFormUrl)).Value.Should().BeNull();
    }

    [Fact]
    public async Task Development_fixtures_cover_the_journey()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        await ReferenceDataSeeder.SeedAsync(db, Now);
        await DevelopmentSeeder.SeedAsync(db, Now);

        var candidates = await db.Candidates
            .Include(c => c.Steps).ThenInclude(s => s.Step)
            .Include(c => c.Qualifications)
            .ToListAsync();

        candidates.Should().HaveCount(8);
        candidates.Should().OnlyContain(c => c.Email.EndsWith("@example.test"));
        candidates.Should().OnlyContain(c => c.Steps.Count == JourneyCatalog.Steps().Count);

        // A step the candidate's position does not require is skipped, never left upcoming.
        foreach (var candidate in candidates)
        {
            candidate.Steps.Where(s => !candidate.Meets(s.Step!.Condition))
                .Should().OnlyContain(s => s.Status == StepStatus.Skipped, $"{candidate.FirstName}'s position does not require them");
        }

        StepOf("Sam", "qualifications").Status.Should().Be(StepStatus.Skipped);
        StepOf("Casey", "i9-remote").Status.Should().Be(StepStatus.ActionNeeded);

        var overdue = StepOf("Taylor", "employee-data");
        overdue.Status.Should().Be(StepStatus.ActionNeeded);
        overdue.DueOverridden.Should().BeTrue();
        overdue.DueDate.Should().BeBefore(new DateOnly(2026, 10, 22));

        // Activated an hour-based step: the due date carries a time of day.
        StepOf("Morgan", "background-check").DueTime.Should().NotBeNull();

        // Clearance-anchored steps get the clearance date from the start date.
        var morgan = candidates.Single(c => c.FirstName == "Morgan");
        StepOf("Morgan", "employee-health").DueDate.Should().BeBefore(morgan.AnticipatedStartDate!.Value);

        CandidateStep StepOf(string firstName, string key) =>
            candidates.Single(c => c.FirstName == firstName).Steps.Single(s => s.Step!.Key == key);
    }

    [Fact]
    public async Task Development_fixtures_fill_unanswered_settings_with_obvious_placeholders()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        await ReferenceDataSeeder.SeedAsync(db, Now);
        await DevelopmentSeeder.SeedAsync(db, Now);

        var urls = await db.Settings.Where(s => s.Kind == SettingKind.Url).Select(s => s.Value).ToListAsync();
        urls.Should().OnlyContain(v => v != null && v.StartsWith("https://example.test/"));
    }

    [Fact]
    public async Task Development_fixtures_never_seed_twice()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        await ReferenceDataSeeder.SeedAsync(db, Now);
        await DevelopmentSeeder.SeedAsync(db, Now);
        await DevelopmentSeeder.SeedAsync(db, Now.AddDays(1));

        (await db.Candidates.CountAsync()).Should().Be(8);
        (await db.Staff.CountAsync()).Should().Be(5);
    }
}
