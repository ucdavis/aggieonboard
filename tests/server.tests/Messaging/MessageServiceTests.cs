using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Messaging;
using Server.Core.Notification;
using Server.Core.Portal;
using System.Text.RegularExpressions;
using Server.Core.Seed;

namespace Server.Tests.Messaging;

public class MessageServiceTests : IAsyncLifetime
{
    // Thursday 2026-10-22, 9 a.m. Pacific.
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.CreateInMemory();
    private readonly RecordingEmailService _email = new();
    private readonly FixedClock _clock = new(Now);
    private StaffMember _me = null!;

    public async Task InitializeAsync()
    {
        await ReferenceDataSeeder.SeedAsync(_db, Now);
        await DevelopmentSeeder.SeedAsync(_db, Now);
        _me = await _db.Staff.SingleAsync(s => s.Email == DevelopmentSeeder.SandboxStaffEmail);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private MessageService Messages(bool smtpConfigured = true) => TestMessaging.Service(_db, _clock, _email, smtpConfigured);

    private async Task<Candidate> Candidate(string firstName) =>
        await JourneyData.CandidatesWithJourney(_db).SingleAsync(c => c.FirstName == firstName);

    [Fact]
    public async Task Every_app_message_fills_every_field_it_uses()
    {
        // Casey has every requirement and a remote I-9, so every section of every message applies.
        var casey = await Candidate("Casey");
        casey.EmployeeId = "SBX-0006";
        casey.HiringManagerContact = "Morgan Ellis at morgan.ellis@example.test";
        foreach (var key in new[] { "qualifications", "employee-data", "dmv", "position-description" })
        {
            CandidateJourney.OverrideDue(casey.Steps.Single(s => s.Step!.Key == key), new DateOnly(2026, 11, 2));
        }
        await _db.SaveChangesAsync();

        // Appointment emails are filled from an appointment; their own tests cover them.
        var keys = await _db.MessageTemplates.Where(t => t.Sender == MessageSender.App).Select(t => t.Key).ToListAsync();
        foreach (var key in keys.Where(k => !SystemMessages.Appointment.Contains(k)))
        {
            var preview = (await Messages().PreviewAsync(casey.Id, key, _me))!;
            preview.Missing.Should().BeEmpty($"'{key}' should be fillable for Casey");
            (preview.Subject + preview.Text).Should().NotContain("{{").And.NotContain("[missing");
        }
    }

    [Fact]
    public async Task A_preview_fills_the_message_for_the_candidate()
    {
        var alex = await Candidate("Alex");

        var preview = (await Messages().PreviewAsync(alex.Id, "references-request", _me))!;

        preview.CanSend.Should().BeTrue();
        preview.Subject.Should().Be("Next step for Clinical Nurse II: Complete your reference check");
        preview.Text.Should().StartWith("Hi Alex,").And.Contain("Thank you,\nSample User\nTalent Acquisition");
        preview.To.Should().Be("alex.rivera@example.test");
        preview.ReplyTo.Should().Be(DevelopmentSeeder.SandboxStaffEmail);
        preview.StartsSteps.Should().BeEmpty("the reference check has already started");
    }

    [Fact]
    public async Task Contacts_read_the_way_the_audit_writes_them()
    {
        var casey = await Candidate("Casey");

        var preview = (await Messages().PreviewAsync(casey.Id, "sso-welcome", _me))!;

        // Casey's onboarding specialist is Pat Kim, who has a phone on file.
        preview.Text.Should().Contain("Contact me at pat.kim@example.test or 916-555-0142.")
            .And.Contain("I'm Pat Kim, your onboarding specialist");
    }

    [Fact]
    public async Task An_unanswered_setting_blocks_sending_and_says_what_is_still_open()
    {
        var setting = await _db.Settings.SingleAsync(s => s.Key == SettingKeys.OehcFormUrl);
        setting.Value = null;
        await _db.SaveChangesAsync();
        var morgan = await Candidate("Morgan");

        var preview = (await Messages().PreviewAsync(morgan.Id, "sso-welcome", _me))!;

        preview.CanSend.Should().BeFalse();
        var missing = preview.Missing.Should().ContainSingle().Which;
        missing.Name.Should().Be("OehcFormUrl");
        missing.Fix.Should().Contain("SSO: provide the OEHC appointment/request form link (audit #9).");
        preview.Text.Should().Contain("[missing: Link to the Employee Health (OEHC) appointment request form]");

        var send = () => Messages().SendAsync(morgan.Id, "sso-welcome", _me);
        await send.Should().ThrowAsync<JourneyRuleException>().WithMessage("*OEHC*");
        _email.Sent.Should().BeEmpty();
        (await _db.SentMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Sending_a_request_starts_its_step_and_logs_exactly_what_went_out()
    {
        var jordan = await Candidate("Jordan");
        var qualifications = jordan.Steps.Single(s => s.Step!.Key == "qualifications");
        qualifications.Status = StepStatus.Upcoming;
        qualifications.ActivatedAt = null;
        await _db.SaveChangesAsync();

        (await Messages().SendAsync(jordan.Id, "qualifications-request", _me)).Should().BeTrue();

        var email = _email.Sent.Should().ContainSingle().Which;
        email.Recipients.To.Should().Equal("jordan.lee@example.test");
        email.ReplyTo.Should().Be(DevelopmentSeeder.SandboxStaffEmail);
        email.Subject.Should().Be("Action needed: Qualification documents for Pharmacy Technician");
        email.TextBody.Should().Contain("- Pharmacy Technician License (California Board of Pharmacy): A copy of your active license")
            .And.NotContain("**Education**", "Jordan has no degree to verify");

        var sent = await _db.SentMessages.SingleAsync();
        sent.Should().BeEquivalentTo(new
        {
            MessageKey = "qualifications-request", AuditReference = "4", Subject = email.Subject,
            SentByName = "Sample User", Automatic = false,
        });

        // The delivered email carries a one-time sign-in link; the logged copy points at the portal
        // instead, so nobody reading the log holds a credential.
        var token = SignInToken(email.HtmlBody);
        email.TextBody.Should().EndWith($"View your onboarding checklist: {TestMessaging.BaseUrl}/c/{token}\n");
        (await _db.CandidateAccessTokens.SingleAsync()).TokenHash.Should().Be(PortalTokens.Hash(token));
        sent.Html.Should().Contain($"{TestMessaging.BaseUrl}/portal").And.NotContain(token);
        sent.Text.Should().Contain($"{TestMessaging.BaseUrl}/portal").And.NotContain(token);
        sent.Html.Should().Be(email.HtmlBody.Replace($"/c/{token}", "/portal"));

        var step = (await Candidate("Jordan")).Steps.Single(s => s.Step!.Key == "qualifications");
        step.Status.Should().Be(StepStatus.ActionNeeded);
        step.ActivatedAt.Should().Be(Now);
        (await _db.AuditEvents.Select(e => e.Action).ToListAsync()).Should().BeEquivalentTo("message.sent", "step.activated");
    }

    private static string SignInToken(string html) =>
        Regex.Match(html, $@"{Regex.Escape(TestMessaging.BaseUrl)}/c/([A-Za-z0-9_-]+)").Groups[1].Value;

    [Fact]
    public async Task A_failed_delivery_leaves_no_working_link()
    {
        var alex = await Candidate("Alex");
        _email.Fail = true;

        var send = () => Messages().SendAsync(alex.Id, SystemMessages.PortalLink, _me);

        await send.Should().ThrowAsync<MessageDeliveryException>();
        (await _db.CandidateAccessTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Without_the_app_address_emails_have_no_button_and_links_cannot_be_sent()
    {
        var alex = await Candidate("Alex");
        var messages = TestMessaging.Service(_db, _clock, _email, baseUrl: "");

        (await messages.PreviewAsync(alex.Id, SystemMessages.PortalLink, _me))!.Blockers
            .Should().Contain("Sign-in links need the app's address (Portal:BaseUrl or Notification:BaseUrl).");

        await messages.SendAsync(alex.Id, "references-request", _me);
        _email.Sent.Single().HtmlBody.Should().NotContain("<a href");
        (await _db.CandidateAccessTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_sign_in_link_email_says_how_long_the_link_works()
    {
        var alex = await Candidate("Alex");

        var preview = (await Messages().PreviewAsync(alex.Id, SystemMessages.PortalLink, _me))!;

        preview.CanSend.Should().BeTrue();
        preview.Subject.Should().Be("Your UC Davis Health onboarding checklist");
        preview.Text.Should().Contain("The button works once and expires in 14 days.");
    }

    [Fact]
    public async Task Message_12_includes_only_the_requirements_that_apply()
    {
        var casey = await Candidate("Casey");
        var morgan = await Candidate("Morgan");
        morgan.RequiresFingerprinting = true;
        await _db.SaveChangesAsync();

        var all = (await Messages().PreviewAsync(casey.Id, "additional-requirements", _me))!;
        var one = (await Messages().PreviewAsync(morgan.Id, "additional-requirements", _me))!;

        all.Subject.Should().Be(
            "Action needed: DMV Employer Pull Notice, fingerprinting, position description and complimentary parking for your UC Davis Health position");
        one.Subject.Should().Be("Action needed: fingerprinting for your UC Davis Health position");
        one.Text.Should().Contain("Fingerprinting").And.NotContain("DMV Employer Pull Notice").And.NotContain("Complimentary Parking");
    }

    [Fact]
    public async Task A_message_for_steps_that_do_not_apply_cannot_be_sent()
    {
        var alex = await Candidate("Alex");

        var preview = (await Messages().PreviewAsync(alex.Id, "i9-remote", _me))!;

        preview.Blockers.Should().ContainSingle().Which.Should().Be("This message is for Remote I-9 representative, which doesn't apply to Alex.");
    }

    [Fact]
    public async Task Vendor_wording_is_shown_but_never_sent()
    {
        var morgan = await Candidate("Morgan");

        var preview = (await Messages().PreviewAsync(morgan.Id, "quest-request", _me))!;

        preview.CanSend.Should().BeFalse();
        preview.Blockers.Should().ContainSingle().Which.Should().StartWith("Quest Diagnostics sends this message");
    }

    [Fact]
    public async Task Nothing_can_be_sent_where_email_is_not_set_up()
    {
        var alex = await Candidate("Alex");

        var preview = (await Messages(smtpConfigured: false).PreviewAsync(alex.Id, "references-request", _me))!;

        preview.Blockers.Should().Contain("Email isn't set up on this server (Smtp:Host is empty).");
    }

    [Fact]
    public async Task A_refused_delivery_records_nothing_and_starts_nothing()
    {
        var taylor = await Candidate("Taylor");
        taylor.ServiceChannelId = _me.Id;
        await _db.SaveChangesAsync();
        _email.Fail = true;

        var send = () => Messages().SendAsync(taylor.Id, "sso-welcome", _me);

        await send.Should().ThrowAsync<MessageDeliveryException>();
        (await _db.SentMessages.CountAsync()).Should().Be(0);
        (await _db.AuditEvents.CountAsync()).Should().Be(0);
        (await Candidate("Taylor")).Steps.Single(s => s.Step!.Key == "employee-health").Status.Should().Be(StepStatus.Upcoming);
    }

    [Fact]
    public async Task Marking_qualifications_received_acknowledges_them_automatically()
    {
        var journey = new JourneyService(_db, _clock, Messages());
        var jordan = await Candidate("Jordan");

        var detail = (await journey.ApplyStepActionAsync(jordan.Id, "qualifications", StepAction.MarkWaiting, null, _me))!;

        var email = _email.Sent.Should().ContainSingle().Which;
        email.Subject.Should().Be("We've received your qualification documents");
        email.TextBody.Should().Contain("We've received the requested license and certification documents for the Pharmacy Technician position.");
        detail.Notice.Should().Be("Message 5 (“We've received your qualification documents”) was sent to jordan.lee@example.test.");
        detail.SentMessages.Should().ContainSingle().Which.Automatic.Should().BeTrue();
    }

    [Fact]
    public async Task An_acknowledgment_that_cannot_go_leaves_the_move_and_says_why()
    {
        var journey = new JourneyService(_db, _clock, Messages(smtpConfigured: false));
        var jordan = await Candidate("Jordan");

        var detail = (await journey.ApplyStepActionAsync(jordan.Id, "qualifications", StepAction.MarkWaiting, null, _me))!;

        detail.Steps.Single(s => s.Key == "qualifications").Status.Should().Be(StepStatus.Waiting);
        detail.Notice.Should().StartWith("Message 5 was not sent automatically. Email isn't set up");
        detail.History[0].Action.Should().Be("message.not-sent");
        _email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task The_email_layout_renders_through_razor_and_mjml()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.test",
            ["Smtp:Port"] = "587",
            ["Smtp:Username"] = "user",
            ["Smtp:Password"] = "password",
            ["Smtp:FromEmail"] = "no-reply@example.test",
            ["Smtp:FromName"] = "UC Davis Health",
        }).Build();
        var services = new ServiceCollection().AddLogging().AddNotificationServices(configuration);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var renderer = scope.ServiceProvider.GetRequiredService<INotificationRenderer>();

        var html = await renderer.RenderAsync(CandidateMessageModel.TemplatePath, new CandidateMessageModel
        {
            BodyHtml = MessageMarkdown.ToHtml("Hi Alex,\n\n**What you need to do**\n"),
            PreviewText = "Next step",
        });

        html.Should().Contain("UC Davis Health").And.Contain("<strong>What you need to do</strong>")
            .And.NotContain("<mjml").And.NotContain("do not reply");
    }
}
