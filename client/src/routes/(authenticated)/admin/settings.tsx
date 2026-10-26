import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { type ReactNode, useId, useState } from 'react';
import { HttpError } from '@/lib/api.ts';
import { errorMessage, formatDate } from '@/lib/journey.ts';
import {
  type DeadlineAnchor,
  type DeadlineUnit,
  holidaysQueryOptions,
  type Setting,
  settingsQueryOptions,
  type StepDeadline,
  stepDeadlinesQueryOptions,
  useAddHoliday,
  useRemoveHoliday,
  useSaveSetting,
  useSaveStepDeadline,
} from '@/queries/admin.ts';

export const Route = createFileRoute('/(authenticated)/admin/settings')({
  component: SettingsPage,
});

/**
 * The process facts the audit left open. A change that moves deadlines recomputes every open
 * step staff haven't set by hand; the notice says how many moved.
 */
function SettingsPage() {
  const [notice, setNotice] = useState<string | null>(null);
  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-3xl font-bold">Settings</h1>
        <p className="text-base-content/70">
          Process facts the communications audit left open. Mark one confirmed
          once TA or SSO answers.
        </p>
      </div>
      {notice && (
        <div className="alert alert-success" role="status">
          <span>{notice}</span>
          <button
            className="btn btn-ghost btn-sm"
            onClick={() => setNotice(null)}
            type="button"
          >
            Dismiss
          </button>
        </div>
      )}
      <Settings onSaved={setNotice} />
      <StepDeadlines onSaved={setNotice} />
      <Holidays onSaved={setNotice} />
    </div>
  );
}

const fieldErrors = (error: Error | null) =>
  error instanceof HttpError && error.status === 400
    ? ((error.body as { errors?: Record<string, string[]> }).errors ?? {})
    : null;

function Section({
  children,
  heading,
  intro,
}: {
  children: ReactNode;
  heading: string;
  intro: string;
}) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="space-y-3">
      <div>
        <h2 className="text-xl font-bold" id={id}>
          {heading}
        </h2>
        <p className="text-sm text-base-content/70">{intro}</p>
      </div>
      {children}
    </section>
  );
}

function Loading({ error }: { error: Error | null }) {
  return error ? (
    <div className="alert alert-error" role="alert">
      {errorMessage(error)}
    </div>
  ) : (
    <span className="loading loading-spinner" />
  );
}

function Settings({ onSaved }: { onSaved: (notice: string) => void }) {
  const { data, error } = useQuery(settingsQueryOptions());
  return (
    <Section
      heading="Process settings"
      intro="Messages that need a setting with no value can't be sent until it has one."
    >
      {!data ? (
        <Loading error={error} />
      ) : (
        <ul className="space-y-3">
          {data.map((s) => (
            <SettingRow key={s.key} onSaved={onSaved} setting={s} />
          ))}
        </ul>
      )}
    </Section>
  );
}

