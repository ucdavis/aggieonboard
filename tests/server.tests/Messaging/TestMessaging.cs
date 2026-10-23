using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Core.Data;
using Server.Core.Messaging;
using Server.Core.Notification;
using Server.Core.Portal;

namespace Server.Tests.Messaging;

/// <summary>Captures what would have gone to the SMTP server, or fails like it.</summary>
public sealed class RecordingEmailService : IEmailService
{
    public List<EmailMessage> Sent { get; } = [];
    public bool Fail { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (Fail) throw new InvalidOperationException("SMTP refused the connection.");
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>Skips Razor and MJML in unit tests; one test renders the real layout.</summary>
public sealed class BodyOnlyRenderer : INotificationRenderer
{
    public Task<string> RenderAsync<TModel>(string templatePath, TModel model, CancellationToken cancellationToken = default) =>
        Task.FromResult(model is CandidateMessageModel m
            ? $"<html>{m.BodyHtml}{(m.ButtonUrl is null ? "" : $"<a href=\"{m.ButtonUrl}\">{m.ButtonText}</a>")}</html>"
            : "<html></html>");
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public static class TestMessaging
{
    public const string BaseUrl = "https://onboard.example.test";

    public static IOptions<PortalOptions> Portal(string baseUrl = BaseUrl) => Options.Create(new PortalOptions { BaseUrl = baseUrl });

    public static MessageService Service(
        AppDbContext db, TimeProvider clock, RecordingEmailService email, bool smtpConfigured = true, string baseUrl = BaseUrl) =>
        new(db, clock, new BodyOnlyRenderer(), email,
            Options.Create(new SmtpOptions { Host = smtpConfigured ? "smtp.example.test" : "" }),
            new PortalTokens(db, clock, Portal(baseUrl)), Portal(baseUrl),
            NullLogger<MessageService>.Instance);
}
