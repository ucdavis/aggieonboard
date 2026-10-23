using Server.Core.Domain;

namespace Server.Core.Scheduling;

// The schedule's API shapes. Times are Pacific wall-clock ("2026-10-28T09:15:00").

public sealed record LocationSummary(
    int Id, string Name, string? Address, string AppointmentTypeKey, string AppointmentTypeName, string TeamName);

public sealed record AppointmentSummary(
    int Id,
    string TypeKey,
    string TypeName,
    int LocationId,
    string LocationName,
    string? LocationAddress,
    DateTime Start,
    DateTime End,
    AppointmentStatus Status,
    string BookedBy,
    DateTimeOffset BookedAt,
    string? CancelReason,
    DateTime ChangeCutoff);

public sealed record LocationSlots(int LocationId, string Name, string? Address, IReadOnlyList<Slot> Slots);

/// <summary>What a candidate can book for one appointment type, over a range of days.</summary>
public sealed record BookingOptions(
    string TypeKey,
    string TypeName,
    DateTime Earliest,
    DateOnly? Latest,
    string? Unavailable,
    AppointmentSummary? Current,
    IReadOnlyList<LocationSlots> Locations);

/// <summary>An appointment type as it applies to one candidate, for the candidate page and the portal.</summary>
public sealed record CandidateAppointmentType(
    string TypeKey, string TypeName, string StepKey, AppointmentSummary? Current, IReadOnlyList<AppointmentSummary> Past);

public sealed record RosterEntry(
    int AppointmentId,
    DateTime Start,
    DateTime End,
    AppointmentStatus Status,
    int CandidateId,
    string CandidateName,
    string PositionTitle,
    string Department,
    string TypeName,
    string BookedBy);

public sealed record RosterLocation(int LocationId, string Name, string? Address, string TeamName, IReadOnlyList<RosterEntry> Appointments);

public sealed record HoursInput(DayOfWeek Day, TimeOnly Start, TimeOnly End, int SlotMinutes, int Capacity);

public sealed record SessionInput(DateTime Start, DateTime End, int Capacity, string? Note);

public sealed record SessionView(int Id, DateTime Start, DateTime End, int Capacity, int Booked, string? Note);

public sealed record ClosureInput(DateOnly Date, string Reason);

public sealed record ClosureView(int Id, DateOnly Date, string Reason, int Booked);

public sealed record LocationAvailability(
    LocationSummary Location,
    IReadOnlyList<HoursInput> Hours,
    IReadOnlyList<SessionView> Sessions,
    IReadOnlyList<ClosureView> Closures);

/// <summary>Who is booking or changing: a staff member, or the candidate themselves (5b).</summary>
public sealed record SchedulingActor(StaffMember? Staff)
{
    public static readonly SchedulingActor Candidate = new((StaffMember?)null);
    public bool IsCandidate => Staff is null;
    public string Name => Staff?.Name ?? "Candidate";
}
