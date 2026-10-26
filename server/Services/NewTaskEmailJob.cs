using Server.Core.Journey;

namespace Server.Services;

/// <summary>
/// Sends the held emails for steps that opened on their own (<see cref="NewTaskEmails"/>) every few
/// minutes. The interval is the product requirement, not a wait for something else. One instance.
/// </summary>
public sealed class NewTaskEmailJob(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, ILogger<NewTaskEmailJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = Math.Max(1, configuration.GetValue("Journey:NewTaskCheckMinutes", 5));
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sent = await scope.ServiceProvider.GetRequiredService<NewTaskEmails>().SendDueAsync(stoppingToken);
                if (sent > 0) logger.LogInformation("Sent {Count} new-step emails.", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sending new-step emails failed; trying again at the next check.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
