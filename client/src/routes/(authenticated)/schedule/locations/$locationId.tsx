import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useId, useState } from 'react';
import {
  errorMessage,
  formatClock,
  formatDate,
  formatDay,
} from '@/lib/journey.ts';
import {
  availabilityQueryOptions,
  type Hours,
  type LocationAvailability,
  useAddClosure,
  useAddSession,
  useRemoveClosure,
  useRemoveSession,
  useSaveHours,
  type Weekday,
} from '@/queries/schedule.ts';

export const Route = createFileRoute(
  '/(authenticated)/schedule/locations/$locationId'
)({
  component: LocationPage,
});

const weekdays: Weekday[] = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
];

/** A location's availability: weekly hours, dated sessions, and closures. */
function LocationPage() {
  const id = Number(Route.useParams().locationId);
  const { data, error } = useQuery(availabilityQueryOptions(id));

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <Link
          className="link link-hover text-sm"
          search={{ location: id }}
          to="/schedule"
        >
          ← Schedule
        </Link>
        <h1 className="mt-1 text-3xl font-bold">
          {data ? data.location.name : 'Location'}
        </h1>
        {data && (
          <p className="text-base-content/70">
            {data.location.appointmentTypeName} ·{' '}
            {data.location.address ?? 'Address to be confirmed'} ·{' '}
            {data.location.teamName}
          </p>
        )}
      </div>
      {error ? (
        <div className="alert alert-error" role="alert">
          {errorMessage(error)}
        </div>
      ) : !data ? (
        <div className="flex justify-center py-12">
          <span className="loading loading-spinner loading-lg" />
        </div>
      ) : (
        <>
          <HoursEditor
            availability={data}
            key={JSON.stringify(data.hours)}
            locationId={id}
          />
          <Sessions availability={data} locationId={id} />
          <Closures availability={data} locationId={id} />
        </>
      )}
    </div>
  );
}

const trimSeconds = (t: string) => t.slice(0, 5);

function HoursEditor({
  availability,
  locationId,
}: {
  availability: LocationAvailability;
  locationId: number;
}) {
  const save = useSaveHours(locationId);
  const [rows, setRows] = useState<Hours[]>(
    availability.hours.map((h) => ({
      ...h,
      end: trimSeconds(h.end),
      start: trimSeconds(h.start),
    }))
  );
  const update = (i: number, change: Partial<Hours>) =>
    setRows((all) =>
      all.map((row, j) => (j === i ? { ...row, ...change } : row))
    );

  return (
    <section
      aria-labelledby="hours-heading"
      className="card bg-base-100 shadow-sm"
    >
      <div className="card-body gap-4">
        <div>
          <h2 className="card-title" id="hours-heading">
            Weekly hours
          </h2>
          <p className="text-sm text-base-content/70">
            Candidates book back-to-back appointments within these hours.
            Holidays and closures are left out automatically. Changing hours
            keeps appointments already booked.
          </p>
        </div>
        {rows.length === 0 && (
          <p className="text-base-content/70">
            No weekly hours. Candidates can book only dated sessions.
          </p>
        )}
        {rows.map((row, i) => (
          <fieldset
            className="grid items-end gap-2 sm:grid-cols-[9rem_7rem_7rem_8rem_7rem_auto]"
            key={i}
          >
            <legend className="sr-only">Hours {i + 1}</legend>
            <Field label="Day">
              {(id) => (
                <select
                  className="select select-bordered select-sm w-full"
                  id={id}
                  onChange={(e) =>
                    update(i, { day: e.target.value as Weekday })
                  }
                  value={row.day}
                >
                  {weekdays.map((d) => (
                    <option key={d}>{d}</option>
                  ))}
                </select>
              )}
            </Field>
            <Field label="From">
              {(id) => (
                <input
                  className="input input-bordered input-sm w-full"
                  id={id}
                  onChange={(e) => update(i, { start: e.target.value })}
                  type="time"
                  value={row.start}
                />
              )}
            </Field>
            <Field label="Until">
              {(id) => (
                <input
                  className="input input-bordered input-sm w-full"
                  id={id}
                  onChange={(e) => update(i, { end: e.target.value })}
                  type="time"
                  value={row.end}
                />
              )}
            </Field>
            <Field label="Minutes each">
              {(id) => (
                <input
                  className="input input-bordered input-sm w-full"
                  id={id}
                  min={5}
                  onChange={(e) =>
                    update(i, { slotMinutes: Number(e.target.value) })
                  }
                  type="number"
                  value={row.slotMinutes}
                />
              )}
            </Field>
            <Field label="At once">
              {(id) => (
                <input
                  className="input input-bordered input-sm w-full"
                  id={id}
                  min={1}
                  onChange={(e) =>
                    update(i, { capacity: Number(e.target.value) })
                  }
                  type="number"
                  value={row.capacity}
                />
              )}
            </Field>
            <button
              className="btn btn-ghost btn-sm"
              onClick={() => setRows((all) => all.filter((_, j) => j !== i))}
              type="button"
            >
              Remove
            </button>
          </fieldset>
        ))}
        {save.error && (
          <div className="alert alert-error py-2 text-sm" role="alert">
            {errorMessage(save.error)}
          </div>
        )}
        {save.isSuccess && (
          <div className="alert alert-success py-2 text-sm" role="status">
            Hours saved.
          </div>
        )}
        <div className="flex gap-2">
          <button
            className="btn btn-outline btn-sm"
            onClick={() =>
              setRows((all) => [
                ...all,
                {
                  capacity: 1,
                  day: 'Monday',
                  end: '12:00',
                  slotMinutes: 15,
                  start: '09:00',
                },
              ])
            }
            type="button"
          >
            Add hours
          </button>
          <button
            className="btn btn-primary btn-sm"
            disabled={save.isPending}
            onClick={() => save.mutate(rows)}
            type="button"
          >
            Save hours
          </button>
        </div>
      </div>
    </section>
  );
}

