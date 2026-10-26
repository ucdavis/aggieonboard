using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Server.Controllers.Admin;
using Server.Core.Admin;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Messaging;
using Server.Core.Seed;
using Server.Tests.Messaging;

namespace Server.Tests.Admin;

public class TemplateAdminTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.CreateInMemory();
    private readonly RecordingEmailService _email = new();
    private StaffMember _admin = null!;

    public async Task InitializeAsync()
    {
        await ReferenceDataSeeder.SeedAsync(_db, Now);
        await DevelopmentSeeder.SeedAsync(_db, Now);
        _admin = await _db.Staff.SingleAsync(s => s.Email == DevelopmentSeeder.SandboxStaffEmail);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private MessageService Messages() => TestMessaging.Service(_db, new FixedClock(Now), _email);
    private TemplateAdmin Templates() => new(_db, new FixedClock(Now), Messages());

    private async Task<int> IdOf(string firstName) => (await _db.Candidates.SingleAsync(c => c.FirstName == firstName)).Id;

    /// <summary>The latest version's wording, as the editor would send it back.</summary>
    private async Task<TemplateEdit> EditOf(string key, Func<string, string>? body = null, string? subject = null)
    {
        var current = (await Templates().GetAsync(key))!;
        return new TemplateEdit(
            subject ?? current.Subject,
            current.Sections.Select(s => s with { Body = body?.Invoke(s.Body) ?? s.Body }).ToList(),
            current.Version);
    }

    [Fact]
    public void Only_admins_reach_the_admin_api()
    {
        typeof(AdminControllerBase).GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(AppRoles.Admin);
    }

    [Fact]
    public async Task Lists_each_message_once_at_its_latest_version_in_audit_order()
    {
        var list = await Templates().ListAsync();

        list.Select(t => t.Key).Should().OnlyHaveUniqueItems();
        list.Single(t => t.Key == "i9-choice").Version.Should().Be(2);
        list.Where(t => !SystemMessages.Keys.Contains(t.Key)).Select(t => t.AuditReference)
            .Should().Equal("1", "2", "4", "5", "7", "8", "9", "10", "11A", "11B", "12", "13");
        list.Single(t => t.Key == "additional-requirements").Steps.Should().HaveCount(4);
    }

    [Fact]
    public async Task Saving_adds_a_version_that_sends_from_then_on_and_keeps_the_old_one()
    {
        var edit = await EditOf("sso-welcome", b => b.Replace("Hi {{FirstName}},", "Hello {{FirstName}},"));

        var saved = (await Templates().SaveAsync("sso-welcome", edit, _admin))!;

        saved.Version.Should().Be(2);
        saved.CreatedBy.Should().Be(_admin.Email);
        saved.Versions.Select(v => v.Version).Should().Equal(2, 1);
        (await Templates().GetAsync("sso-welcome", 1))!.Sections[0].Body.Should().StartWith("Hi {{FirstName}},");

        var preview = (await Messages().PreviewAsync(await IdOf("Morgan"), "sso-welcome", _admin))!;
        preview.TemplateVersion.Should().Be(2);
        preview.Text.Should().StartWith("Hello Morgan,");

        var audit = await _db.AuditEvents.SingleAsync(e => e.Action == "template.saved");
        (audit.CandidateId, audit.ActorName, audit.Detail).Should().Be((null, _admin.Name, "Message 9: version 2 saved (was version 1)."));
    }

    [Fact]
    public async Task An_edit_from_an_older_version_or_with_no_change_is_refused()
    {
        var stale = await EditOf("sso-welcome", b => b + "\nP.S.\n");
        await Templates().SaveAsync("sso-welcome", await EditOf("sso-welcome", b => b + "\nThanks!\n"), _admin);

        var save = () => Templates().SaveAsync("sso-welcome", stale, _admin);
        await save.Should().ThrowAsync<JourneyRuleException>().WithMessage("Version 2 was saved by sample@example.test after you started editing*");

        var current = await EditOf("sso-welcome");
        var unchanged = () => Templates().SaveAsync("sso-welcome", current, _admin);
        await unchanged.Should().ThrowAsync<JourneyRuleException>().WithMessage("Nothing has changed since version 2.");
    }

    [Fact]
    public async Task Only_wording_the_app_can_fill_is_saved()
    {
        var edit = await EditOf("sso-welcome", subject: "");
        edit = edit with
        {
            Sections =
            [
                new(CandidateCondition.Always, "Hi {{FirstName}}, see {{Nickname}} and {{AppointmentTime}}. Due [date]. {{ First name }}"),
                new(CandidateCondition.DrugScreen, "  \n"),
                new(CandidateCondition.Always, "<!-- if: Parking -->Park here<!-- end -->"),
            ],
        };

        var save = () => Templates().SaveAsync("sso-welcome", edit, _admin);

        var errors = (await save.Should().ThrowAsync<JourneyValidationException>()).Which.Errors;
        errors["subject"].Should().Equal("The subject is required.");
        errors["sections[0].body"].Should().BeEquivalentTo(
            "{{Nickname}} isn't a field the app can fill. Use one from the list.",
            "{{AppointmentTime}} only has a value in appointment emails.",
            "[date] looks like a placeholder. Use a merge field, or give it a link: [date](https://…).",
            "A merge field is written {{FieldName}}, with no spaces or punctuation inside.");
        errors["sections[1].body"].Should().Equal("This section is empty. Write something or remove it.");
        errors["sections[2].body"].Should().Equal("Choose the section's audience instead of writing <!-- if: … --> markers.");
        (await _db.MessageTemplates.CountAsync(t => t.Key == "sso-welcome")).Should().Be(1);
    }

    [Fact]
    public async Task Appointment_emails_may_use_appointment_fields_and_vendor_wording_none()
    {
        var booked = await EditOf(SystemMessages.AppointmentBooked, b => b + "\nSee you at {{AppointmentTime}}.\n");
        (await Templates().SaveAsync(SystemMessages.AppointmentBooked, booked, _admin))!.Version.Should().Be(2);

        var vendor = await EditOf("quest-request", b => b + "\nHi {{FirstName}}\n");
        var save = () => Templates().SaveAsync("quest-request", vendor, _admin);
        (await save.Should().ThrowAsync<JourneyValidationException>()).Which.Errors.Values.SelectMany(v => v)
            .Should().Contain("{{FirstName}}: a vendor sends this message, so the app can't fill merge fields in it.");
        (await Templates().GetAsync("quest-request"))!.Fields.Should().BeEmpty();
    }

    [Fact]
    public async Task A_preview_fills_unsaved_wording_for_a_candidate_and_saves_nothing()
    {
        var morgan = await IdOf("Morgan");

        var preview = (await Templates().PreviewAsync("sso-welcome",
            new TemplatePreviewRequest(morgan, "Welcome, {{FirstName}}", [new(CandidateCondition.Always, "Start date: {{StartDate}}. {{Nickname}}")]),
            _admin))!;

        preview.Subject.Should().Be("Welcome, Morgan");
        preview.Missing.Should().ContainSingle(m => m.Name == "Nickname");
        preview.CanSend.Should().BeFalse();
        (await _db.MessageTemplates.CountAsync(t => t.Key == "sso-welcome")).Should().Be(1);
        _email.Sent.Should().BeEmpty();

        var reminder = (await Templates().GetAsync(SystemMessages.AppointmentReminder))!;
        var appointment = (await Templates().PreviewAsync(SystemMessages.AppointmentReminder,
            new TemplatePreviewRequest(morgan, reminder.Subject, reminder.Sections), _admin))!;
        appointment.Subject.Should().Be("Tomorrow: Example appointment");
        appointment.Missing.Should().BeEmpty();
    }
}
