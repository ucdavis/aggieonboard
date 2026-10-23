using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Scheduling;

namespace Server.Core.Journey;

/// <summary>Loads for the journey that more than one service needs.</summary>
public static class JourneyData
{
    /// <summary>A candidate with everything the journey and its messages read.</summary>
    public static IQueryable<Candidate> CandidatesWithJourney(AppDbContext db) =>
        db.Candidates
            .Include(c => c.Steps).ThenInclude(s => s.Step)
            .Include(c => c.Steps).ThenInclude(s => s.CompletedBy)
            .Include(c => c.Qualifications)
            .Include(c => c.RecruitingAssistant)
            .Include(c => c.Recruiter)
            .Include(c => c.OnboardingSpecialist)
            .AsSplitQuery();

    /// <summary>The deadline rules as they stand: holidays and the clearance setting are read fresh each time.</summary>
    public static async Task<DeadlineCalculator> LoadDeadlinesAsync(AppDbContext db, CancellationToken ct = default)
    {
        var holidays = await db.Holidays.Select(h => h.Date).ToListAsync(ct);
        var clearance = await db.Settings
            .Where(s => s.Key == SettingKeys.ClearanceBusinessDaysBeforeStart)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(ct);
        return new DeadlineCalculator(new BusinessCalendar(holidays), int.TryParse(clearance, out var days) ? days : 0);
    }
}
