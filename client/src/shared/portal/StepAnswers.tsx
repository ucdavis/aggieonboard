import { type ReactNode, useId, useState } from 'react';
import { HttpError } from '@/lib/api.ts';
import { errorMessage } from '@/lib/journey.ts';
import {
  type PortalAtiNumber,
  type PortalBooking,
  type PortalI9Choice,
  type PortalRepresentative,
  useChooseRemoteI9,
  useSetAtiNumber,
  useSetRepresentative,
} from '@/queries/portal.ts';
import { StepBooking } from './StepBooking.tsx';

// The answers messages 11A, 11B and 12 used to ask for by reply. The server says which step asks for
// what (PortalAnswers); an answer can move the step to another section, so the page shows the confirmation.

/** Step 8: in person (book a time) or remotely. Once remote is chosen, only staff can change it. */
export function I9Choice({
  booking,
  choice,
  onDone,
}: {
  booking: PortalBooking | null;
  choice: PortalI9Choice;
  onDone: (message: string) => void;
}) {
  const [confirming, setConfirming] = useState(false);
  const remote = useChooseRemoteI9();

  if (choice.method === 'Remote') {
    return (
      <p className="rounded-box border border-base-300 p-3 text-sm">
        You chose to verify your documents remotely, with an authorized
        representative.
      </p>
    );
  }
  if (!choice.canChoose) {
    return booking && <StepBooking booking={booking} onDone={onDone} />;
  }

  return (
    <div className="space-y-2">
      <p className="font-semibold">
        Choose how you&apos;ll verify your documents
      </p>
      {booking && <StepBooking booking={booking} onDone={onDone} />}
      <div className="space-y-2 rounded-box border border-base-300 p-3">
        <p className="text-sm">
          Or verify remotely, with an authorized representative.
        </p>
        {confirming ? (
          <div className="flex flex-wrap items-center gap-2" role="group">
            <span className="text-sm">
              Verify remotely? We&apos;ll email you what to do next.
            </span>
            <button
              className="btn btn-primary btn-sm"
              disabled={remote.isPending}
              onClick={() =>
                remote.mutate(undefined, {
                  onSuccess: () =>
                    onDone(
                      "You chose remote verification. We're emailing you the next steps."
                    ),
                })
              }
              type="button"
            >
              Yes, verify remotely
            </button>
            <button
              className="btn btn-ghost btn-sm"
              onClick={() => setConfirming(false)}
              type="button"
            >
              Back
            </button>
          </div>
        ) : (
          <button
            className="btn btn-outline btn-sm"
            onClick={() => {
              remote.reset();
              setConfirming(true);
            }}
            type="button"
          >
            Verify remotely
          </button>
        )}
        {remote.error && <Failure error={remote.error} />}
      </div>
    </div>
  );
}

/** Step 9: message 11B's answer, the representative's full name and email address. */
export function RepresentativeForm({
  onDone,
  representative,
}: {
  onDone: (message: string) => void;
  representative: PortalRepresentative;
}) {
  const [editing, setEditing] = useState(!representative.name);
  const [name, setName] = useState(representative.name ?? '');
  const [email, setEmail] = useState(representative.email ?? '');
  const save = useSetRepresentative();
  const errors = fieldErrors(save.error);

  if (!editing) {
    return (
      <Saved
        label="Your authorized representative"
        onChange={() => {
          save.reset();
          setEditing(true);
        }}
        value={`${representative.name} (${representative.email})`}
      />
    );
  }

  return (
    <AnswerForm
      error={errors ? null : save.error}
      heading="Your authorized representative"
      isPending={save.isPending}
      onCancel={representative.name ? () => setEditing(false) : undefined}
      onSubmit={() =>
        save.mutate(
          { email, name },
          {
            onSuccess: () => {
              setEditing(false);
              onDone("Thanks. We have your representative's details.");
            },
          }
        )
      }
    >
      <Field
        error={errors?.name}
        label="Full name"
        onChange={setName}
        value={name}
      />
      <Field
        error={errors?.email}
        label="Email address"
        onChange={setEmail}
        type="email"
        value={email}
      />
    </AnswerForm>
  );
}

