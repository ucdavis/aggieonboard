using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Scheduling;

namespace Server.Core.Seed;

/// <summary>
/// Fictional staff and candidates spread across the journey, for the sandbox and local development.
/// Every name, address and ID is invented; email domains are example.test (AGENTS.md, invariant 9).
/// Dates are relative to <c>now</c> so "overdue" and "due soon" mean something on any day.
/// Runs only into an empty candidate table.
/// </summary>
public static class DevelopmentSeeder
{
    /// <summary>Stand-ins for settings nobody has answered yet, so the sandbox can show every message.</summary>
    private static readonly Dictionary<string, string> PlaceholderSettings = new()
    {
        [SettingKeys.ReferenceSurveyBusinessDays] = "5",
        [SettingKeys.QualificationSubmissionMethod] = "the secure upload link from your recruiting assistant (sandbox placeholder)",
        [SettingKeys.EmployeeDataFormUrl] = "https://example.test/employee-data-form",
        [SettingKeys.EmployeeDataFormContact] = "chris.alvarez@example.test",
        [SettingKeys.OehcFormUrl] = "https://example.test/oehc-appointment-request",
        [SettingKeys.FingerprintSchedulingUrl] = "https://example.test/fingerprinting",
    };

    /// <summary>
    /// The sandbox's "Sample User" sign-in (server/Helpers/LocalAuthentication.cs): a Service Channel
    /// specialist and admin. The group sign-ins (recruiter, Service Channel, Unit HR) are the fixture
    /// staff below; "Basic User" is left off on purpose, to show the signed-in-but-not-staff screen.
    /// </summary>
    public const string SandboxStaffEmail = "sample@example.test";

    /// <summary>The sandbox's "Schedule User": a police-department team member who sees only the schedule.</summary>
    public const string SandboxSchedulerEmail = "scheduler@example.test";

