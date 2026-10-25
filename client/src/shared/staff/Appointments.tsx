import { useQuery } from '@tanstack/react-query';
import { useId, useState } from 'react';
import {
  appointmentStatusBadge,
  appointmentStatusLabel,
  errorMessage,
  formatAppointment,
} from '@/lib/journey.ts';
import {
  type AppointmentSummary,
  bookingOptionsQueryOptions,
  candidateAppointmentsQueryOptions,
  type CandidateAppointmentType,
  useBookForCandidate,
  useCancelForCandidate,
} from '@/queries/schedule.ts';
import { BookingDialog } from '../scheduling/BookingDialog.tsx';

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
        <StaffBooking
          candidateId={candidateId}
          onClose={() => setBooking(null)}
          type={booking}
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

/** Booking on the candidate's behalf. Shows the email notice, if any, on the candidate page. */
function StaffBooking({
  candidateId,
  onClose,
  type,
}: {
  candidateId: number;
  onClose: () => void;
  type: CandidateAppointmentType;
}) {
  const book = useBookForCandidate(candidateId);
  return (
    <BookingDialog
      booking={book}
      current={type.current}
      onBook={(choice) =>
        book.mutate(
          { ...choice, typeKey: type.typeKey },
          { onSuccess: onClose }
        )
      }
      onClose={onClose}
      optionsFor={(from, to) =>
        bookingOptionsQueryOptions(candidateId, type.typeKey, from, to)
      }
      title={`${type.current ? 'Change' : 'Book'} ${type.typeName}`}
    />
  );
}
