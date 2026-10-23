import { useQuery } from '@tanstack/react-query';
import { useId, useState } from 'react';
import {
  addDays,
  appointmentStatusBadge,
  appointmentStatusLabel,
  errorMessage,
  formatAppointment,
  formatClock,
  formatDate,
  formatDay,
  pacificToday,
} from '@/lib/journey.ts';
import {
  type AppointmentSummary,
  bookingOptionsQueryOptions,
  candidateAppointmentsQueryOptions,
  type CandidateAppointmentType,
  type Slot,
  useBookForCandidate,
  useCancelForCandidate,
} from '@/queries/schedule.ts';
import { Dialog } from './Dialog.tsx';

/** The candidate's appointments, booked or changed here on their behalf. */
export function Appointments({ candidateId }: { candidateId: number }) {
  const { data, error } = useQuery(
    candidateAppointmentsQueryOptions(candidateId)
  );
  const [booking, setBooking] = useState<CandidateAppointmentType | null>(null);

  if (error) {
    return (
      <div className="alert alert-error" role="alert">
        {errorMessage(error)}
      </div>
    );
  }
  if (!data || data.length === 0) {
    return null;
  }

  return (
    <section aria-labelledby="appointments-heading" className="space-y-3">
      <h3 className="text-lg font-semibold" id="appointments-heading">
        Appointments
      </h3>
      <ul className="space-y-3">
        {data.map((type) => (
          <AppointmentRow
            candidateId={candidateId}
            key={type.typeKey}
            onBook={() => setBooking(type)}
            type={type}
          />
        ))}
      </ul>
      {booking && (
        <BookingDialog
          candidateId={candidateId}
          current={booking.current}
          onClose={() => setBooking(null)}
          typeKey={booking.typeKey}
          typeName={booking.typeName}
        />
      )}
    </section>
  );
}

function AppointmentRow({
  candidateId,
  onBook,
  type,
}: {
  candidateId: number;
  onBook: () => void;
  type: CandidateAppointmentType;
}) {
  const cancel = useCancelForCandidate(candidateId);
  const [cancelling, setCancelling] = useState(false);
  const [reason, setReason] = useState('');
  const reasonId = useId();
  const current = type.current;

  return (
    <li
      aria-label={type.typeName}
      className="rounded-box border border-base-300 bg-base-100 p-4"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <p className="font-semibold">{type.typeName}</p>
          {current ? (
            <AppointmentLine appointment={current} />
          ) : (
            <p className="text-sm text-base-content/70">Not booked.</p>
          )}
        </div>
        <div className="flex gap-2">
          <button
            className="btn btn-sm btn-primary btn-outline"
            onClick={onBook}
            type="button"
          >
            {current ? 'Change' : 'Book'}
          </button>
          {current && !cancelling && (
            <button
              className="btn btn-sm btn-ghost text-error"
              onClick={() => setCancelling(true)}
              type="button"
            >
              Cancel
            </button>
          )}
        </div>
      </div>
      {cancelling && current && (
        <form
          className="mt-3 flex flex-wrap items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            cancel.mutate(
              { id: current.id, reason },
              { onSuccess: () => setCancelling(false) }
            );
          }}
        >
          <div className="form-control grow">
            <label className="label py-1 text-sm" htmlFor={reasonId}>
              Reason (recorded in the history)
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
            disabled={cancel.isPending}
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
      {cancel.error && (
        <div className="alert alert-error mt-3 py-2 text-sm" role="alert">
          {errorMessage(cancel.error)}
        </div>
      )}
      {type.past.length > 0 && (
        <details className="mt-3 text-sm">
          <summary className="cursor-pointer text-base-content/70">
            Earlier bookings ({type.past.length})
          </summary>
          <ul className="mt-2 space-y-1">
            {type.past.map((a) => (
              <li key={a.id}>
                <AppointmentLine appointment={a} />
              </li>
            ))}
          </ul>
        </details>
      )}
    </li>
  );
}

function AppointmentLine({
  appointment: a,
}: {
  appointment: AppointmentSummary;
}) {
  return (
    <p className="text-sm">
      {formatAppointment(a.start, a.end)} · {a.locationName}{' '}
      <span className={`badge badge-sm ${appointmentStatusBadge[a.status]}`}>
        {appointmentStatusLabel[a.status]}
      </span>
      <span className="block text-xs text-base-content/60">
        Booked by {a.bookedBy}
        {a.cancelReason && ` · ${a.cancelReason}`}
      </span>
    </p>
  );
}

const span = 13;