function SettingRow({
  onSaved,
  setting,
}: {
  onSaved: (notice: string) => void;
  setting: Setting;
}) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(setting.value ?? '');
  const [confirmed, setConfirmed] = useState(setting.isConfirmed);
  const [question, setQuestion] = useState(setting.openQuestion ?? '');
  const save = useSaveSetting();
  const errors = fieldErrors(save.error);
  const valueId = useId();
  const questionId = useId();

  return (
    <li
      aria-label={setting.key}
      className="rounded-box bg-base-100 p-4 shadow-sm"
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <p className="font-semibold">{setting.key}</p>
          <p className="text-sm text-base-content/70">{setting.description}</p>
        </div>
        <span
          className={`badge ${setting.isConfirmed ? 'badge-success' : 'badge-warning'}`}
        >
          {setting.isConfirmed ? 'Confirmed' : 'Unconfirmed'}
        </span>
      </div>
      {!editing ? (
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <div className="text-sm">
            <p>
              {setting.value ?? (
                <span className="text-error">No value yet</span>
              )}
            </p>
            {setting.openQuestion && (
              <p className="text-base-content/60">
                Open question: {setting.openQuestion}
              </p>
            )}
          </div>
          <button
            className="btn btn-ghost btn-sm"
            onClick={() => {
              save.reset();
              setEditing(true);
            }}
            type="button"
          >
            Edit
          </button>
        </div>
      ) : (
        <form
          className="mt-3 space-y-3"
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate(
              {
                isConfirmed: confirmed,
                key: setting.key,
                openQuestion: question,
                value,
              },
              {
                onSuccess: (result) => {
                  setEditing(false);
                  onSaved(result.notice);
                },
              }
            );
          }}
        >
          <div className="form-control">
            <label className="label py-1 text-sm" htmlFor={valueId}>
              Value (leave blank if nobody knows yet)
            </label>
            <input
              aria-invalid={errors?.value ? true : undefined}
              className={`input input-bordered input-sm w-full ${errors?.value ? 'input-error' : ''}`}
              id={valueId}
              inputMode={setting.kind === 'Integer' ? 'numeric' : undefined}
              onChange={(e) => setValue(e.target.value)}
              type={setting.kind === 'Url' ? 'url' : 'text'}
              value={value}
            />
            <Errors messages={errors?.value} />
          </div>
          <label className="label cursor-pointer justify-start gap-2">
            <input
              checked={confirmed}
              className="checkbox checkbox-sm checkbox-primary"
              onChange={(e) => setConfirmed(e.target.checked)}
              type="checkbox"
            />
            <span className="label-text">Confirmed by TA or SSO</span>
          </label>
          <Errors messages={errors?.isConfirmed} />
          <div className="form-control">
            <label className="label py-1 text-sm" htmlFor={questionId}>
              Open question
            </label>
            <textarea
              className="textarea textarea-bordered textarea-sm w-full"
              id={questionId}
              onChange={(e) => setQuestion(e.target.value)}
              rows={2}
              value={question}
            />
            <Errors messages={errors?.openQuestion} />
          </div>
          <SaveButtons
            error={errors ? null : save.error}
            isPending={save.isPending}
            onCancel={() => setEditing(false)}
          />
        </form>
      )}
    </li>
  );
}

const anchorLabel: Record<DeadlineAnchor, string> = {
  Activation: 'After the step starts',
  AfterStart: 'After the start date',
  BeforeStart: 'Before the start date',
  Clearance: 'The clearance deadline',
  None: 'No rule: staff enter it',
};

const unitLabel: Record<DeadlineUnit, string> = {
  BusinessDays: 'business days',
  BusinessHours: 'business hours',
  CalendarDays: 'calendar days',
  Hours: 'hours',
  Weeks: 'weeks',
};

function StepDeadlines({ onSaved }: { onSaved: (notice: string) => void }) {
  const { data, error } = useQuery(stepDeadlinesQueryOptions());
  return (
    <Section
      heading="Step deadlines"
      intro="How each step's due date is worked out. Changing a rule recomputes open due dates that staff haven't set by hand."
    >
      {!data ? (
        <Loading error={error} />
      ) : (
        <ul className="space-y-2">
          {data.map((s) => (
            <StepDeadlineRow key={s.key} onSaved={onSaved} step={s} />
          ))}
        </ul>
      )}
    </Section>
  );
}

