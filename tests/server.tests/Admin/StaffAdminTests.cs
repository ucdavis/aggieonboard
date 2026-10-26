using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Admin;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Seed;
using Server.Tests.Messaging;

namespace Server.Tests.Admin;

public class StaffAdminTests : IAsyncLifetime
{
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

    private StaffAdmin Admin() => new(_db, new FixedClock(Now));

    private static StaffInput Input(StaffAdminView s) => new(s.Name, s.Email, s.Phone, s.Role, s.IsAdmin, s.IsActive, s.TeamIds);

    private async Task<StaffAdminView> Find(string email) => (await Admin().ListAsync()).Staff.Single(s => s.Email == email);

    [Fact]
    public async Task Adds_a_team_member_who_signs_in_to_their_team_schedule()
    {
        var teams = (await Admin().ListAsync()).Teams;
        var oehc = teams.Single(t => t.Key == "oehc");
        oehc.Locations.Should().ContainSingle();

        var saved = await Admin().CreateAsync(
            new("  Lee Park ", "lee.park@example.test", null, StaffRole.TeamMember, false, true, [oehc.Id]), _admin);

        saved.Member.TeamIds.Should().Equal(oehc.Id);
        var member = await _db.Staff.Include(s => s.Teams).SingleAsync(s => s.Email == "lee.park@example.test");
        (member.Name, member.Role, member.IsActive).Should().Be(("Lee Park", StaffRole.TeamMember, true));
        (await _db.AuditEvents.SingleAsync(e => e.Action == "staff.added")).Detail
            .Should().Be($"Lee Park added: lee.park@example.test, team member, teams: {oehc.Name}.");
    }

    [Fact]
    public async Task Names_and_emails_are_checked_and_emails_are_unique_in_any_case()
    {
        var create = () => Admin().CreateAsync(new("", "PAT.KIM@example.test", null, StaffRole.Recruiter, false, true, [999]), _admin);

        var errors = (await create.Should().ThrowAsync<JourneyValidationException>()).Which.Errors;
        errors["name"].Should().Equal("Name is required.");
        errors["email"].Should().Equal("Someone on the staff list already has this email.");
        errors["teamIds"].Should().Equal("Choose teams from the list.");
    }

    [Fact]
    public async Task An_admin_cannot_lock_themselves_out()
    {
        var me = await Find(_admin.Email);

        var demote = () => Admin().UpdateAsync(me.Id, Input(me) with { IsAdmin = false }, _admin);
        await demote.Should().ThrowAsync<JourneyRuleException>().WithMessage("You can't remove your own admin access*");
        var deactivate = () => Admin().UpdateAsync(me.Id, Input(me) with { IsActive = false }, _admin);
        await deactivate.Should().ThrowAsync<JourneyRuleException>();
    }

    [Fact]
    public async Task A_role_change_waits_until_candidates_are_reassigned_but_deactivating_says_who_to_reassign()
    {
        var pat = await Find("pat.kim@example.test");
        pat.AssignedCandidates.Should().BeGreaterThan(0);

        var change = () => Admin().UpdateAsync(pat.Id, Input(pat) with { Role = StaffRole.Recruiter }, _admin);
        await change.Should().ThrowAsync<JourneyRuleException>()
            .WithMessage("Pat Kim is the Service Channel specialist for 1 candidate. Reassign them on each candidate's page first.");

        var saved = (await Admin().UpdateAsync(pat.Id, Input(pat) with { IsActive = false, Phone = null }, _admin))!;

        saved.Notice.Should().Be("Pat Kim can no longer sign in, but is still the Service Channel specialist for 1 candidate. Reassign them on each candidate's page.");
        (await _db.AuditEvents.SingleAsync(e => e.Action == "staff.updated")).Detail
            .Should().StartWith("Pat Kim: pat.kim@example.test, Service Channel specialist, teams: ")
            .And.Contain(" → pat.kim@example.test, Service Channel specialist, inactive, teams: ");
    }

    [Fact]
    public async Task Team_membership_is_replaced_by_the_list_sent()
    {
        var teams = (await Admin().ListAsync()).Teams.ToDictionary(t => t.Key, t => t.Id);
        var me = await Find(_admin.Email);
        me.TeamIds.Should().Contain(teams["i9-desk"]);

        await Admin().UpdateAsync(me.Id, Input(me) with { TeamIds = [teams["campus-pd"]] }, _admin);

        (await Find(_admin.Email)).TeamIds.Should().Equal(teams["campus-pd"]);
    }
}
