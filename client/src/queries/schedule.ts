import {
  queryOptions,
  useMutation,
  useQueryClient,
} from '@tanstack/react-query';
import { fetchJson } from '../lib/api.ts';

// Shapes of /api/schedule and the booking endpoints on /api/staff (server.core/Scheduling).
// Times are Pacific wall-clock strings without an offset ("2026-10-27T09:15:00"): never pass them
// through Date, which would shift them into the browser's time zone.

export type AppointmentStatus = 'Booked' | 'Attended' | 'NoShow' | 'Cancelled';
export type Weekday =
  | 'Sunday'
  | 'Monday'
  | 'Tuesday'
  | 'Wednesday'
  | 'Thursday'
  | 'Friday'
  | 'Saturday';

export type LocationSummary = {
  address: string | null;
  appointmentTypeKey: string;
  appointmentTypeName: string;
  id: number;
  name: string;
  teamName: string;
};

export type RosterEntry = {
  appointmentId: number;
  bookedBy: string;
  candidateId: number;
  candidateName: string;
  department: string;
  end: string;
  positionTitle: string;
  start: string;
  status: AppointmentStatus;
  typeName: string;
};

export type RosterLocation = {
  address: string | null;
  appointments: RosterEntry[];
  locationId: number;
  name: string;
  teamName: string;
};

export type Hours = {
  capacity: number;
  day: Weekday;
  end: string;
  slotMinutes: number;
  start: string;
};

export type Session = {
  booked: number;
  capacity: number;
  end: string;
  id: number;
  note: string | null;
  start: string;
};

export type ClosureView = {
  booked: number;
  date: string;
  id: number;
  reason: string;
};

export type LocationAvailability = {
  closures: ClosureView[];
  hours: Hours[];
  location: LocationSummary;
  sessions: Session[];
};

export type AppointmentSummary = {
  bookedAt: string;
  bookedBy: string;
  cancelReason: string | null;
  changeCutoff: string;
  end: string;
  id: number;
  locationAddress: string | null;
  locationId: number;
  locationName: string;
  start: string;
  status: AppointmentStatus;
  typeKey: string;
  typeName: string;
};

export type CandidateAppointmentType = {
  current: AppointmentSummary | null;
  past: AppointmentSummary[];
  stepKey: string;
  typeKey: string;
  typeName: string;
};

export type Slot = {
  capacity: number;
  end: string;
  remaining: number;
  start: string;
};

export type BookingOptions = {
  current: AppointmentSummary | null;
  earliest: string;
  latest: string | null;
  locations: {
    address: string | null;
    locationId: number;
    name: string;
    slots: Slot[];
  }[];
  typeKey: string;
  typeName: string;
  unavailable: string | null;
};

export const scheduleKeys = {
  all: ['schedule'] as const,
  availability: (locationId: number) =>
    ['schedule', 'availability', locationId] as const,
  locations: () => ['schedule', 'locations'] as const,
  roster: (date: string, locationId?: number) =>
    ['schedule', 'roster', date, locationId ?? null] as const,
};

export const scheduleLocationsQueryOptions = () =>
  queryOptions({
    queryFn: () => fetchJson<LocationSummary[]>('/api/schedule/locations'),
    queryKey: scheduleKeys.locations(),
  });

export const rosterQueryOptions = (date: string, locationId?: number) =>
  queryOptions({
    queryFn: () =>
      fetchJson<RosterLocation[]>(
        `/api/schedule/roster?date=${date}${locationId ? `&locationId=${locationId}` : ''}`
      ),
    queryKey: scheduleKeys.roster(date, locationId),
  });

export const availabilityQueryOptions = (locationId: number) =>
  queryOptions({
    queryFn: () =>
      fetchJson<LocationAvailability>(
        `/api/schedule/locations/${locationId}/availability`
      ),
    queryKey: scheduleKeys.availability(locationId),
  });

export const candidateAppointmentsQueryOptions = (candidateId: number) =>
  queryOptions({
    queryFn: () =>
      fetchJson<CandidateAppointmentType[]>(
        `/api/staff/candidates/${candidateId}/appointments`
      ),
    queryKey: ['staff', 'candidates', candidateId, 'appointments'] as const,
  });

export const bookingOptionsQueryOptions = (
  candidateId: number,
  typeKey: string,
  from: string,
  to: string
) =>
  queryOptions({
    queryFn: () =>
      fetchJson<BookingOptions>(
        `/api/staff/candidates/${candidateId}/appointments/${typeKey}/options?from=${from}&to=${to}`
      ),
    queryKey: [
      'staff',
      'candidates',
      candidateId,
      'options',
      typeKey,
      from,
      to,
    ] as const,
    staleTime: 0,
  });

const json = (method: string, body?: unknown) => ({
  body: body === undefined ? undefined : JSON.stringify(body),
  method,
});

/** Any schedule change can move a candidate's step, so the candidate and pipeline refresh too. */
const useScheduleMutation = <TVariables, TResult>(
  request: (variables: TVariables) => Promise<TResult>
) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: request,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: scheduleKeys.all });
      void queryClient.invalidateQueries({ queryKey: ['staff'] });
    },
  });
};

export const useMarkAttendance = () =>
  useScheduleMutation(({ attended, id }: { attended: boolean; id: number }) =>
    fetchJson<AppointmentSummary>(
      `/api/schedule/appointments/${id}/attendance`,
      json('POST', { attended })
    )
  );

export const useRosterCancel = () =>
  useScheduleMutation(({ id, reason }: { id: number; reason: string }) =>
    fetchJson<AppointmentSummary>(
      `/api/schedule/appointments/${id}/cancel`,
      json('POST', { reason })
    )
  );

export const useSaveHours = (locationId: number) =>
  useScheduleMutation((hours: Hours[]) =>
    fetchJson<LocationAvailability>(
      `/api/schedule/locations/${locationId}/hours`,
      json('PUT', hours)
    )
  );

export const useAddSession = (locationId: number) =>
  useScheduleMutation(
    (session: {
      capacity: number;
      end: string;
      note: string | null;
      start: string;
    }) =>
      fetchJson<LocationAvailability>(
        `/api/schedule/locations/${locationId}/sessions`,
        json('POST', session)
      )
  );

export const useRemoveSession = (locationId: number) =>
  useScheduleMutation((sessionId: number) =>
    fetchJson<LocationAvailability>(
      `/api/schedule/locations/${locationId}/sessions/${sessionId}`,
      json('DELETE')
    )
  );

export const useAddClosure = (locationId: number) =>
  useScheduleMutation((closure: { date: string; reason: string }) =>
    fetchJson<LocationAvailability>(
      `/api/schedule/locations/${locationId}/closures`,
      json('POST', closure)
    )
  );

export const useRemoveClosure = (locationId: number) =>
  useScheduleMutation((closureId: number) =>
    fetchJson<LocationAvailability>(
      `/api/schedule/locations/${locationId}/closures/${closureId}`,
      json('DELETE')
    )
  );

export const useBookForCandidate = (candidateId: number) =>
  useScheduleMutation(
    (booking: { locationId: number; start: string; typeKey: string }) =>
      fetchJson<AppointmentSummary>(
        `/api/staff/candidates/${candidateId}/appointments`,
        json('POST', booking)
      )
  );

export const useCancelForCandidate = (candidateId: number) =>
  useScheduleMutation(({ id, reason }: { id: number; reason: string }) =>
    fetchJson<AppointmentSummary>(
      `/api/staff/candidates/${candidateId}/appointments/${id}/cancel`,
      json('POST', { reason })
    )
  );
