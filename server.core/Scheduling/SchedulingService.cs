using System.Data;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Journey;
using Server.Core.Messaging;

namespace Server.Core.Scheduling;

/// <summary>
/// Booking, rosters and availability. Booking checks the slot inside a serializable transaction, so
/// two people cannot take the last seat; every change writes the candidate's history and moves the
/// journey step it fulfils. The candidate is emailed about every booking, change and cancellation,
/// after the change is saved: an email that fails never undoes a booking.
/// </summary>
public sealed class SchedulingService(AppDbContext db, TimeProvider clock, MessageService messages)
{
    /// <summary>The widest range of days one slot request may cover.</summary>
    public const int MaxRangeDays = 62;

    // Reading -----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<CandidateAppointmentType>> GetCandidateAppointmentsAsync(int candidateId, CancellationToken ct = default)
    {
        var candidate = await JourneyData.CandidatesWithJourney(db).SingleOrDefaultAsync(c => c.Id == candidateId, ct);
        if (candidate is null) return [];

        var types = await db.AppointmentTypes.OrderBy(t => t.Id).ToListAsync(ct);
        var appointments = await Appointments().Where(a => a.CandidateId == candidateId).OrderByDescending(a => a.Start).ToListAsync(ct);

        return types
            .Where(t => Applies(candidate, t))
            .Select(t =>
            {
                var mine = appointments.Where(a => a.AppointmentTypeId == t.Id).ToList();
                var current = mine.FirstOrDefault(a => a.IsActive);
                return new CandidateAppointmentType(
                    t.Key, t.Name, t.StepKey,
                    current is null ? null : Summary(current),
                    mine.Where(a => a != current).Select(Summary).ToList());
            })
            .ToList();
    }

    /// <summary>The portal's appointment for each step that has one, keyed by step.</summary>
    public async Task<IReadOnlyDictionary<string, PortalBooking>> GetPortalBookingsAsync(int candidateId, CancellationToken ct = default)
    {
        var candidate = await JourneyData.CandidatesWithJourney(db).SingleOrDefaultAsync(c => c.Id == candidateId, ct);
        if (candidate is null) return new Dictionary<string, PortalBooking>();

        var types = await db.AppointmentTypes.OrderBy(t => t.Id).ToListAsync(ct);
        var appointments = await Appointments()
            .Where(a => a.CandidateId == candidateId && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Attended))
            .OrderByDescending(a => a.Start)
            .ToListAsync(ct);
        var now = BusinessCalendar.ToPacific(clock.GetUtcNow());