    public static async Task SeedAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct = default)
    {
        // Added on its own so an existing sandbox database picks it up too.
        var sandboxStaff = await db.Staff.SingleOrDefaultAsync(s => s.Email == SandboxStaffEmail, ct);
        if (sandboxStaff is null)
        {
            sandboxStaff = new StaffMember
            {
                Name = "Sample User",
                Email = SandboxStaffEmail,
                Role = StaffRole.ServiceChannel,
                IsAdmin = true,
            };
            db.Staff.Add(sandboxStaff);
            await db.SaveChangesAsync(ct);
        }

        await EnsureSandboxTeamsAsync(db, sandboxStaff, ct);

        if (await db.Candidates.AnyAsync(ct)) return;

        foreach (var setting in await db.Settings.Where(s => s.Value == null).ToListAsync(ct))
        {
            if (PlaceholderSettings.TryGetValue(setting.Key, out var value)) setting.Value = value;
        }

        var definitions = await db.StepDefinitions.OrderBy(s => s.Order).ToListAsync(ct);
        var holidays = await db.Holidays.Select(h => h.Date).ToListAsync(ct);
        var clearanceDays = int.Parse(
            (await db.Settings.SingleAsync(s => s.Key == SettingKeys.ClearanceBusinessDaysBeforeStart, ct)).Value!);
        var calendar = new BusinessCalendar(holidays);
        var deadlines = new DeadlineCalculator(calendar, clearanceDays);
        var today = DateOnly.FromDateTime(BusinessCalendar.ToPacific(now));

        // A start date that is a business day, at least `weeks` out (or back, when negative).
        DateOnly StartIn(int weeks) => calendar.AddBusinessDays(today.AddDays(7 * weeks - 1), 1);

        // Recruiter (Talent Acquisition), Service Channel and Unit HR: the groups that own the steps.
        var recruiter = Staff("Chris Alvarez", StaffRole.Recruiter);
        var recruiter2 = Staff("Dana Ortiz", StaffRole.Recruiter);
        var specialist = Staff("Pat Kim", StaffRole.ServiceChannel, "916-555-0142");
        var specialist2 = Staff("Jamie Fox", StaffRole.ServiceChannel);
        var learning = Staff("Robin Hale", StaffRole.UnitHr);
        db.Staff.AddRange(recruiter, recruiter2, specialist, specialist2, learning);

        Candidate New(string first, string last, string position, string department, StaffMember? onboarding = null)
        {
            var candidate = new Candidate
            {
                FirstName = first,
                LastName = last,
                Email = $"{first}.{last}@example.test".ToLowerInvariant(),
                PositionTitle = position,
                Department = department,
                Recruiter = recruiter,
                ServiceChannel = onboarding,
                UnitHr = learning,
                CreatedAt = now.AddDays(-30),
            };
            db.Candidates.Add(candidate);
            return candidate;
        }

        // 1. Just identified as a top candidate.
        var alex = New("Alex", "Rivera", "Clinical Nurse II", "Emergency Department");
        alex.Qualifications.Add(License("Registered Nurse (California)", "A copy of your active California RN license"));
        alex.Qualifications.Add(Degree("Bachelor of Science in Nursing"));
        Path(alex).Active("references", daysAgo: 1);

        // 2. Sending qualification documents.
        var jordan = New("Jordan", "Lee", "Pharmacy Technician", "Pharmacy Services");
        jordan.Qualifications.Add(License("Pharmacy Technician License (California Board of Pharmacy)", "A copy of your active license"));
        jordan.Qualifications.Add(Certification("PTCB Certified Pharmacy Technician", "A copy of your current certificate"));
        Path(jordan).Done("references", daysAgo: 9).Active("qualifications", daysAgo: 2, due: calendar.AddBusinessDays(today, 3));

        // 3. No qualifications to verify (the step is skipped); waiting on the offer.
        var sam = New("Sam", "Patel", "Administrative Assistant II", "Department of Surgery");
        Path(sam).Done("references", daysAgo: 12).Active("offer", daysAgo: 3);

        // 4. Accepted the offer; the Employee Data Gathering Form is overdue.
        var taylor = New("Taylor", "Nguyen", "Patient Care Technician", "Medical/Surgical Unit");
        taylor.AnticipatedStartDate = StartIn(5);
        taylor.Qualifications.Add(Certification("Basic Life Support (BLS)", "A copy of your current BLS card"));
        Path(taylor).Done("references", 25).Done("qualifications", 20).Done("offer", 14)
            .Active("employee-data", daysAgo: 8, due: calendar.AddBusinessDays(today, -3));

        // 5. Early pre-employment, with a drug screen.
        var morgan = New("Morgan", "Chen", "Respiratory Therapist", "Respiratory Care", sandboxStaff);
        morgan.AnticipatedStartDate = StartIn(4);
        morgan.RequiresDrugScreen = true;
        morgan.Qualifications.Add(License("Respiratory Care Practitioner (California)", "A copy of your active RCP license"));
        Path(morgan).Done("references", 30).Done("qualifications", 26).Done("offer", 20).Done("employee-data", 12)
            .Active("background-check", daysAgo: 1).Active("employee-health", daysAgo: 1).Active("drug-screen", daysAgo: 1);

        // 6. Every conditional requirement, remote I-9.
        var casey = New("Casey", "Brooks", "Security Officer", "Police and Security Services", specialist);
        casey.AnticipatedStartDate = StartIn(3);
        casey.RequiresDrugScreen = true;
        casey.RequiresDmvPullNotice = true;
        casey.RequiresFingerprinting = true;
        casey.RequiresPositionDescription = true;
        casey.EligibleForParking = true;
        casey.I9Method = I9Method.Remote;
        casey.Qualifications.Add(Certification("California Guard Card (BSIS)", "A copy of your current guard card"));
        Path(casey).Done("references", 40).Done("qualifications", 35).Done("offer", 28).Done("employee-data", 21)
            .Waiting("background-check", daysAgo: 14).Done("employee-health", 14).Done("drug-screen", 13)
            .Waiting("i9", daysAgo: 10).Active("i9-remote", daysAgo: 4)
            .Waiting("dmv", daysAgo: 6, due: calendar.AddBusinessDays(today, 2))
            .Active("fingerprinting", daysAgo: 6)
            .Active("position-description", daysAgo: 6, due: calendar.AddBusinessDays(today, 1))
            .Done("parking", 6);

        // 7. Cleared; finishing before-first-day items.
        var riley = New("Riley", "Johnson", "Medical Assistant", "Primary Care Network", sandboxStaff);
        riley.AnticipatedStartDate = StartIn(2);
        riley.EmployeeId = "SBX-0007";
        riley.HiringManagerContact = "Morgan Ellis at morgan.ellis@example.test";
        riley.EligibleForParking = true;
        riley.I9Method = I9Method.InPerson;
        riley.Qualifications.Add(Certification("Certified Medical Assistant (CMA)", "A copy of your current certificate"));
        Path(riley).Done("references", 45).Done("qualifications", 40).Done("offer", 35).Done("employee-data", 30)
            .Done("background-check", 25).Done("employee-health", 25).Done("i9", 20).Done("parking", 18)
            .Active("cleared", daysAgo: 2);

        // 8. Started; New Employee Welcome still ahead.
        var avery = New("Avery", "Thompson", "Financial Analyst", "Health Finance", specialist2);
        avery.AnticipatedStartDate = StartIn(-1);
        avery.EmployeeId = "SBX-0008";
        avery.HiringManagerContact = "Lee Morales at lee.morales@example.test";
        avery.I9Method = I9Method.InPerson;
        avery.Qualifications.Add(Degree("Bachelor's degree in Accounting or Finance"));
        Path(avery).Done("references", 60).Done("qualifications", 55).Done("offer", 50).Done("employee-data", 45)
            .Done("background-check", 40).Done("employee-health", 40).Done("i9", 35).Done("cleared", 10)
            .Active("new-employee-welcome", daysAgo: 7);

        await db.SaveChangesAsync(ct);

        await SeedScheduleAsync(db, now, calendar, ct, learning, specialist, (casey, "fingerprinting", "health-pd"),
            (morgan, "employee-health", "oehc"), (avery, "new-employee-welcome", "learning"), (riley, "i9", "i9-desk"));

        StaffMember Staff(string name, StaffRole role, string? phone = null) => new()
        {
            Name = name,
            Email = $"{name.Replace(' ', '.')}@example.test".ToLowerInvariant(),
            Phone = phone,
            Role = role,
        };

        JourneyBuilder Path(Candidate candidate)
        {
            CandidateJourney.AddSteps(candidate, definitions);
            return new JourneyBuilder(candidate, now, deadlines, candidate.Recruiter);
        }
    }

    /// <summary>Schedule User and the sandbox's team memberships. Added on their own so an existing sandbox picks them up.</summary>
    private static async Task EnsureSandboxTeamsAsync(AppDbContext db, StaffMember sample, CancellationToken ct)
    {
        var scheduler = await db.Staff.SingleOrDefaultAsync(s => s.Email == SandboxSchedulerEmail, ct);
        if (scheduler is null)
        {
            scheduler = new StaffMember { Name = "Schedule User", Email = SandboxSchedulerEmail, Role = StaffRole.TeamMember };
            db.Staff.Add(scheduler);
            await db.SaveChangesAsync(ct);
        }

        var teams = await db.Teams.ToDictionaryAsync(t => t.Key, ct);
        await JoinAsync(db, sample, teams["i9-desk"], ct);
        await JoinAsync(db, scheduler, teams["health-pd"], ct);
        await JoinAsync(db, scheduler, teams["campus-pd"], ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task JoinAsync(AppDbContext db, StaffMember staff, Team team, CancellationToken ct)
    {
        var exists = await db.Set<TeamMembership>().AnyAsync(m => m.TeamId == team.Id && m.StaffMemberId == staff.Id, ct);
        if (!exists) db.Set<TeamMembership>().Add(new TeamMembership { TeamId = team.Id, StaffMemberId = staff.Id });
    }

    /// <summary>
    /// Stand-in hours for the locations nobody has confirmed, New Employee Welcome sessions every other
    /// Tuesday, and a few bookings so every roster has someone on it. All fictional.
    /// </summary>
    private static async Task SeedScheduleAsync(
        AppDbContext db, DateTimeOffset now, BusinessCalendar calendar, CancellationToken ct,
        StaffMember learning, StaffMember oehcStaff, params (Candidate Candidate, string StepKey, string TeamKey)[] bookings)
    {
        var locations = await db.Locations.Include(l => l.Team).Include(l => l.Hours).Include(l => l.AppointmentType)
            .ToDictionaryAsync(l => l.Team!.Key, ct);
        var teams = await db.Teams.ToDictionaryAsync(t => t.Key, ct);
        await JoinAsync(db, learning, teams["learning"], ct);
        await JoinAsync(db, oehcStaff, teams["oehc"], ct);

        void Hours(string team, TimeOnly start, TimeOnly end, int minutes, int capacity, params DayOfWeek[] days)
        {
            if (locations[team].Hours.Count > 0) return;
            locations[team].Hours.AddRange(days.Select(d => new WeeklyHours { Day = d, Start = start, End = end, SlotMinutes = minutes, Capacity = capacity }));
        }

        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        Hours("health-pd", new(9, 0), new(12, 0), 15, 1, weekdays);
        Hours("campus-pd", new(13, 0), new(16, 0), 30, 1, DayOfWeek.Tuesday, DayOfWeek.Thursday);
        Hours("oehc", new(8, 0), new(11, 30), 30, 2, weekdays);

        var today = DateOnly.FromDateTime(BusinessCalendar.ToPacific(now));
        var firstTuesday = Next(today, DayOfWeek.Tuesday);
        var sessions = Enumerable.Range(0, 4).Select(i => new ScheduleSession
        {
            LocationId = locations["learning"].Id,
            Start = firstTuesday.AddDays(14 * i).ToDateTime(new TimeOnly(8, 0)),
            End = firstTuesday.AddDays(14 * i).ToDateTime(new TimeOnly(12, 0)),
            Capacity = 40,
            Note = "Sandbox stand-in session",
        }).ToList();
        db.ScheduleSessions.AddRange(sessions);

        var businessToday = calendar.IsBusinessDay(today) ? today : calendar.AddBusinessDays(today, 1);
        var lastWednesday = Next(today.AddDays(-8), DayOfWeek.Wednesday); // within the past week, never today
        var starts = new Dictionary<string, DateTime>
        {
            ["health-pd"] = calendar.AddBusinessDays(today, 1).ToDateTime(new TimeOnly(9, 0)),
            ["oehc"] = businessToday.ToDateTime(new TimeOnly(10, 0)),
            ["learning"] = sessions[0].Start,
            ["i9-desk"] = lastWednesday.ToDateTime(new TimeOnly(9, 0)),
        };

        foreach (var (candidate, stepKey, teamKey) in bookings)
        {
            var location = locations[teamKey];
            var step = candidate.Steps.Single(s => s.Step!.Key == stepKey);
            var start = starts[teamKey];
            var length = teamKey == "learning" ? sessions[0].End - sessions[0].Start : TimeSpan.FromMinutes(location.Hours.FirstOrDefault()?.SlotMinutes ?? 30);
            var attended = step.Status == StepStatus.Complete;
            db.Appointments.Add(new Appointment
            {
                CandidateId = candidate.Id,
                AppointmentTypeId = location.AppointmentTypeId,
                LocationId = location.Id,
                Start = start,
                End = start + length,
                Status = attended ? AppointmentStatus.Attended : AppointmentStatus.Booked,
                BookedAt = now.AddDays(-3),
                BookedBy = "Candidate",
                StatusChangedAt = attended ? now.AddDays(-2) : null,
                StatusChangedBy = attended ? "Sample User" : null,
            });
            if (step.Status == StepStatus.ActionNeeded) CandidateJourney.MarkWaiting(step);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The first <paramref name="day"/> strictly after <paramref name="date"/>.</summary>
    private static DateOnly Next(DateOnly date, DayOfWeek day)
    {
        var ahead = ((int)day - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(ahead == 0 ? 7 : ahead);
    }

    private static CandidateQualification License(string name, string document) =>
        new() { Kind = QualificationKind.License, Name = name, DocumentToSubmit = document };

    private static CandidateQualification Certification(string name, string document) =>
        new() { Kind = QualificationKind.Certification, Name = name, DocumentToSubmit = document };

    /// <summary>Degrees default to message 4's wording for what to send.</summary>
    private static CandidateQualification Degree(string name) =>
        new() { Kind = QualificationKind.Degree, Name = name, DocumentToSubmit = CandidateQualification.DegreeDocument };

    /// <summary>Walks one candidate's steps with the same moves the app makes.</summary>
    private sealed class JourneyBuilder(Candidate candidate, DateTimeOffset now, DeadlineCalculator deadlines, StaffMember? by)
    {
        public JourneyBuilder Done(string key, int daysAgo)
        {
            var step = Activate(key, daysAgo + 2, null);
            CandidateJourney.Complete(step, now.AddDays(-daysAgo), by);
            return this;
        }

        public JourneyBuilder Active(string key, int daysAgo, DateOnly? due = null)
        {
            Activate(key, daysAgo, due);
            return this;
        }

        public JourneyBuilder Waiting(string key, int daysAgo, DateOnly? due = null)
        {
            CandidateJourney.MarkWaiting(Activate(key, daysAgo, due));
            return this;
        }

        private CandidateStep Activate(string key, int daysAgo, DateOnly? due)
        {
            var step = candidate.Steps.Single(s => s.Step!.Key == key);
            if (due is { } date) CandidateJourney.OverrideDue(step, date);
            CandidateJourney.Activate(candidate, step, now.AddDays(-daysAgo), deadlines);
            return step;
        }
    }
}