function StepDeadlineRow({
  onSaved,
  step,
}: {
  onSaved: (notice: string) => void;
  step: StepDeadline;
}) {
  const [editing, setEditing] = useState(false);
  const [anchor, setAnchor] = useState(step.anchor);
  const [amount, setAmount] = useState(String(step.amount));
  const [unit, setUnit] = useState(step.unit);
  const [confirmed, setConfirmed] = useState(step.confirmed);
  const [question, setQuestion] = useState(step.question ?? '');
  const save = useSaveStepDeadline();
  const errors = fieldErrors(save.error);
  const ids = {
    amount: useId(),
    anchor: useId(),
    question: useId(),
    unit: useId(),
  };
  const counted = anchor !== 'None' && anchor !== 'Clearance';
  const units = (Object.keys(unitLabel) as DeadlineUnit[]).filter(
    (u) => anchor === 'Activation' || (u !== 'Hours' && u !== 'BusinessHours')
  );

  return (
    <li
      aria-label={step.title}
      className="rounded-box bg-base-100 p-4 shadow-sm"
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <p className="font-semibold">
            {step.order}. {step.title}
          </p>
          <p className="text-sm">{step.rule}</p>
          {step.question && (
            <p className="text-sm text-base-content/60">
              Open question: {step.question}
            </p>
          )}
        </div>
        <div className="flex items-center gap-2">
          <span
            className={`badge ${step.confirmed ? 'badge-success' : 'badge-warning'}`}
          >
            {step.confirmed ? 'Confirmed' : 'Unconfirmed'}
          </span>
          {!editing && (
            <button
              className="btn btn-ghost btn-sm"
              onClick={() => {
                save.reset();
                setEditing(true);
              }}
              type="button"
            >
              Edit
            </button>
          )}
        </div>
      </div>
      {editing && (
        <form
          className="mt-3 space-y-3"
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate(
              {
                amount: counted ? Number(amount) : 0,
                anchor,
                confirmed,
                key: step.key,
                question,
                unit: counted ? unit : 'BusinessDays',
              },
              {
                onSuccess: (result) => {
                  setEditing(false);
                  onSaved(result.notice);
                },
              }
            );
          }}
        >
          <div className="flex flex-wrap items-end gap-3">
            <div className="form-control">
              <label className="label py-1 text-sm" htmlFor={ids.anchor}>
                Due
              </label>
              <select
                className="select select-bordered select-sm"
                id={ids.anchor}
                onChange={(e) => {
                  const next = e.target.value as DeadlineAnchor;
                  setAnchor(next);
                  if (
                    next !== 'Activation' &&
                    (unit === 'Hours' || unit === 'BusinessHours')
                  ) {
                    setUnit('BusinessDays');
                  }
                }}
                value={anchor}
              >
                {(Object.keys(anchorLabel) as DeadlineAnchor[]).map((a) => (
                  <option key={a} value={a}>
                    {anchorLabel[a]}
                  </option>
                ))}
              </select>
              <Errors messages={errors?.anchor} />
            </div>
            {counted && (
              <>
                <div className="form-control w-24">
                  <label className="label py-1 text-sm" htmlFor={ids.amount}>
                    How many
                  </label>
                  <input
                    aria-invalid={errors?.amount ? true : undefined}
                    className={`input input-bordered input-sm ${errors?.amount ? 'input-error' : ''}`}
                    id={ids.amount}
                    inputMode="numeric"
                    onChange={(e) => setAmount(e.target.value)}
                    value={amount}
                  />
                </div>
                <div className="form-control">
                  <label className="label py-1 text-sm" htmlFor={ids.unit}>
                    Unit
                  </label>
                  <select
                    className="select select-bordered select-sm"
                    id={ids.unit}
                    onChange={(e) => setUnit(e.target.value as DeadlineUnit)}
                    value={unit}
                  >
                    {units.map((u) => (
                      <option key={u} value={u}>
                        {unitLabel[u]}
                      </option>
                    ))}
                  </select>
                </div>
              </>
            )}
          </div>
          <Errors
            messages={[...(errors?.amount ?? []), ...(errors?.unit ?? [])]}
          />
          <label className="label cursor-pointer justify-start gap-2">
            <input
              checked={confirmed}
              className="checkbox checkbox-sm checkbox-primary"
              onChange={(e) => setConfirmed(e.target.checked)}
              type="checkbox"
            />
            <span className="label-text">Confirmed by TA or SSO</span>
          </label>
          <div className="form-control">
            <label className="label py-1 text-sm" htmlFor={ids.question}>
              Open question
            </label>
            <textarea
              className="textarea textarea-bordered textarea-sm w-full"
              id={ids.question}
              onChange={(e) => setQuestion(e.target.value)}
              rows={2}
              value={question}
            />
            <Errors messages={errors?.question} />
          </div>
          <SaveButtons
            error={errors ? null : save.error}
            isPending={save.isPending}
            onCancel={() => setEditing(false)}
          />
        </form>
      )}
    </li>
  );
}

