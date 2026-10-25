using Microsoft.Extensions.Options;
using Server.Core.Scheduling;

namespace Server.Services;

/// <summary>
/// Sends day-before appointment reminders (<see cref="AppointmentReminders"/>) on a fixed interval.
/// The interval is the product requirement here, not a wait for something else to finish.
/// </summary>
public sealed class AppointmentReminderJob(
    IServiceScopeFactory scopes, IOptions<SchedulingOptions> options, TimeProvider clock, ILogger<AppointmentReminderJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.ReminderCheckMinutes)), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sent = await scope.ServiceProvider.GetRequiredService<AppointmentReminders>().SendDueAsync(stoppingToken);
                if (sent > 0) logger.LogInformation("Sent {Count} appointment reminders.", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sending appointment reminders failed; trying again at the next check.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
