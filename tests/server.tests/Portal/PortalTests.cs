using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Server.Controllers.Portal;
using Server.Controllers.Staff;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Portal;
using Server.Core.Scheduling;
using Server.Core.Seed;
using Server.Helpers;
using Server.Tests.Messaging;

namespace Server.Tests.Portal;

public class PortalTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.CreateInMemory();
    private readonly RecordingEmailService _email = new();
    private MutableClock _clock = new(Now);

    public async Task InitializeAsync()
    {
        await ReferenceDataSeeder.SeedAsync(_db, Now);
        await DevelopmentSeeder.SeedAsync(_db, Now);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private PortalTokens Tokens() => new(_db, _clock, TestMessaging.Portal());

    private PortalService Portal() =>
        new(_db, _clock, Tokens(), TestMessaging.Service(_db, _clock, _email),
            new SchedulingService(_db, _clock, TestMessaging.Service(_db, _clock, _email)));

    private async Task<int> IdOf(string firstName) => (await _db.Candidates.SingleAsync(c => c.FirstName == firstName)).Id;

    private async Task<string> IssueFor(string firstName)
    {
        var pending = PortalTokens.Create();
        Tokens().Record(await IdOf(firstName), pending);
        await _db.SaveChangesAsync();
        return pending.Token;
    }

    // Tokens

    [Fact]
    public async Task A_link_works_once()
    {
        var token = await IssueFor("Alex");

        (await Tokens().RedeemAsync(token)).Should().Be(new RedeemResult(await IdOf("Alex"), null));
        (await Tokens().RedeemAsync(token)).Failure.Should().Be(RedeemFailure.AlreadyUsed);
    }

    [Fact]
    public async Task Only_the_hash_is_stored()
    {
        var token = await IssueFor("Alex");

        var stored = await _db.CandidateAccessTokens.SingleAsync();
        stored.TokenHash.Should().HaveLength(64).And.NotContain(token);
        stored.ExpiresAt.Should().Be(Now.AddDays(14));
    }

    [Fact]
    public async Task A_link_stops_working_when_it_expires()
    {
        var token = await IssueFor("Alex");
        _clock = new MutableClock(Now.AddDays(14));

        (await Tokens().RedeemAsync(token)).Failure.Should().Be(RedeemFailure.Expired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public async Task An_unknown_link_signs_no_one_in(string? token)
    {
        (await Tokens().RedeemAsync(token)).Should().Be(new RedeemResult(null, RedeemFailure.Unknown));
    }

    [Fact]
    public async Task An_archived_candidate_cannot_sign_in()
    {
        var token = await IssueFor("Alex");
        (await _db.Candidates.SingleAsync(c => c.FirstName == "Alex")).ArchivedAt = Now;
        await _db.SaveChangesAsync();

        (await Tokens().RedeemAsync(token)).Failure.Should().Be(RedeemFailure.Unavailable);
    }

    // The portal view

    [Fact]
    public async Task The_view_shows_only_steps_that_apply_in_the_candidates_terms()
    {
        var casey = await _db.Candidates.Include(c => c.Steps).ThenInclude(s => s.Step).SingleAsync(c => c.FirstName == "Casey");
        var fingerprinting = casey.Steps.Single(s => s.Step!.Key == "fingerprinting");
        fingerprinting.Status = StepStatus.Blocked;
        fingerprinting.StaffNote = "Live Scan rejected: prints unreadable. Do not share.";
        await _db.SaveChangesAsync();

        var view = (await Portal().GetViewAsync(casey.Id))!;

        view.FirstName.Should().Be("Casey");
        view.Steps.Should().HaveCount(15, "Casey has every requirement and a remote I-9");
        State("i9-remote").Should().Be(PortalStepState.ToDo);
        State("dmv").Should().Be(PortalStepState.InProgress, "waiting is with UC Davis Health");
        State("fingerprinting").Should().Be(PortalStepState.InProgress, "blocked is internal");
        State("parking").Should().Be(PortalStepState.Done);
        State("cleared").Should().Be(PortalStepState.Upcoming);

        var alex = (await Portal().GetViewAsync(await IdOf("Alex")))!;
        alex.Steps.Select(s => s.Key).Should().NotContain("drug-screen", "it doesn't apply to Alex");

        System.Text.Json.JsonSerializer.Serialize(view).Should().NotContain("Do not share");

        PortalStepState State(string key) => view.Steps.Single(s => s.Key == key).State;
    }

    [Fact]
    public async Task Steps_say_who_to_ask_and_what_email_to_watch_for()
    {
        var view = (await Portal().GetViewAsync(await IdOf("Morgan")))!;

        var drug = view.Steps.Single(s => s.Key == "drug-screen");
        drug.WatchFor.Should().Be(new PortalWatchFor("Quest Diagnostics", "noreply@questdiagnostics.com", "Your Drug Screen QPassport"));
        drug.Contact.Should().Be(new PortalContact("Sample User", "sample@example.test"));
        drug.DueTime.Should().NotBeNull();

        view.Steps.Single(s => s.Key == "references").WatchFor.Should().BeNull("it is done");
        view.Steps.Single(s => s.Key == "references").Contact!.Name.Should().Be("Dana Ortiz");
    }

    [Fact]
    public async Task Sent_messages_are_linked_from_their_step_and_readable_only_by_their_candidate()
    {
        var morganId = await IdOf("Morgan");
        var me = await _db.Staff.SingleAsync(s => s.Email == DevelopmentSeeder.SandboxStaffEmail);
        await TestMessaging.Service(_db, _clock, _email).SendAsync(morganId, "sso-welcome", me);
        var sentId = (await _db.SentMessages.SingleAsync()).Id;

        var view = (await Portal().GetViewAsync(morganId))!;
        view.Steps.Single(s => s.Key == "employee-health").Messages.Should().ContainSingle().Which.Id.Should().Be(sentId);

        (await Portal().GetMessageAsync(morganId, sentId))!.Html.Should().Contain("/portal");
        (await Portal().GetMessageAsync(await IdOf("Alex"), sentId)).Should().BeNull();
    }

    // "Send me a new link"

    [Fact]
    public async Task A_known_address_gets_a_link_whatever_its_case()
    {
        await Portal().RequestLinkAsync("  Alex.Rivera@EXAMPLE.test ");

        var email = _email.Sent.Should().ContainSingle().Which;
        email.Recipients.To.Should().Equal("alex.rivera@example.test");
        email.Subject.Should().Be("Your UC Davis Health onboarding checklist");
        email.HtmlBody.Should().Contain($"{TestMessaging.BaseUrl}/c/");
        (await _db.CandidateAccessTokens.CountAsync()).Should().Be(1);
        (await _db.AuditEvents.SingleAsync()).ActorName.Should().Be("Candidate (asked for a new link)");
    }

    [Theory]
    [InlineData("nobody@example.test")]
    [InlineData("")]
    [InlineData(null)]
    public async Task An_unknown_address_gets_nothing(string? email)
    {
        await Portal().RequestLinkAsync(email);

        _email.Sent.Should().BeEmpty();
        (await _db.CandidateAccessTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Requests_beyond_three_an_hour_send_nothing()
    {
        for (var i = 0; i < 5; i++) await Portal().RequestLinkAsync("alex.rivera@example.test");

        _email.Sent.Should().HaveCount(3);
    }

    // Wiring

    [Fact]
    public void Staff_endpoints_require_the_staff_role_and_portal_endpoints_only_the_candidate_cookie()
    {
        typeof(StaffControllerBase).GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(AppRoles.Staff);

        var portal = typeof(PortalController).GetCustomAttribute<AuthorizeAttribute>()!;
        portal.Policy.Should().Be(CandidateAuthentication.Policy);
        portal.Roles.Should().BeNull();

        var anonymous = typeof(PortalController).GetMethods()
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            .Select(m => m.Name);
        anonymous.Should().BeEquivalentTo("SignIn", "RequestLink", "SignOutOfPortal");
    }

    [Fact]
    public async Task The_candidate_policy_accepts_only_the_candidate_scheme()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddCandidateAuthentication(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();

        var policy = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(CandidateAuthentication.Policy);

        policy!.AuthenticationSchemes.Should().Equal(CandidateAuthentication.Scheme);
        CandidateAuthentication.CandidateId(CandidateAuthentication.CreatePrincipal(42)).Should().Be(42);
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
