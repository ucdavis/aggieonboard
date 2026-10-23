using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Seed;

/// <summary>
/// Inserts any part of <see cref="JourneyCatalog"/> the database does not have yet. Matches on keys
/// and never updates, so a step, setting or message edited in the app keeps its edit. A message
/// that exists in any version is left alone: new wording arrives as a version, not a reseed.
/// </summary>
public static class ReferenceDataSeeder
{
    public const string SeedAuthor = "seed";

    public static async Task SeedAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct = default)
    {
        var stepKeys = await db.StepDefinitions.Select(s => s.Key).ToListAsync(ct);
        db.StepDefinitions.AddRange(JourneyCatalog.Steps().Where(s => !stepKeys.Contains(s.Key)));

        var messageKeys = await db.MessageTemplates.Select(m => m.Key).Distinct().ToListAsync(ct);
        db.MessageTemplates.AddRange(JourneyCatalog.Messages()
            .Where(m => !messageKeys.Contains(m.Key))
            .Select(m => new MessageTemplate
            {
                Key = m.Key,
                Version = 1,
                AuditReference = m.AuditReference,
                Sender = m.Sender,
                Subject = m.Subject,
                Sections = m.Sections
                    .Select((section, i) => new MessageTemplateSection { Order = i + 1, Condition = section.Condition, Body = section.Body })
                    .ToList(),
                CreatedAt = now,
                CreatedBy = SeedAuthor,
            }));

        var settingKeys = await db.Settings.Select(s => s.Key).ToListAsync(ct);
        db.Settings.AddRange(JourneyCatalog.Settings().Where(s => !settingKeys.Contains(s.Key)));

        var holidayDates = await db.Holidays.Select(h => h.Date).ToListAsync(ct);
        db.Holidays.AddRange(JourneyCatalog.Holidays().Where(h => !holidayDates.Contains(h.Date)));

        await db.SaveChangesAsync(ct);
        await SeedSchedulingAsync(db, ct);
    }

    /// <summary>Teams, appointment types and their locations. A location's hours are seeded with it, never added later.</summary>
    private static async Task SeedSchedulingAsync(AppDbContext db, CancellationToken ct)
    {
        var teamKeys = await db.Teams.Select(t => t.Key).ToListAsync(ct);
        db.Teams.AddRange(JourneyCatalog.Teams().Where(t => !teamKeys.Contains(t.Key)));
        var typeKeys = await db.AppointmentTypes.Select(t => t.Key).ToListAsync(ct);
        var newTypes = JourneyCatalog.AppointmentTypes().Where(t => !typeKeys.Contains(t.Key)).ToList();
        db.AppointmentTypes.AddRange(newTypes);
        await db.SaveChangesAsync(ct);

        var teams = await db.Teams.ToDictionaryAsync(t => t.Key, ct);
        foreach (var type in newTypes)
        {
            foreach (var l in JourneyCatalog.Locations().Where(l => l.TypeKey == type.Key))
            {
                db.Locations.Add(new Location
                {
                    AppointmentTypeId = type.Id,
                    TeamId = teams[l.TeamKey].Id,
                    Name = l.Name,
                    Address = l.Address,
                    Hours = l.Hours.Select(h => new WeeklyHours
                    {
                        Day = h.Day, Start = h.Start, End = h.End, SlotMinutes = h.SlotMinutes, Capacity = h.Capacity,
                    }).ToList(),
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
