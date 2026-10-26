using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Messaging;

namespace Server.Core.Journey;

/// <summary>
/// Emails about steps that opened on their own (JourneyFlow), once the new employee has been away
/// from their checklist for the NewTaskEmailDelayMinutes setting. One email per candidate per check:
/// the step's own message when a single step opened and it has one, otherwise the new-steps email
/// listing them. A step done by then is never emailed about. Run on one instance (like reminders).
/// </summary>
public sealed class NewTaskEmails(AppDbContext db, TimeProvider clock, MessageService messages)
{
    public const int DefaultDelayMinutes = 60;

    /// <summary>Settles every held email that is due. Returns how many emails went.</summary>
    public async Task<int> SendDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var setting = await db.Settings.Where(s => s.Key == SettingKeys.NewTaskEmailDelayMinutes).Select(s => s.Value).SingleOrDefaultAsync(ct);
        var delay = TimeSpan.FromMinutes(int.TryParse(setting, out var minutes) && minutes >= 0 ? minutes : DefaultDelayMinutes);
        var cutoff = now - delay;

        var due = await db.TaskNotifications
            .Include(n => n.CandidateStep).ThenInclude(s => s!.Step)
            .Where(n => n.SettledAt == null && n.OpenedAt <= cutoff)
            .ToListAsync(ct);
        var candidates = due.Select(n => n.CandidateId).Distinct().ToList();
        var away = await db.Candidates
            .Where(c => candidates.Contains(c.Id) && c.ArchivedAt == null && (c.LastPortalActivityAt == null || c.LastPortalActivityAt <= cutoff))
            .Select(c => c.Id)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var group in due.GroupBy(n => n.CandidateId))
        {
            // In the checklist lately: they've seen it there. Archived: nobody to tell.
            if (!away.Contains(group.Key))
            {
                if (!await db.Candidates.AnyAsync(c => c.Id == group.Key && c.ArchivedAt == null, ct))
                {
                    Settle(group, now, "candidate archived");
                }
                continue;
            }

            var open = group.Where(n => n.CandidateStep!.Status == StepStatus.ActionNeeded).ToList();
            Settle(group.Except(open), now, "done first");

            // Staff may have sent the step's message by hand since it opened.
            var opened = group.Min(n => n.OpenedAt);
            var sentKeys = await db.SentMessages
                .Where(m => m.CandidateId == group.Key && m.SentAt >= opened)
                .Select(m => m.MessageKey)
                .ToListAsync(ct);
            var already = open.Where(n => n.CandidateStep!.Step!.RequestMessageKey is { } k && sentKeys.Contains(k)).ToList();
            Settle(already, now, "already emailed");
            open = open.Except(already).ToList();
            if (open.Count == 0) continue;

            var steps = open.Select(n => n.CandidateStep!.Step!).OrderBy(s => s.Order).ToList();
            var key = steps.Count == 1 && steps[0].RequestMessageKey is { } own ? own : SystemMessages.NewTasks;
            var outcome = await messages.SendForOpenedStepsAsync(
                group.Key, key, steps[0].Owner, key == SystemMessages.NewTasks ? steps.Select(s => s.TaskLabel).ToList() : null, ct);

            if (outcome?.Sent == true) sent++;
            Settle(open, now, outcome is null ? "message missing" : outcome.Sent ? "emailed" : outcome.Notice);
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }

    private static void Settle(IEnumerable<TaskNotification> notes, DateTimeOffset now, string outcome)
    {
        foreach (var note in notes)
        {
            note.SettledAt = now;
            note.Outcome = outcome.Length > 500 ? outcome[..500] : outcome;
        }
    }
}
