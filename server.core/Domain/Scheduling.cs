namespace Server.Core.Domain;

/// <summary>
/// A group that runs appointments: the SSO I-9 desk, a police department, Employee Health,
/// Learning and Development. Its members manage its locations' hours and see their day rosters.
/// </summary>
public class Team
{
    public int Id { get; set; }

    /// <summary>Stable identifier ("health-pd"). Unique.</summary>
    public string Key { get; set; } = "";

    public string Name { get; set; } = "";
    public List<TeamMembership> Members { get; set; } = [];
}

public class TeamMembership
{
    public int TeamId { get; set; }
    public Team? Team { get; set; }
    public int StaffMemberId { get; set; }
    public StaffMember? StaffMember { get; set; }
}

/// <summary>
/// Something a candidate books: in-person I-9, fingerprinting, Employee Health, New Employee
/// Welcome. Its window is data, because the audit states some of it and SSO has yet to confirm the rest.
/// </summary>
public class AppointmentType
{
    public int Id { get; set; }

    /// <summary>Stable identifier ("fingerprinting"). Unique.</summary>
    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>The journey step this appointment fulfils (<see cref="StepDefinition.Key"/>).</summary>
    public string StepKey { get; set; } = "";

    /// <summary>
    /// The earliest an appointment may be, besides <see cref="MinNoticeHours"/>: New Employee Welcome
    /// is on or after the start date. <see cref="DeadlineRule.None"/> when only the notice applies.
    /// </summary>
    public DeadlineRule Earliest { get; set; } = DeadlineRule.None;

    /// <summary>The last day an appointment may be on: the clearance deadline, two weeks before start…</summary>
    public DeadlineRule Latest { get; set; } = DeadlineRule.None;

    /// <summary>How far ahead a booking must be made, so the location can prepare.</summary>
    public int MinNoticeHours { get; set; } = 24;

    /// <summary>A candidate may change or cancel until this many hours before. Staff may always.</summary>
    public int ChangeCutoffHours { get; set; } = 24;

    /// <summary>Marking the candidate attended completes the step (I-9, NEW); otherwise a result still follows.</summary>
    public bool CompletesStepOnAttendance { get; set; }

    public bool WindowConfirmed { get; set; }
    public string? WindowQuestion { get; set; }

    public List<Location> Locations { get; set; } = [];
}

/// <summary>Where an appointment type happens, and which team runs it there.</summary>
public class Location
{
    public int Id { get; set; }
    public int AppointmentTypeId { get; set; }
    public AppointmentType? AppointmentType { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Null until someone confirms it; the candidate is told the place by name only.</summary>
    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;

    public List<WeeklyHours> Hours { get; set; } = [];
}

/// <summary>Recurring availability: every <see cref="Day"/>, slots of <see cref="SlotMinutes"/> from start to end, Pacific time.</summary>
public class WeeklyHours
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public DayOfWeek Day { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public int SlotMinutes { get; set; }

    /// <summary>How many candidates one slot takes (stations, staff on duty).</summary>
    public int Capacity { get; set; } = 1;
}

/// <summary>A dated block with seats: a New Employee Welcome session, or extra hours. Pacific time.</summary>
public class ScheduleSession
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public int Capacity { get; set; }
    public string? Note { get; set; }
}

/// <summary>A day a location takes no appointments. Holidays close every location without one.</summary>
public class Closure
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public DateOnly Date { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>One candidate's booking. Kept when cancelled, for the history.</summary>
public class Appointment
{
    public int Id { get; set; }
    public int CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public int AppointmentTypeId { get; set; }
    public AppointmentType? AppointmentType { get; set; }
    public int LocationId { get; set; }
    public Location? Location { get; set; }

    /// <summary>Pacific wall-clock time, like every schedule time in the app.</summary>
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    public AppointmentStatus Status { get; set; }

    public DateTimeOffset BookedAt { get; set; }

    /// <summary>A staff member's name, or "Candidate" when they booked it themselves.</summary>
    public string BookedBy { get; set; } = "";

    public DateTimeOffset? StatusChangedAt { get; set; }
    public string? StatusChangedBy { get; set; }
    public string? CancelReason { get; set; }

    /// <summary>When the day-before reminder went out (5b). Null until then.</summary>
    public DateTimeOffset? ReminderSentAt { get; set; }

    public bool IsActive => Status == AppointmentStatus.Booked;
}

public enum AppointmentStatus
{
    Booked,
    Attended,
    NoShow,
    Cancelled,
}