function Sessions({
  availability,
  locationId,
}: {
  availability: LocationAvailability;
  locationId: number;
}) {
  const add = useAddSession(locationId);
  const remove = useRemoveSession(locationId);
  const [form, setForm] = useState({
    capacity: 40,
    date: '',
    end: '12:00',
    note: '',
    start: '08:00',
  });
  const error = add.error ?? remove.error;

  return (
    <section
      aria-labelledby="sessions-heading"
      className="card bg-base-100 shadow-sm"
    >
      <div className="card-body gap-4">
        <div>
          <h2 className="card-title" id="sessions-heading">
            Sessions
          </h2>
          <p className="text-sm text-base-content/70">
            A dated block with a number of seats, such as a New Employee
            Welcome, or extra hours.
          </p>
        </div>
        {availability.sessions.length ? (
          <ul className="divide-y divide-base-200">
            {availability.sessions.map((s) => (
              <li
                className="flex flex-wrap items-center justify-between gap-2 py-2"
                key={s.id}
              >
                <span>
                  {formatDay(s.start)}, {formatClock(s.start)}–
                  {formatClock(s.end)}
                  <span className="ml-2 text-sm text-base-content/70">
                    {s.booked} of {s.capacity} seats booked
                    {s.note && ` · ${s.note}`}
                  </span>
                </span>
                <button
                  className="btn btn-ghost btn-sm text-error"
                  disabled={remove.isPending}
                  onClick={() => remove.mutate(s.id)}
                  type="button"
                >
                  Remove
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-base-content/70">No upcoming sessions.</p>
        )}
        <form
          className="grid items-end gap-2 sm:grid-cols-[10rem_7rem_7rem_6rem_1fr_auto]"
          onSubmit={(e) => {
            e.preventDefault();
            if (!form.date) {
              return;
            }
            add.mutate({
              capacity: form.capacity,
              end: `${form.date}T${form.end}:00`,
              note: form.note || null,
              start: `${form.date}T${form.start}:00`,
            });
          }}
        >
          <Field label="Date">
            {(id) => (
              <input
                className="input input-bordered input-sm w-full"
                id={id}
                onChange={(e) => setForm({ ...form, date: e.target.value })}
                required
                type="date"
                value={form.date}
              />
            )}
          </Field>
          <Field label="From">
            {(id) => (
              <input
                className="input input-bordered input-sm w-full"
                id={id}
                onChange={(e) => setForm({ ...form, start: e.target.value })}
                type="time"
                value={form.start}
              />
            )}
          </Field>
          <Field label="Until">
            {(id) => (
              <input
                className="input input-bordered input-sm w-full"
                id={id}
                onChange={(e) => setForm({ ...form, end: e.target.value })}
                type="time"
                value={form.end}
              />
            )}
          </Field>
          <Field label="Seats">
            {(id) => (
              <input
                className="input input-bordered input-sm w-full"
                id={id}
                min={1}
                onChange={(e) =>
                  setForm({ ...form, capacity: Number(e.target.value) })
                }
                type="number"
                value={form.capacity}
              />
            )}
          </Field>
          <Field label="Note (optional)">
            {(id) => (
              <input
                className="input input-bordered input-sm w-full"
                id={id}
                onChange={(e) => setForm({ ...form, note: e.target.value })}
                value={form.note}
              />
            )}
          </Field>
          <button
            className="btn btn-primary btn-sm"
            disabled={add.isPending}
            type="submit"
          >
            Add session
          </button>
        </form>
        {error && (
          <div className="alert alert-error py-2 text-sm" role="alert">
            {errorMessage(error)}
          </div>
        )}
      </div>
    </section>
  );
}

function Closures({
  availability,
  locationId,
}: {
  availability: LocationAvailability;
  locationId: number;
}) {
  const add = useAddClosure(locationId);
  const remove = useRemoveClosure(locationId);
  const [date, setDate] = useState('');
  const [reason, setReason] = useState('');
  const error = add.error ?? remove.error;

  return (
    <section
      aria-labelledby="closures-heading"
      className="card bg-base-100 shadow-sm"
    >
      <div className="card-body gap-4">
        <div>
          <h2 className="card-title" id="closures-heading">
            Closures
          </h2>
          <p className="text-sm text-base-content/70">
            Days this location takes no appointments, beyond UC Davis holidays.
          </p>
        </div>
        {availability.closures.length ? (
          <ul className="divide-y divide-base-200">
            {availability.closures.map((c) => (
              <li
                className="flex flex-wrap items-center justify-between gap-2 py-2"
                key={c.id}
              >
                <span>
                  {formatDate(c.date)}{' '}
                  <span className="text-sm text-base-content/70">
                    · {c.reason}
                  </span>
                </span>
                <button
                  className="btn btn-ghost btn-sm"
                  disabled={remove.isPending}
                  onClick={() => remove.mutate(c.id)}
                  type="button"
                >
                  Reopen
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-base-content/70">No upcoming closures.</p>
        )}
        <form
          className="grid items-end gap-2 sm:grid-cols-[10rem_1fr_auto]"
          onSubmit={(e) => {
            e.preventDefault();
            if (date) {
              add.mutate(
                { date, reason },
                {
                  onSuccess: () => {
                    setDate('');
                    setReason('');
                  },
                }
              );
            }
          }}
        >
          <Field label="Date">
            {(id) => (
              <input
                className="input input-bordered input-sm w-full"
                id={id}
                onChange={(e) => setDate(e.target.value)}
                required
                type="date"
                value={date}
              />
            )}
          </Field>
          <Field label="Reason">
            {(id) => (
              <input
                className="input input-bordered input-sm w-full"
                id={id}
                onChange={(e) => setReason(e.target.value)}
                required
                value={reason}
              />
            )}
          </Field>
          <button
            className="btn btn-primary btn-sm"
            disabled={add.isPending}
            type="submit"
          >
            Close that day
          </button>
        </form>
        {error && (
          <div className="alert alert-error py-2 text-sm" role="alert">
            {errorMessage(error)}
          </div>
        )}
      </div>
    </section>
  );
}

function Field({
  children,
  label,
}: {
  children: (id: string) => React.ReactNode;
  label: string;
}) {
  const id = useId();
  return (
    <div className="form-control">
      <label className="label py-1 text-xs" htmlFor={id}>
        {label}
      </label>
      {children(id)}
    </div>
  );
}
