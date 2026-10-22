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
        [SettingKeys.EmployeeDataFormContact] = "Chris Alvarez at chris.alvarez@example.test",
        [SettingKeys.OehcFormUrl] = "https://example.test/oehc-appointment-request",
        [SettingKeys.FingerprintSchedulingUrl] = "https://example.test/fingerprinting",
    };

    public static async Task SeedAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct = default)
    {
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

        var ra = Staff("Dana Ortiz", StaffRole.RecruitingAssistant);
        var recruiter = Staff("Chris Alvarez", StaffRole.Recruiter);
        var specialist = Staff("Pat Kim", StaffRole.OnboardingSpecialist, "916-555-0142");
        var specialist2 = Staff("Jamie Fox", StaffRole.OnboardingSpecialist);
        db.Staff.AddRange(ra, recruiter, specialist, specialist2, Staff("Robin Hale", StaffRole.LearningAndDevelopment));

        Candidate New(string first, string last, string position, string department, StaffMember? onboarding = null)
        {
            var candidate = new Candidate
            {
                FirstName = first,
                LastName = last,
                Email = $"{first}.{last}@example.test".ToLowerInvariant(),
                PositionTitle = position,
                Department = department,
                RecruitingAssistant = ra,
                Recruiter = recruiter,
                OnboardingSpecialist = onboarding,
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
        var morgan = New("Morgan", "Chen", "Respiratory Therapist", "Respiratory Care", specialist);
        morgan.AnticipatedStartDate = StartIn(4);
        morgan.RequiresDrugScreen = true;
        morgan.Qualifications.Add(License("Respiratory Care Practitioner (California)", "A copy of your active RCP license"));
        Path(morgan).Done("references", 30).Done("qualifications", 26).Done("offer", 20).Done("employee-data", 12)
            .Active("background-check", daysAgo: 1).Active("employee-health", daysAgo: 1).Active("drug-screen", daysAgo: 1);

        // 6. Every conditional requirement, remote I-9.
        var casey = New("Casey", "Brooks", "Security Officer", "Police and Security Services", specialist2);
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
        var riley = New("Riley", "Johnson", "Medical Assistant", "Primary Care Network", specialist);
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
            return new JourneyBuilder(candidate, now, deadlines, candidate.RecruitingAssistant);
        }
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