/** Open times in the candidate's window, two weeks at a time, grouped by day. */
function BookingDialog({
  candidateId,
  current,
  onClose,
  typeKey,
  typeName,
}: {
  candidateId: number;
  current: AppointmentSummary | null;
  onClose: () => void;
  typeKey: string;
  typeName: string;
}) {
  const [from, setFrom] = useState(pacificToday());
  const to = addDays(from, span);
  const options = useQuery(
    bookingOptionsQueryOptions(candidateId, typeKey, from, to)
  );
  const book = useBookForCandidate(candidateId);
  const [locationId, setLocationId] = useState<number | null>(null);
  const [chosen, setChosen] = useState<Slot | null>(null);
  const locationSelectId = useId();

  const data = options.data;
  const location =
    data?.locations.find((l) => l.locationId === locationId) ??
    data?.locations[0];
  const byDay = new Map<string, Slot[]>();
  for (const slot of location?.slots ?? []) {
    const day = slot.start.slice(0, 10);
    byDay.set(day, [...(byDay.get(day) ?? []), slot]);
  }

  return (
    <Dialog
      onClose={onClose}
      title={`${current ? 'Change' : 'Book'} ${typeName}`}
    >
      {options.error ? (
        <div className="alert alert-error" role="alert">
          {errorMessage(options.error)}
        </div>
      ) : !data ? (
        <div className="flex justify-center py-12">
          <span className="loading loading-spinner loading-lg" />
        </div>
      ) : data.unavailable ? (
        <div className="alert alert-warning" role="status">
          {data.unavailable}
        </div>
      ) : (
        <div className="space-y-4">
          {current && (
            <p className="text-sm">
              Currently {formatAppointment(current.start, current.end)} at{' '}
              {current.locationName}. Booking a new time replaces it.
            </p>
          )}
          {data.latest && (
            <p className="text-sm text-base-content/70">
              Must be on or before {formatDate(data.latest)}.
            </p>
          )}
          {data.locations.length > 1 && (
            <div className="form-control max-w-md">
              <label className="label py-1 text-sm" htmlFor={locationSelectId}>
                Location
              </label>
              <select
                className="select select-bordered select-sm"
                id={locationSelectId}
                onChange={(e) => {
                  setLocationId(Number(e.target.value));
                  setChosen(null);
                }}
                value={location?.locationId}
              >
                {data.locations.map((l) => (
                  <option key={l.locationId} value={l.locationId}>
                    {l.name}
                  </option>
                ))}
              </select>
            </div>
          )}
          <div className="flex items-center justify-between gap-2">
            <button
              className="btn btn-ghost btn-sm"
              disabled={from <= pacificToday()}
              onClick={() => setFrom(addDays(from, -(span + 1)))}
              type="button"
            >
              ← Earlier
            </button>
            <span className="text-sm font-medium">
              {formatDate(from)} – {formatDate(to)}
            </span>
            <button
              className="btn btn-ghost btn-sm"
              onClick={() => setFrom(addDays(from, span + 1))}
              type="button"
            >
              Later →
            </button>
          </div>
          {byDay.size === 0 ? (
            <p className="rounded-box bg-base-200 p-4 text-sm">
              No open times in these two weeks
              {location ? ` at ${location.name}` : ''}.
            </p>
          ) : (
            <div className="max-h-80 space-y-3 overflow-y-auto">
              {[...byDay].map(([day, slots]) => (
                <fieldset key={day}>
                  <legend className="mb-1 text-sm font-semibold">
                    {formatDate(day)}
                  </legend>
                  <div className="flex flex-wrap gap-2">
                    {slots.map((slot) => (
                      <button
                        aria-pressed={chosen?.start === slot.start}
                        className={`btn btn-sm ${chosen?.start === slot.start ? 'btn-primary' : 'btn-outline'}`}
                        key={slot.start}
                        onClick={() => setChosen(slot)}
                        type="button"
                      >
                        {formatClock(slot.start)}
                      </button>
                    ))}
                  </div>
                </fieldset>
              ))}
            </div>
          )}
          {book.error && (
            <div className="alert alert-error text-sm" role="alert">
              {errorMessage(book.error)}
            </div>
          )}
          <div className="flex justify-end gap-2">
            <button className="btn btn-ghost" onClick={onClose} type="button">
              Close
            </button>
            <button
              className="btn btn-primary"
              disabled={!chosen || !location || book.isPending}
              onClick={() =>
                chosen &&
                location &&
                book.mutate(
                  {
                    locationId: location.locationId,
                    start: chosen.start,
                    typeKey,
                  },
                  { onSuccess: onClose }
                )
              }
              type="button"
            >
              {chosen
                ? `Book ${formatDay(chosen.start)}, ${formatClock(chosen.start)}`
                : 'Choose a time'}
            </button>
          </div>
        </div>
      )}
    </Dialog>
  );
}