        var bookings = new Dictionary<string, PortalBooking>();
        foreach (var type in types.Where(t => Applies(candidate, t)))
        {
            var step = StepFor(candidate, type);
            var shown = appointments.FirstOrDefault(a => a.AppointmentTypeId == type.Id && a.Status == AppointmentStatus.Booked)
                ?? appointments.FirstOrDefault(a => a.AppointmentTypeId == type.Id);

            // Nothing booked and nothing to book yet (coming up, or done some other way, like a remote I-9).
            if (shown is null && !OpenToCandidate(candidate, type, step)) continue;
            var cutoff = shown?.Start.AddHours(-type.ChangeCutoffHours);
            var canChange = shown is { Status: AppointmentStatus.Booked } && now < cutoff;
            var canBook = OpenToCandidate(candidate, type, step) && (shown is null || canChange);

            bookings[type.StepKey] = new PortalBooking(type.Key, type.Name, canBook, shown is null ? null : new PortalAppointment(
                shown.Id, shown.Start, shown.End, shown.Location!.Name, shown.Location.Address, shown.Status, cutoff!.Value, canChange));
        }
        return bookings;
    }

    /// <summary>
    /// What the candidate can book for this type between two dates, at every active location.
    /// <paramref name="forCandidate"/> applies the portal's rule: only a step they've been asked to do.
    /// </summary>
    public async Task<BookingOptions?> GetOptionsAsync(
        int candidateId, string typeKey, DateOnly from, DateOnly to, CancellationToken ct = default, bool forCandidate = false)
    {
        var candidate = await JourneyData.CandidatesWithJourney(db).SingleOrDefaultAsync(c => c.Id == candidateId, ct);
        var type = await db.AppointmentTypes.Include(t => t.Locations).SingleOrDefaultAsync(t => t.Key == typeKey, ct);
        if (candidate is null || type is null) return null;

        if (to < from) to = from;
        if (to.DayNumber - from.DayNumber > MaxRangeDays) to = from.AddDays(MaxRangeDays);

        var window = await WindowAsync(candidate, type, ct);
        var current = await Appointments().FirstOrDefaultAsync(a => a.CandidateId == candidateId && a.AppointmentTypeId == type.Id && a.Status == AppointmentStatus.Booked, ct);
        var unavailable = !Applies(candidate, type) ? $"{type.Name} doesn't apply to {candidate.FirstName}."
            : forCandidate && !OpenToCandidate(candidate, type, StepFor(candidate, type)) ? NotOpen(candidate, type)
            : window.Unavailable;

        var locations = new List<LocationSlots>();
        if (unavailable is null)
        {
            foreach (var location in type.Locations.Where(l => l.IsActive).OrderBy(l => l.Name))
            {
                var slots = await SlotsAsync(location.Id, from, to, window.Earliest, window.Latest, ct);
                locations.Add(new LocationSlots(location.Id, location.Name, location.Address, slots.Where(s => s.Remaining > 0).ToList()));
            }
        }

        return new BookingOptions(type.Key, type.Name, window.Earliest, window.Latest, unavailable,
            current is null ? null : Summary(current), locations);
    }

    /// <summary>The locations this person runs: their teams', or every one for an admin.</summary>
    public async Task<IReadOnlyList<LocationSummary>> GetManagedLocationsAsync(StaffMember actor, CancellationToken ct = default)
    {
        var teamIds = await TeamIdsAsync(actor, ct);
        return await db.Locations
            .Where(l => actor.IsAdmin || teamIds.Contains(l.TeamId))
            .OrderBy(l => l.AppointmentType!.Id).ThenBy(l => l.Name)
            .Select(l => new LocationSummary(l.Id, l.Name, l.Address, l.AppointmentType!.Key, l.AppointmentType.Name, l.Team!.Name))
            .ToListAsync(ct);
    }

    /// <summary>Who is coming on one day, by location, for the locations this person runs.</summary>
    public async Task<IReadOnlyList<RosterLocation>> GetRosterAsync(StaffMember actor, DateOnly date, int? locationId, CancellationToken ct = default)
    {
        var locations = (await GetManagedLocationsAsync(actor, ct)).Where(l => locationId is null || l.Id == locationId).ToList();
        var ids = locations.Select(l => l.Id).ToList();
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        var appointments = await Appointments()
            .Where(a => ids.Contains(a.LocationId) && a.Start >= dayStart && a.Start < dayEnd && a.Status != AppointmentStatus.Cancelled)
            .OrderBy(a => a.Start).ThenBy(a => a.Candidate!.LastName)
            .ToListAsync(ct);

        return locations.Select(l => new RosterLocation(
            l.Id, l.Name, l.Address, l.TeamName,
            appointments.Where(a => a.LocationId == l.Id).Select(a => new RosterEntry(
                a.Id, a.Start, a.End, a.Status, a.CandidateId,
                $"{a.Candidate!.FirstName} {a.Candidate.LastName}", a.Candidate.PositionTitle, a.Candidate.Department,
                a.AppointmentType!.Name, a.BookedBy)).ToList())).ToList();
    }

    public async Task<LocationAvailability?> GetAvailabilityAsync(int locationId, CancellationToken ct = default)
    {
        var location = await db.Locations.Include(l => l.Hours).Include(l => l.AppointmentType).Include(l => l.Team)
            .SingleOrDefaultAsync(l => l.Id == locationId, ct);
        if (location is null) return null;

        var today = Today();
        var sessions = await db.ScheduleSessions.Where(s => s.LocationId == locationId && s.End >= today.ToDateTime(TimeOnly.MinValue))
            .OrderBy(s => s.Start).ToListAsync(ct);
        var closures = await db.Closures.Where(c => c.LocationId == locationId && c.Date >= today).OrderBy(c => c.Date).ToListAsync(ct);
        var booked = await db.Appointments.Where(a => a.LocationId == locationId && a.Status == AppointmentStatus.Booked && a.Start >= today.ToDateTime(TimeOnly.MinValue))
            .Select(a => a.Start).ToListAsync(ct);

        return new LocationAvailability(
            new LocationSummary(location.Id, location.Name, location.Address, location.AppointmentType!.Key, location.AppointmentType.Name, location.Team!.Name),
            location.Hours.OrderBy(h => h.Day).ThenBy(h => h.Start).Select(h => new HoursInput(h.Day, h.Start, h.End, h.SlotMinutes, h.Capacity)).ToList(),
            sessions.Select(s => new SessionView(s.Id, s.Start, s.End, s.Capacity, booked.Count(b => b == s.Start), s.Note)).ToList(),
            closures.Select(c => new ClosureView(c.Id, c.Date, c.Reason, booked.Count(b => DateOnly.FromDateTime(b) == c.Date))).ToList());
    }

    public async Task<bool> CanManageAsync(StaffMember actor, int locationId, CancellationToken ct = default) =>
        (await GetManagedLocationsAsync(actor, ct)).Any(l => l.Id == locationId);

    /// <summary>Whether this person runs the location the appointment is at.</summary>
    public async Task<bool> CanManageAppointmentAsync(StaffMember actor, int appointmentId, CancellationToken ct = default)
    {
        var locationId = await db.Appointments.Where(a => a.Id == appointmentId).Select(a => (int?)a.LocationId).SingleOrDefaultAsync(ct);
        return locationId is { } id && await CanManageAsync(actor, id, ct);
    }

    // Booking -----------------------------------------------------------------------------------

    /// <summary>
    /// Books the slot that starts at <paramref name="start"/>, replacing the candidate's current
    /// appointment of this type if they have one (a reschedule). Throws <see cref="JourneyRuleException"/>
    /// with a reason written for whoever clicked.
    /// </summary>
    public async Task<AppointmentSummary> BookAsync(
        int candidateId, string typeKey, int locationId, DateTime start, SchedulingActor actor, CancellationToken ct = default)
    {
        (Appointment Booked, DateTime? PreviousStart) result;
        try
        {
            result = await BookInTransactionAsync(candidateId, typeKey, locationId, start, actor, ct);
        }
        catch (Exception ex) when (LostARace(ex))
        {
            // Two people took the last seat at once; SQL Server let one through and stopped the other.
            throw new JourneyRuleException("That time was just taken. Choose another.");
        }

        var key = result.PreviousStart is null ? SystemMessages.AppointmentBooked : SystemMessages.AppointmentChanged;
        var notice = await messages.SendAppointmentAsync(result.Booked.Id, key, result.PreviousStart, actor.Name, ct);
        return Summary(result.Booked) with { Notice = notice?.Notice };
    }

    /// <summary>A serializable booking that SQL Server chose as a deadlock victim (1205): someone else won the seat.</summary>
    private static bool LostARace(Exception ex)
    {
        for (var e = (Exception?)ex; e is not null; e = e.InnerException)
        {
            if (e is Microsoft.Data.SqlClient.SqlException { Number: 1205 }) return true;
        }
        return false;
    }

    private async Task<(Appointment Booked, DateTime? PreviousStart)> BookInTransactionAsync(
        int candidateId, string typeKey, int locationId, DateTime start, SchedulingActor actor, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;

        var candidate = await JourneyData.CandidatesWithJourney(db).SingleOrDefaultAsync(c => c.Id == candidateId, ct)
            ?? throw new JourneyRuleException("There is no such candidate.");
        var type = await db.AppointmentTypes.Include(t => t.Locations).SingleOrDefaultAsync(t => t.Key == typeKey, ct)
            ?? throw new JourneyRuleException("There is no such appointment type.");
        var location = type.Locations.SingleOrDefault(l => l.Id == locationId && l.IsActive)
            ?? throw new JourneyRuleException($"That location doesn't offer {type.Name}.");
        if (!Applies(candidate, type)) throw new JourneyRuleException($"{type.Name} doesn't apply to {candidate.FirstName}.");
        if (actor.IsCandidate && !OpenToCandidate(candidate, type, StepFor(candidate, type))) throw new JourneyRuleException(NotOpen(candidate, type));

        var window = await WindowAsync(candidate, type, ct);
        if (window.Unavailable is { } why) throw new JourneyRuleException(why);

        var date = DateOnly.FromDateTime(start);
        var slot = (await SlotsAsync(locationId, date, date, window.Earliest, window.Latest, ct)).SingleOrDefault(s => s.Start == start)
            ?? throw new JourneyRuleException("That time isn't available. Choose another.");
        if (slot.Remaining <= 0) throw new JourneyRuleException("That time was just taken. Choose another.");

        var now = clock.GetUtcNow();
        var previous = await db.Appointments.Include(a => a.Location)
            .FirstOrDefaultAsync(a => a.CandidateId == candidateId && a.AppointmentTypeId == type.Id && a.Status == AppointmentStatus.Booked, ct);
        if (previous is not null)
        {
            RequireChangeAllowed(previous, type, actor);
            previous.Status = AppointmentStatus.Cancelled;
            previous.StatusChangedAt = now;
            previous.StatusChangedBy = actor.Name;
            previous.CancelReason = "Rescheduled";
        }

        var appointment = new Appointment
        {
            CandidateId = candidateId,
            AppointmentTypeId = type.Id,
            AppointmentType = type,
            LocationId = location.Id,
            Location = location,
            Start = slot.Start,
            End = slot.End,
            Status = AppointmentStatus.Booked,
            BookedAt = now,
            BookedBy = actor.Name,
        };
        db.Appointments.Add(appointment);

        // In-person I-9 is the candidate's choice of method; a remote-representative step no longer applies.
        if (type.StepKey == "i9" && candidate.I9Method != I9Method.InPerson)
        {
            candidate.I9Method = I9Method.InPerson;
            CandidateJourney.ApplyConditions(candidate);
        }

        var step = StepFor(candidate, type);
        if (step.Status == StepStatus.Upcoming) CandidateJourney.Activate(candidate, step, now, await JourneyData.LoadDeadlinesAsync(db, ct));
        if (step.Status == StepStatus.ActionNeeded) CandidateJourney.MarkWaiting(step);

        Audit(candidateId, actor, previous is null ? "appointment.booked" : "appointment.changed", previous is null
            ? $"{type.Name} booked for {Describe(slot.Start)} at {location.Name}."
            : $"{type.Name} moved from {Describe(previous.Start)} at {previous.Location!.Name} to {Describe(slot.Start)} at {location.Name}.");

        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return (appointment, previous?.Start);
    }

    /// <summary>Cancels a booking. A candidate may cancel only their own, and only before the cutoff.</summary>
    public async Task<AppointmentSummary?> CancelAsync(
        int appointmentId, SchedulingActor actor, string? reason, int? asCandidateId = null, CancellationToken ct = default)
    {
        var appointment = await Appointments().SingleOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment is null || (asCandidateId is { } own && appointment.CandidateId != own)) return null;
        if (appointment.Status != AppointmentStatus.Booked)
        {
            throw new JourneyRuleException($"This appointment is {Describe(appointment.Status)}; only a booked one can be cancelled.");
        }
        RequireChangeAllowed(appointment, appointment.AppointmentType!, actor);

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.StatusChangedAt = clock.GetUtcNow();
        appointment.StatusChangedBy = actor.Name;
        appointment.CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        var candidate = await JourneyData.CandidatesWithJourney(db).SingleAsync(c => c.Id == appointment.CandidateId, ct);
        var step = StepFor(candidate, appointment.AppointmentType!);
        if (step.Status == StepStatus.Waiting) step.Status = StepStatus.ActionNeeded;

        Audit(appointment.CandidateId, actor, "appointment.cancelled",
            $"{appointment.AppointmentType!.Name} on {Describe(appointment.Start)} at {appointment.Location!.Name} cancelled"
            + (appointment.CancelReason is { } r ? $": {r}" : "."));
        await db.SaveChangesAsync(ct);

        var notice = await messages.SendAppointmentAsync(appointment.Id, SystemMessages.AppointmentCancelled, null, actor.Name, ct);
        return Summary(appointment) with { Notice = notice?.Notice };
    }

    /// <summary>
    /// From the roster: the candidate came, or didn't. Attendance completes the step where the type
    /// says so; a no-show sends the step back so the candidate rebooks.
    /// </summary>
    public async Task<AppointmentSummary?> MarkAttendanceAsync(int appointmentId, bool attended, StaffMember actor, CancellationToken ct = default)
    {
        var appointment = await Appointments().SingleOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment is null) return null;
        if (appointment.Status == AppointmentStatus.Cancelled) throw new JourneyRuleException("This appointment was cancelled.");
        if (DateOnly.FromDateTime(appointment.Start) > Today())
        {
            throw new JourneyRuleException("Attendance can be recorded from the day of the appointment.");
        }

        var now = clock.GetUtcNow();
        appointment.Status = attended ? AppointmentStatus.Attended : AppointmentStatus.NoShow;
        appointment.StatusChangedAt = now;
        appointment.StatusChangedBy = actor.Name;

        var type = appointment.AppointmentType!;
        var candidate = await JourneyData.CandidatesWithJourney(db).SingleAsync(c => c.Id == appointment.CandidateId, ct);
        var step = StepFor(candidate, type);
        if (attended && type.CompletesStepOnAttendance && step.Status != StepStatus.Complete)
        {
            CandidateJourney.Complete(step, now, actor);
            JourneyFlow.Advance(db, candidate, now, await JourneyData.LoadDeadlinesAsync(db, ct), actor, actor.Name);
        }
        else if (!attended && step.Status is StepStatus.Waiting or StepStatus.Complete)
        {
            if (step.Status == StepStatus.Complete) CandidateJourney.Apply(candidate, step, StepAction.Reopen, now, actor, await JourneyData.LoadDeadlinesAsync(db, ct));
            else step.Status = StepStatus.ActionNeeded;
        }

        Audit(appointment.CandidateId, new SchedulingActor(actor), attended ? "appointment.attended" : "appointment.no-show",
            $"{type.Name} on {Describe(appointment.Start)} at {appointment.Location!.Name}: {(attended ? "attended" : "did not attend")}.");
        await db.SaveChangesAsync(ct);
        return Summary(appointment);
    }

    // Availability ------------------------------------------------------------------------------

    /// <summary>Replaces the location's weekly hours. Existing bookings stay; they are commitments already made.</summary>
    public async Task<LocationAvailability?> ReplaceHoursAsync(int locationId, IReadOnlyList<HoursInput> hours, CancellationToken ct = default)
    {
        foreach (var h in hours)
        {
            if (h.End <= h.Start) throw new JourneyRuleException($"{h.Day} hours end before they start.");
            if (h.SlotMinutes is < 5 or > 480) throw new JourneyRuleException("Appointment length must be between 5 minutes and 8 hours.");
            if (h.Capacity is < 1 or > 100) throw new JourneyRuleException("Capacity must be between 1 and 100.");
            if (h.Start.AddMinutes(h.SlotMinutes) > h.End) throw new JourneyRuleException($"{h.Day} hours are shorter than one appointment.");
        }

        var location = await db.Locations.Include(l => l.Hours).SingleOrDefaultAsync(l => l.Id == locationId, ct);
        if (location is null) return null;
        location.Hours.Clear();
        location.Hours.AddRange(hours.Select(h => new WeeklyHours
        {
            Day = h.Day, Start = h.Start, End = h.End, SlotMinutes = h.SlotMinutes, Capacity = h.Capacity,
        }));
        await db.SaveChangesAsync(ct);
        return await GetAvailabilityAsync(locationId, ct);
    }

    public async Task<LocationAvailability?> AddSessionAsync(int locationId, SessionInput input, CancellationToken ct = default)
    {
        if (input.End <= input.Start) throw new JourneyRuleException("The session ends before it starts.");
        if (input.Capacity is < 1 or > 1000) throw new JourneyRuleException("Seats must be between 1 and 1,000.");
        if (!await db.Locations.AnyAsync(l => l.Id == locationId, ct)) return null;

        db.ScheduleSessions.Add(new ScheduleSession
        {
            LocationId = locationId, Start = input.Start, End = input.End, Capacity = input.Capacity,
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
        });
        await db.SaveChangesAsync(ct);
        return await GetAvailabilityAsync(locationId, ct);
    }

    /// <summary>Refused while anyone is booked into it: cancel or move them first, so nobody loses a seat silently.</summary>
    public async Task<LocationAvailability?> RemoveSessionAsync(int locationId, int sessionId, CancellationToken ct = default)
    {
        var session = await db.ScheduleSessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.LocationId == locationId, ct);
        if (session is null) return null;
        var booked = await db.Appointments.CountAsync(a => a.LocationId == locationId && a.Start == session.Start && a.Status == AppointmentStatus.Booked, ct);
        if (booked > 0) throw new JourneyRuleException($"{Plural(booked, "candidate is", "candidates are")} booked into this session. Cancel or move them first.");

        db.ScheduleSessions.Remove(session);
        await db.SaveChangesAsync(ct);
        return await GetAvailabilityAsync(locationId, ct);
    }

    /// <summary>Refused while anyone is booked that day, for the same reason as removing a session.</summary>
    public async Task<LocationAvailability?> AddClosureAsync(int locationId, ClosureInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Reason)) throw new JourneyRuleException("Say why the location is closed.");
        if (!await db.Locations.AnyAsync(l => l.Id == locationId, ct)) return null;
        if (await db.Closures.AnyAsync(c => c.LocationId == locationId && c.Date == input.Date, ct))
        {
            throw new JourneyRuleException("That day is already closed.");
        }

        var dayStart = input.Date.ToDateTime(TimeOnly.MinValue);
        var booked = await db.Appointments.CountAsync(a => a.LocationId == locationId && a.Start >= dayStart && a.Start < dayStart.AddDays(1) && a.Status == AppointmentStatus.Booked, ct);
        if (booked > 0) throw new JourneyRuleException($"{Plural(booked, "candidate is", "candidates are")} booked that day. Cancel or move them first.");

        db.Closures.Add(new Closure { LocationId = locationId, Date = input.Date, Reason = input.Reason.Trim() });
        await db.SaveChangesAsync(ct);
        return await GetAvailabilityAsync(locationId, ct);
    }

    public async Task<LocationAvailability?> RemoveClosureAsync(int locationId, int closureId, CancellationToken ct = default)
    {
        var closure = await db.Closures.SingleOrDefaultAsync(c => c.Id == closureId && c.LocationId == locationId, ct);
        if (closure is null) return null;
        db.Closures.Remove(closure);
        await db.SaveChangesAsync(ct);
        return await GetAvailabilityAsync(locationId, ct);
    }

    // Helpers -----------------------------------------------------------------------------------

    private sealed record Window(DateTime Earliest, DateOnly? Latest, string? Unavailable);

    /// <summary>The candidate's booking window for this type: the notice period, then the type's rules.</summary>
    private async Task<Window> WindowAsync(Candidate candidate, AppointmentType type, CancellationToken ct)
    {
        var deadlines = await JourneyData.LoadDeadlinesAsync(db, ct);
        var earliest = BusinessCalendar.ToPacific(clock.GetUtcNow()).AddHours(type.MinNoticeHours);

        var needsStart = type.Earliest.Anchor != DeadlineAnchor.None || type.Latest.Anchor != DeadlineAnchor.None;
        if (needsStart && candidate.AnticipatedStartDate is null)
        {
            return new Window(earliest, null, $"{type.Name} can be booked once {candidate.FirstName} has an anticipated start date.");
        }

        if (deadlines.Compute(type.Earliest, null, candidate.AnticipatedStartDate) is { } first && first.Date.ToDateTime(TimeOnly.MinValue) > earliest)
        {
            earliest = first.Date.ToDateTime(TimeOnly.MinValue);
        }
        var latest = deadlines.Compute(type.Latest, null, candidate.AnticipatedStartDate)?.Date;

        return latest is { } last && last < DateOnly.FromDateTime(earliest)
            ? new Window(earliest, latest, $"The window for {type.Name} closed on {last:dddd, MMMM d}. Contact the onboarding specialist.")
            : new Window(earliest, latest, null);
    }

    private async Task<IReadOnlyList<Slot>> SlotsAsync(
        int locationId, DateOnly from, DateOnly to, DateTime earliest, DateOnly? latest, CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.ToDateTime(TimeOnly.MinValue).AddDays(1);
        var hours = await db.WeeklyHours.Where(h => h.LocationId == locationId).ToListAsync(ct);
        var sessions = await db.ScheduleSessions.Where(s => s.LocationId == locationId && s.Start >= start && s.Start < end).ToListAsync(ct);
        var closures = await db.Closures.Where(c => c.LocationId == locationId && c.Date >= from && c.Date <= to).Select(c => c.Date).ToListAsync(ct);
        var holidays = await db.Holidays.Where(h => h.Date >= from && h.Date <= to).Select(h => h.Date).ToListAsync(ct);
        var booked = (await db.Appointments
                .Where(a => a.LocationId == locationId && a.Start >= start && a.Start < end && a.Status != AppointmentStatus.Cancelled)
                .Select(a => a.Start)
                .ToListAsync(ct))
            .GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());

        return SlotEngine.Generate(hours, sessions, closures, holidays, booked, from, to, earliest, latest);
    }

    private void RequireChangeAllowed(Appointment appointment, AppointmentType type, SchedulingActor actor)
    {
        if (!actor.IsCandidate) return;
        var cutoff = appointment.Start.AddHours(-type.ChangeCutoffHours);
        if (BusinessCalendar.ToPacific(clock.GetUtcNow()) >= cutoff)
        {
            throw new JourneyRuleException(
                $"Appointments can be changed online until {type.ChangeCutoffHours} hours before. Contact your onboarding specialist to change this one.");
        }
    }

    /// <summary>
    /// A candidate books only a step they've been asked to do (action needed), or changes one already
    /// booked (waiting). Staff may book ahead; booking starts the step. Step 8: a candidate who chose a
    /// remote I-9 isn't offered the in-person desk; the choice is <see cref="Portal.PortalAnswers"/>, and only staff undo it.
    /// </summary>
    private static bool OpenToCandidate(Candidate candidate, AppointmentType type, CandidateStep step) =>
        step.Status is StepStatus.ActionNeeded or StepStatus.Waiting
        && !(type.StepKey == "i9" && candidate.I9Method == I9Method.Remote);

    private static string NotOpen(Candidate candidate, AppointmentType type) =>
        type.StepKey == "i9" && candidate.I9Method == I9Method.Remote
            ? "You chose remote verification. To verify in person instead, contact your onboarding specialist."
            : $"You can book {type.Name} once we ask you to. We'll email you when it's time.";

    private static bool Applies(Candidate candidate, AppointmentType type) =>
        candidate.Steps.FirstOrDefault(s => s.Step!.Key == type.StepKey) is { Status: not StepStatus.Skipped };

    private static CandidateStep StepFor(Candidate candidate, AppointmentType type) =>
        candidate.Steps.Single(s => s.Step!.Key == type.StepKey);

    private IQueryable<Appointment> Appointments() =>
        db.Appointments.Include(a => a.AppointmentType).Include(a => a.Location).Include(a => a.Candidate);

    private async Task<List<int>> TeamIdsAsync(StaffMember actor, CancellationToken ct) =>
        await db.Set<TeamMembership>().Where(m => m.StaffMemberId == actor.Id).Select(m => m.TeamId).ToListAsync(ct);

    private DateOnly Today() => DateOnly.FromDateTime(BusinessCalendar.ToPacific(clock.GetUtcNow()));

    private static AppointmentSummary Summary(Appointment a) => new(
        a.Id, a.AppointmentType!.Key, a.AppointmentType.Name, a.LocationId, a.Location!.Name, a.Location.Address,
        a.Start, a.End, a.Status, a.BookedBy, a.BookedAt, a.CancelReason, a.Start.AddHours(-a.AppointmentType.ChangeCutoffHours));

    /// <summary>"Wednesday, October 28 at 9:15 a.m."</summary>
    public static string Describe(DateTime start) =>
        $"{start:dddd, MMMM d} at {start:h:mm} {(start.Hour < 12 ? "a.m." : "p.m.")}";

    private static string Describe(AppointmentStatus status) => status switch
    {
        AppointmentStatus.NoShow => "marked as a no-show",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    private void Audit(int candidateId, SchedulingActor actor, string action, string detail) =>
        db.AuditEvents.Add(new AuditEvent
        {
            CandidateId = candidateId,
            At = clock.GetUtcNow(),
            ActorStaffId = actor.Staff?.Id,
            ActorName = actor.IsCandidate ? "Candidate" : actor.Name,
            Action = action,
            Detail = detail,
        });
}
