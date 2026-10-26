using System.Security.Claims;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Seed;
using Server.Helpers;
using Server.Services;

namespace Server.Tests.Helpers;

/// <summary>Admin → Emulate: only an admin's emulation is honoured, and it is marked so the API can refuse changes.</summary>
public class EmulationTests : IAsyncLifetime
{
    private readonly AppDbContext _db = TestDbContextFactory.CreateInMemory();

    public async Task InitializeAsync()
    {
        var now = new DateTimeOffset(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);
        await ReferenceDataSeeder.SeedAsync(_db, now);
        await DevelopmentSeeder.SeedAsync(_db, now);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private UserService Users() => new(NullLogger<UserService>.Instance, _db);

    private static ClaimsPrincipal SignedIn(string email, int? emulating = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, email), new("preferred_username", email) };
        if (emulating is { } id) claims.Add(new Claim(Emulation.StaffClaim, id.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static IEnumerable<string> Roles(ClaimsPrincipal? principal) =>
        principal?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? [];

    [Fact]
    public async Task An_admin_sees_the_app_as_the_person_they_emulate_with_the_emulating_marker()
    {
        var pat = await _db.Staff.SingleAsync(s => s.Email == "pat.kim@example.test");
        var scheduler = await _db.Staff.SingleAsync(s => s.Email == DevelopmentSeeder.SandboxSchedulerEmail);

        var asPat = SignedIn(DevelopmentSeeder.SandboxStaffEmail, pat.Id);
        (await Users().GetStaffMemberAsync(asPat))!.Name.Should().Be("Pat Kim");
        (await Users().GetSignedInStaffMemberAsync(asPat))!.Name.Should().Be("Sample User");
        Roles(await Users().UpdateUserPrincipalIfNeeded(asPat))
            .Should().BeEquivalentTo("ServiceChannel", AppRoles.Staff, AppRoles.Scheduling, AppRoles.Emulating);

        Roles(await Users().UpdateUserPrincipalIfNeeded(SignedIn(DevelopmentSeeder.SandboxStaffEmail, scheduler.Id)))
            .Should().BeEquivalentTo("TeamMember", AppRoles.Scheduling, AppRoles.Emulating);
    }

    [Fact]
    public async Task Someone_who_is_not_an_admin_cannot_emulate()
    {
        var sample = await _db.Staff.SingleAsync(s => s.Email == DevelopmentSeeder.SandboxStaffEmail);

        var forged = SignedIn("pat.kim@example.test", sample.Id);

        (await Users().GetStaffMemberAsync(forged))!.Name.Should().Be("Pat Kim");
        Roles(await Users().UpdateUserPrincipalIfNeeded(forged)).Should().NotContain([AppRoles.Admin, AppRoles.Emulating]);
    }
}
