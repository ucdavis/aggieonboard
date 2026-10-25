import { type UseQueryOptions, useQuery } from '@tanstack/react-query';
import { useId, useState } from 'react';
import {
  addDays,
  errorMessage,
  formatAppointment,
  formatClock,
  formatDate,
  formatDay,
  pacificToday,
} from '@/lib/journey.ts';
import type { BookingOptions, Slot } from '@/queries/schedule.ts';
import { Dialog } from '../staff/Dialog.tsx';

const span = 13;

export type BookingChoice = { locationId: number; start: string };

/**
 * Open times in the candidate's window, two weeks at a time, grouped by day. Staff (on a
 * candidate's behalf) and candidates (in the portal) book through the same dialog; each passes
 * where its options come from and what booking does.
 */
export function BookingDialog<TKey extends readonly unknown[]>({
  booking,
  current,
  onBook,
  onClose,
  optionsFor,
  title,
}: {
  booking: { error: Error | null; isPending: boolean };
  current: { end: string; locationName: string; start: string } | null;
  onBook: (choice: BookingChoice) => void;
  onClose: () => void;
  optionsFor: (
    from: string,
    to: string
  ) => UseQueryOptions<BookingOptions, Error, BookingOptions, TKey>;
  title: string;
}) {
  const [from, setFrom] = useState(pacificToday());
  const to = addDays(from, span);
  const options = useQuery(optionsFor(from, to));
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
    <Dialog onClose={onClose} title={title}>
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
          {location?.address && (
            <p className="text-sm text-base-content/70">{location.address}</p>
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
            <span className="text-center text-sm font-medium">
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
          {booking.error && (
            <div className="alert alert-error text-sm" role="alert">
              {errorMessage(booking.error)}
            </div>
          )}
          <div className="flex flex-wrap justify-end gap-2">
            <button className="btn btn-ghost" onClick={onClose} type="button">
              Close
            </button>
            <button
              className="btn btn-primary"
              disabled={!chosen || !location || booking.isPending}
              onClick={() =>
                chosen &&
                location &&
                onBook({ locationId: location.locationId, start: chosen.start })
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