function Holidays({ onSaved }: { onSaved: (notice: string) => void }) {
  const { data, error } = useQuery(holidaysQueryOptions());
  const add = useAddHoliday();
  const remove = useRemoveHoliday();
  const [date, setDate] = useState('');
  const [name, setName] = useState('');
  const errors = fieldErrors(add.error);
  const dateId = useId();
  const nameId = useId();

  return (
    <Section
      heading="Holidays"
      intro="Business-day deadlines skip weekends and these dates. Add each year's UC Davis holidays before they're needed."
    >
      <form
        className="flex flex-wrap items-end gap-3 rounded-box bg-base-100 p-4 shadow-sm"
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          add.mutate(
            { date, name },
            {
              onSuccess: (result) => {
                setDate('');
                setName('');
                onSaved(result.notice);
              },
            }
          );
        }}
      >
        <div className="form-control">
          <label className="label py-1 text-sm" htmlFor={dateId}>
            Date
          </label>
          <input
            aria-invalid={errors?.date ? true : undefined}
            className={`input input-bordered input-sm ${errors?.date ? 'input-error' : ''}`}
            id={dateId}
            onChange={(e) => setDate(e.target.value)}
            type="date"
            value={date}
          />
          <Errors messages={errors?.date} />
        </div>
        <div className="form-control grow">
          <label className="label py-1 text-sm" htmlFor={nameId}>
            Holiday
          </label>
          <input
            aria-invalid={errors?.name ? true : undefined}
            className={`input input-bordered input-sm ${errors?.name ? 'input-error' : ''}`}
            id={nameId}
            onChange={(e) => setName(e.target.value)}
            value={name}
          />
          <Errors messages={errors?.name} />
        </div>
        <button
          className="btn btn-primary btn-sm"
          disabled={add.isPending}
          type="submit"
        >
          Add holiday
        </button>
      </form>
      {add.error && !errors && <Failure error={add.error} />}
      {remove.error && <Failure error={remove.error} />}
      {!data ? (
        <Loading error={error} />
      ) : (
        <ul className="divide-y divide-base-200 rounded-box bg-base-100 shadow-sm">
          {data.map((h) => (
            <li
              className="flex items-center justify-between gap-2 px-4 py-2"
              key={h.date}
            >
              <span>
                <span className="font-medium">{formatDate(h.date)}</span> ·{' '}
                {h.name}
              </span>
              <button
                aria-label={`Remove ${h.name}, ${formatDate(h.date)}`}
                className="btn btn-ghost btn-xs text-error"
                disabled={remove.isPending}
                onClick={() =>
                  remove.mutate(h.date, {
                    onSuccess: (result) => onSaved(result.notice),
                  })
                }
                type="button"
              >
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}

function SaveButtons({
  error,
  isPending,
  onCancel,
}: {
  error: Error | null;
  isPending: boolean;
  onCancel: () => void;
}) {
  return (
    <>
      {error && <Failure error={error} />}
      <div className="flex gap-2">
        <button
          className="btn btn-primary btn-sm"
          disabled={isPending}
          type="submit"
        >
          Save
        </button>
        <button
          className="btn btn-ghost btn-sm"
          onClick={onCancel}
          type="button"
        >
          Cancel
        </button>
      </div>
    </>
  );
}

function Failure({ error }: { error: Error }) {
  return (
    <div className="alert alert-error py-2 text-sm" role="alert">
      {errorMessage(error)}
    </div>
  );
}

function Errors({ messages }: { messages?: string[] }) {
  if (!messages?.length) {
    return null;
  }
  return (
    <ul className="mt-1 space-y-1 text-sm text-error">
      {messages.map((m) => (
        <li key={m}>{m}</li>
      ))}
    </ul>
  );
}
