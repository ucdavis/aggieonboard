import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useId, useState } from 'react';
import {
  addDays,
  appointmentStatusBadge,
  appointmentStatusLabel,
  errorMessage,
  formatClock,
  formatDate,
  pacificToday,
} from '@/lib/journey.ts';
import {
  type RosterEntry,
  rosterQueryOptions,
  scheduleLocationsQueryOptions,
  useMarkAttendance,
  useRosterCancel,
} from '@/queries/schedule.ts';
import { useUser } from '@/shared/auth/UserContext.tsx';

type ScheduleSearch = { date?: string; location?: number };

export const Route = createFileRoute('/(authenticated)/schedule/')({
  component: SchedulePage,
  validateSearch: (search: Record<string, unknown>): ScheduleSearch => ({
    date:
      typeof search.date === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(search.date)
        ? search.date
        : undefined,
    location: Number(search.location) > 0 ? Number(search.location) : undefined,
  }),
});

/** Who is coming, when and where, for the locations this person's teams run. */
function SchedulePage() {
  const today = pacificToday();
  const { date = today, location } = Route.useSearch();
  const navigate = Route.useNavigate();
  const locations = useQuery(scheduleLocationsQueryOptions());
  const roster = useQuery(rosterQueryOptions(date, location));
  const dateId = useId();
  const locationId = useId();

  const go = (next: ScheduleSearch) =>
    navigate({ search: (prev: ScheduleSearch) => ({ ...prev, ...next }) });
  const total =
    roster.data?.reduce((n, l) => n + l.appointments.length, 0) ?? 0;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold">Schedule</h1>
          <p className="mt-1 text-base-content/70">
            {formatDate(date)}
            {date === today && ' (today)'}
            {roster.data && ` · ${total} appointment${total === 1 ? '' : 's'}`}
          </p>
        </div>
        <div className="flex flex-wrap items-end gap-2">
          <div className="join">
            <button
              className="btn join-item btn-sm"
              onClick={() => go({ date: addDays(date, -1) })}
              type="button"
            >
              ← Previous
            </button>
            <button
              className="btn join-item btn-sm"
              disabled={date === today}
              onClick={() => go({ date: undefined })}
              type="button"
            >
              Today
            </button>
            <button
              className="btn join-item btn-sm"
              onClick={() => go({ date: addDays(date, 1) })}
              type="button"
            >
              Next →
            </button>
          </div>
          <label className="sr-only" htmlFor={dateId}>
            Date
          </label>
          <input
            className="input input-bordered input-sm"
            id={dateId}
            onChange={(e) => e.target.value && go({ date: e.target.value })}
            type="date"
            value={date}
          />
          <label className="sr-only" htmlFor={locationId}>
            Location
          </label>
          <select
            className="select select-bordered select-sm"
            id={locationId}
            onChange={(e) =>
              go({
                location: e.target.value ? Number(e.target.value) : undefined,
              })
            }
            value={location ?? ''}
          >
            <option value="">All my locations</option>
            {locations.data?.map((l) => (
              <option key={l.id} value={l.id}>
                {l.appointmentTypeName}: {l.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      {roster.isPending ? (
        <div className="flex justify-center py-12">
          <span className="loading loading-spinner loading-lg" />
        </div>
      ) : roster.error ? (
        <div className="alert alert-error" role="alert">
          {errorMessage(roster.error)}
        </div>
      ) : roster.data.length === 0 ? (
        <p className="rounded-box bg-base-100 p-6 text-base-content/70">
          You aren&apos;t on a team that runs appointments.
        </p>
      ) : (
        <div className="space-y-6">
          {roster.data.map((l) => (
            <section
              aria-labelledby={`location-${l.locationId}`}
              className="card bg-base-100 shadow-sm"
              key={l.locationId}
            >
              <div className="card-body">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div>
                    <h2 className="card-title" id={`location-${l.locationId}`}>
                      {l.name}
                    </h2>
                    <p className="text-sm text-base-content/70">
                      {l.address ?? 'Address to be confirmed'} · {l.teamName}
                    </p>
                  </div>
                  <Link
                    className="btn btn-ghost btn-sm"
                    params={{ locationId: String(l.locationId) }}
                    to="/schedule/locations/$locationId"
                  >
                    Hours and sessions
                  </Link>
                </div>
                {l.appointments.length === 0 ? (
                  <p className="text-base-content/70">No one is booked.</p>
                ) : (
                  <ul className="divide-y divide-base-200">
                    {l.appointments.map((a) => (
                      <RosterRow
                        entry={a}
                        isPast={date <= today}
                        key={a.appointmentId}
                      />
                    ))}
                  </ul>
                )}
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}

function RosterRow({ entry, isPast }: { entry: RosterEntry; isPast: boolean }) {
  const user = useUser();
  const attendance = useMarkAttendance();
  const cancel = useRosterCancel();
  const [cancelling, setCancelling] = useState(false);
  const [reason, setReason] = useState('');
  const reasonId = useId();
  const error = attendance.error ?? cancel.error;
  const pending = attendance.isPending || cancel.isPending;

  return (
    <li className="space-y-2 py-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-start gap-4">
          <div className="w-24 shrink-0 font-semibold tabular-nums">
            {formatClock(entry.start)}
            <div className="text-xs font-normal text-base-content/60">
              to {formatClock(entry.end)}
            </div>
          </div>
          <div>
            {user.roles.includes('Staff') ? (
              <Link
                className="link link-hover font-semibold"
                params={{ candidateId: String(entry.candidateId) }}
                to="/candidates/$candidateId"
              >
                {entry.candidateName}
              </Link>
            ) : (
              <span className="font-semibold">{entry.candidateName}</span>
            )}
            <div className="text-sm text-base-content/70">
              {entry.positionTitle} · {entry.department}
            </div>
            <div className="text-xs text-base-content/60">
              Booked by {entry.bookedBy}
            </div>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <span className={`badge ${appointmentStatusBadge[entry.status]}`}>
            {appointmentStatusLabel[entry.status]}
          </span>
          {isPast && entry.status !== 'Attended' && (
            <button
              className="btn btn-success btn-sm"
              disabled={pending}
              onClick={() =>
                attendance.mutate({ attended: true, id: entry.appointmentId })
              }
              type="button"
            >
              Attended
            </button>
          )}
          {isPast && entry.status !== 'NoShow' && (
            <button
              className="btn btn-outline btn-sm"
              disabled={pending}
              onClick={() =>
                attendance.mutate({ attended: false, id: entry.appointmentId })
              }
              type="button"
            >
              No-show
            </button>
          )}
          {entry.status === 'Booked' && !cancelling && (
            <button
              className="btn btn-ghost btn-sm text-error"
              onClick={() => setCancelling(true)}
              type="button"
            >
              Cancel
            </button>
          )}
        </div>
      </div>
      {cancelling && (
        <form
          className="flex flex-wrap items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            cancel.mutate(
              { id: entry.appointmentId, reason },
              { onSuccess: () => setCancelling(false) }
            );
          }}
        >
          <div className="form-control grow">
            <label className="label py-1 text-sm" htmlFor={reasonId}>
              Reason (recorded in the candidate&apos;s history)
            </label>
            <input
              className="input input-bordered input-sm"
              id={reasonId}
              onChange={(e) => setReason(e.target.value)}
              value={reason}
            />
          </div>
          <button
            className="btn btn-error btn-sm"
            disabled={pending}
            type="submit"
          >
            Cancel appointment
          </button>
          <button
            className="btn btn-ghost btn-sm"
            onClick={() => setCancelling(false)}
            type="button"
          >
            Keep it
          </button>
        </form>
      )}
      {error && (
        <div className="alert alert-error py-2 text-sm" role="alert">
          {errorMessage(error)}
        </div>
      )}
    </li>
  );
}