/** Step 11: message 12's ATI number, after the fingerprinting appointment. */
export function AtiNumberForm({
  atiNumber,
  onDone,
}: {
  atiNumber: PortalAtiNumber;
  onDone: (message: string) => void;
}) {
  const [editing, setEditing] = useState(!atiNumber.value);
  const [value, setValue] = useState(atiNumber.value ?? '');
  const save = useSetAtiNumber();
  const errors = fieldErrors(save.error);

  if (!editing) {
    return (
      <Saved
        label="Your ATI number"
        onChange={() => {
          save.reset();
          setEditing(true);
        }}
        value={atiNumber.value ?? ''}
      />
    );
  }

  return (
    <AnswerForm
      error={errors ? null : save.error}
      heading="Your ATI number"
      hint="The Automated Transaction Identifier the police department gave you at your fingerprinting appointment."
      isPending={save.isPending}
      onCancel={atiNumber.value ? () => setEditing(false) : undefined}
      onSubmit={() =>
        save.mutate(value, {
          onSuccess: () => {
            setEditing(false);
            onDone('Thanks. We have your ATI number.');
          },
        })
      }
    >
      <Field
        error={errors?.atiNumber}
        label="ATI number"
        onChange={setValue}
        value={value}
      />
    </AnswerForm>
  );
}

function Saved({
  label,
  onChange,
  value,
}: {
  label: string;
  onChange: () => void;
  value: string;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 rounded-box border border-base-300 p-3">
      <p className="text-sm">
        <span className="font-semibold">{label}:</span> {value}
      </p>
      <button className="btn btn-ghost btn-sm" onClick={onChange} type="button">
        Change
      </button>
    </div>
  );
}

function AnswerForm({
  children,
  error,
  heading,
  hint,
  isPending,
  onCancel,
  onSubmit,
}: {
  children: ReactNode;
  error: Error | null;
  heading: string;
  hint?: string;
  isPending: boolean;
  onCancel?: () => void;
  onSubmit: () => void;
}) {
  const headingId = useId();
  return (
    <form
      aria-labelledby={headingId}
      className="space-y-3 rounded-box border border-base-300 p-3"
      noValidate
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit();
      }}
    >
      <div>
        <p className="font-semibold" id={headingId}>
          {heading}
        </p>
        {hint && <p className="text-sm text-base-content/70">{hint}</p>}
      </div>
      <div className="grid gap-3 sm:grid-cols-2">{children}</div>
      {error && <Failure error={error} />}
      <div className="flex flex-wrap gap-2">
        <button
          className="btn btn-primary btn-sm"
          disabled={isPending}
          type="submit"
        >
          Send
        </button>
        {onCancel && (
          <button
            className="btn btn-ghost btn-sm"
            onClick={onCancel}
            type="button"
          >
            Cancel
          </button>
        )}
      </div>
    </form>
  );
}

function Field({
  error,
  label,
  onChange,
  type = 'text',
  value,
}: {
  error?: string[];
  label: string;
  onChange: (value: string) => void;
  type?: 'email' | 'text';
  value: string;
}) {
  const id = useId();
  return (
    <div className="form-control">
      <label className="label py-1 text-sm" htmlFor={id}>
        {label}
      </label>
      <input
        aria-describedby={error ? `${id}-error` : undefined}
        aria-invalid={error ? true : undefined}
        className={`input input-bordered input-sm ${error ? 'input-error' : ''}`}
        id={id}
        onChange={(e) => onChange(e.target.value)}
        type={type}
        value={value}
      />
      {error && (
        <p className="mt-1 text-sm text-error" id={`${id}-error`}>
          {error.join(' ')}
        </p>
      )}
    </div>
  );
}

function Failure({ error }: { error: Error }) {
  return (
    <div className="alert alert-error py-2 text-sm" role="alert">
      {errorMessage(error)}
    </div>
  );
}

/** The server's field errors from a 400, keyed as the request named them. */
const fieldErrors = (error: Error | null) =>
  error instanceof HttpError && error.status === 400
    ? (error.body as { errors?: Record<string, string[]> }).errors
    : undefined;
