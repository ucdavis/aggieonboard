import { useState } from 'react';
import {
  errorMessage,
  formatAppointment,
  formatClock,
  formatDay,
} from '@/lib/journey.ts';
import {
  type PortalBooking,
  portalBookingOptionsQueryOptions,
  useBookAppointment,
  useCancelAppointment,
} from '@/queries/portal.ts';
import { BookingDialog } from '../scheduling/BookingDialog.tsx';

/**
 * A step's appointment in the checklist: book it, or see, change and cancel the one booked. The
 * server decides what's allowed (canBook, canChange); this shows exactly that. A change can move
 * the step to another section, which remounts this card, so the page shows the confirmation.
 */
export function StepBooking({
  booking,
  onDone,
}: {
  booking: PortalBooking;
  onDone: (message: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [confirmingCancel, setConfirmingCancel] = useState(false);
  const book = useBookAppointment();
  const cancel = useCancelAppointment();
  const a = booking.appointment;
  const booked = a?.status === 'Booked' ? a : null;

  return (
    <div className="space-y-2 rounded-box border border-base-300 p-3">
      {a ? (
        <div className="space-y-1">
          <p className="text-sm font-semibold">
            {a.status === 'Attended'
              ? `Attended: ${booking.typeName}`
              : `Your appointment: ${booking.typeName}`}
          </p>
          <p>{formatAppointment(a.start, a.end)}</p>
          <p className="text-sm">
            {a.locationName}
            {a.address && (
              <span className="block text-base-content/70">{a.address}</span>
            )}
          </p>
          {booked &&
            (booked.canChange ? (
              <p className="text-sm text-base-content/70">
                {`Until ${formatDay(booked.changeCutoff)}, ${formatClock(booked.changeCutoff)}, you can change or cancel it here.`}
              </p>
            ) : (
              <p className="text-sm text-base-content/70">
                It&apos;s too close to change here. To change it, reply to your
                confirmation email.
              </p>
            ))}
        </div>
      ) : (
        <p className="text-sm">
          Book your {booking.typeName} appointment here.
        </p>
      )}

      {booking.canBook && !confirmingCancel && (
        <div className="flex flex-wrap gap-2">
          <button
            className="btn btn-primary btn-sm"
            onClick={() => {
              book.reset();
              setOpen(true);
            }}
            type="button"
          >
            {booked ? 'Change time' : `Book ${booking.typeName}`}
          </button>
          {booked?.canChange && (
            <button
              className="btn btn-ghost btn-sm text-error"
              onClick={() => {
                cancel.reset();
                setConfirmingCancel(true);
              }}
              type="button"
            >
              Cancel appointment
            </button>
          )}
        </div>
      )}

      {confirmingCancel && booked && (
        <div className="flex flex-wrap items-center gap-2" role="group">
          <span className="text-sm">Cancel this appointment?</span>
          <button
            className="btn btn-error btn-sm"
            disabled={cancel.isPending}
            onClick={() =>
              cancel.mutate(booked.id, {
                onSuccess: () => {
                  setConfirmingCancel(false);
                  onDone(`Your ${booking.typeName} appointment is cancelled.`);
                },
              })
            }
            type="button"
          >
            Yes, cancel it
          </button>
          <button
            className="btn btn-ghost btn-sm"
            onClick={() => setConfirmingCancel(false)}
            type="button"
          >
            Keep it
          </button>
        </div>
      )}
      {cancel.error && (
        <div className="alert alert-error py-2 text-sm" role="alert">
          {errorMessage(cancel.error)}
        </div>
      )}

      {open && (
        <BookingDialog
          booking={book}
          current={booked}
          onBook={(choice) =>
            book.mutate(
              { ...choice, typeKey: booking.typeKey },
              {
                onSuccess: () => {
                  setOpen(false);
                  onDone(
                    `Your ${booking.typeName} appointment is ${booked ? 'changed' : 'booked'}. We're emailing you the details.`
                  );
                },
              }
            )
          }
          onClose={() => setOpen(false)}
          optionsFor={(from, to) =>
            portalBookingOptionsQueryOptions(booking.typeKey, from, to)
          }
          title={`${booked ? 'Change' : 'Book'} ${booking.typeName}`}
        />
      )}
    </div>
  );
}
