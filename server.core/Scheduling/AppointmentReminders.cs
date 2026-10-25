using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Messaging;

namespace Server.Core.Scheduling;

/// <summary>
/// The day-before reminder. From <see cref="SchedulingOptions.ReminderHour"/> on the day before, each
/// booked appointment gets one reminder, unless it was booked or moved that same day: its
/// confirmation has just arrived.
/// </summary>
public sealed class AppointmentReminders(AppDbContext db, TimeProvider clock, MessageService messages, IOptions<SchedulingOptions> options)
{
    /// <summary>Sends the reminders now due and returns how many went.</summary>
    public async Task<int> SendDueAsync(CancellationToken ct = default)
    {
        var now = BusinessCalendar.ToPacific(clock.GetUtcNow());
        if (now.Hour < options.Value.ReminderHour) return 0;

        var tomorrow = now.Date.AddDays(1);
        var due = await db.Appointments
            .Where(a => a.Status == AppointmentStatus.Booked && a.ReminderSentAt == null
                && a.Start >= tomorrow && a.Start < tomorrow.AddDays(1))
            .OrderBy(a => a.Start)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var appointment in due.Where(a => BusinessCalendar.ToPacific(a.BookedAt).Date < now.Date))
        {
            var outcome = await messages.SendAppointmentAsync(
                appointment.Id, SystemMessages.AppointmentReminder, null, "AggieOnboard (reminder)", ct);

            // One try: a reminder that couldn't go is in the candidate's history, and trying again every
            // few minutes would fill it. (Phase 7: claim rows first if more than one instance sends.)
            appointment.ReminderSentAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            if (outcome?.Sent == true) sent++;
        }
        return sent;
    }
}
