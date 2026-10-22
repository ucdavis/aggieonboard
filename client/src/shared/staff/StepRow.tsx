import { useId, useState } from 'react';
import {
  actionLabel,
  errorMessage,
  formatDue,
  formatTimestamp,
  roleLabel,
  statusBadge,
  statusLabel,
} from '@/lib/journey.ts';
import {
  type StepAction,
  type StepDetail,
  useStepAction,
  useStepDue,
  useStepNote,
} from '@/queries/staff.ts';

const actionStyle: Record<StepAction, string> = {
  Activate: 'btn-primary',
  Block: 'btn-ghost text-error',
  Complete: 'btn-success',
  MarkWaiting: 'btn-outline',
  Reopen: 'btn-ghost',
  Unblock: 'btn-outline',
};

/** One step of one candidate's journey, with the moves its status allows. */
export function StepRow({
  candidateId,
  step,
}: {
  candidateId: number;
  step: StepDetail;
}) {
  const action = useStepAction(candidateId);
  const due = useStepDue(candidateId);
  const note = useStepNote(candidateId);
  const [editing, setEditing] = useState<'due' | 'note' | null>(null);
  const error = action.error ?? due.error ?? note.error;
  const skipped = step.status === 'Skipped';
  const headingId = useId();

  const reset = () => {
    action.reset();
    due.reset();
    note.reset();
  };

  return (
    <li
      aria-labelledby={headingId}
      className={`rounded-box border border-base-300 p-4 ${
        skipped ? 'bg-base-200/60 text-base-content/60' : 'bg-base-100'
      }`}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 space-y-1">
          <h3 className="font-semibold" id={headingId}>
            <span className="mr-2 text-base-content/50">{step.order}.</span>
            {step.title}
          </h3>
          <p className="text-xs text-base-content/60">
            Audit #{step.auditReference} · {roleLabel[step.owner]}
            {step.watchForSender &&
              ` · Candidate watches for ${step.watchForSender}`}
          </p>
        </div>
        <span className={`badge ${statusBadge[step.status]}`}>
          {statusLabel[step.status]}
        </span>
      </div>

      {!skipped && (
        <dl className="mt-3 grid gap-x-6 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
          <dt className="text-base-content/60">Due</dt>
          <dd className={step.isOverdue ? 'font-semibold text-error' : ''}>
            {formatDue(step.dueDate, step.dueTime)}
            {step.isOverdue && (
              <span className="badge badge-error badge-sm ml-2">Overdue</span>
            )}
            {step.dueOverridden && (
              <span className="ml-2 text-xs text-base-content/60">
                (set by hand)
              </span>
            )}
          </dd>
          <dt className="text-base-content/60">Rule</dt>
          <dd>
            {step.deadlineRule}
            {!step.deadlineConfirmed && (
              <span className="badge badge-warning badge-outline badge-sm ml-2">
                Unconfirmed
              </span>
            )}
          </dd>
          {!step.deadlineConfirmed && step.deadlineQuestion && (
            <>
              <dt className="text-base-content/60">Open question</dt>
              <dd className="text-base-content/80">{step.deadlineQuestion}</dd>
            </>
          )}
          {step.completedAt && (
            <>
              <dt className="text-base-content/60">Completed</dt>
              <dd>
                {formatTimestamp(step.completedAt)}
                {step.completedBy && ` by ${step.completedBy}`}
              </dd>
            </>
          )}
          {step.staffNote && editing !== 'note' && (
            <>
              <dt className="text-base-content/60">Staff note</dt>
              <dd className="whitespace-pre-line">{step.staffNote}</dd>
            </>
          )}
        </dl>
      )}

      {error && (
        <div className="alert alert-error mt-3 py-2 text-sm" role="alert">
          {errorMessage(error)}
        </div>
      )}

      {editing === 'due' && (
        <DueEditor
          onCancel={() => setEditing(null)}
          onSave={(date) =>
            due.mutate(
              { date, stepKey: step.key },
              { onSuccess: () => setEditing(null) }
            )
          }
          pending={due.isPending}
          step={step}
        />
      )}
      {editing === 'note' && (
        <NoteEditor
          initial={step.staffNote ?? ''}
          onCancel={() => setEditing(null)}
          onSave={(value) =>
            note.mutate(
              { note: value || null, stepKey: step.key },
              { onSuccess: () => setEditing(null) }
            )
          }
          pending={note.isPending}
        />
      )}

      {!skipped && editing === null && (
        <div className="mt-3 flex flex-wrap gap-2">
          {step.availableActions.map((move) => (
            <button
              className={`btn btn-sm ${actionStyle[move]}`}
              disabled={action.isPending}
              key={move}
              onClick={() => {
                reset();
                action.mutate({ action: move, stepKey: step.key });
              }}
              type="button"
            >
              {actionLabel[move]}
            </button>
          ))}
          {step.status !== 'Complete' && (
            <button
              className="btn btn-sm btn-ghost"
              onClick={() => {
                reset();
                setEditing('due');
              }}
              type="button"
            >
              Change due date
            </button>
          )}
          <button
            className="btn btn-sm btn-ghost"
            onClick={() => {
              reset();
              setEditing('note');
            }}
            type="button"
          >
            {step.staffNote ? 'Edit note' : 'Add note'}
          </button>
        </div>
      )}
    </li>
  );
}

function DueEditor({
  onCancel,
  onSave,
  pending,
  step,
}: {
  onCancel: () => void;
  onSave: (date: string | null) => void;
  pending: boolean;
  step: StepDetail;
}) {
  const [value, setValue] = useState(step.dueDate ?? '');
  const id = useId();
  return (
    <form
      className="mt-3 flex flex-wrap items-end gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        if (value) {
          onSave(value);
        }
      }}
    >
      <div className="form-control">
        <label className="label py-1 text-sm" htmlFor={id}>
          Due date
        </label>
        <input
          className="input input-bordered input-sm"
          id={id}
          onChange={(e) => setValue(e.target.value)}
          type="date"
          value={value}
        />
      </div>
      <button
        className="btn btn-sm btn-primary"
        disabled={pending || !value}
        type="submit"
      >
        Save
      </button>
      {step.dueOverridden && (
        <button
          className="btn btn-sm btn-ghost"
          disabled={pending}
          onClick={() => onSave(null)}
          type="button"
        >
          Use the rule again
        </button>
      )}
      <button className="btn btn-sm btn-ghost" onClick={onCancel} type="button">
        Cancel
      </button>
    </form>
  );
}

function NoteEditor({
  initial,
  onCancel,
  onSave,
  pending,
}: {
  initial: string;
  onCancel: () => void;
  onSave: (value: string) => void;
  pending: boolean;
}) {
  const [value, setValue] = useState(initial);
  const id = useId();
  return (
    <form
      className="mt-3 space-y-2"
      onSubmit={(e) => {
        e.preventDefault();
        onSave(value.trim());
      }}
    >
      <label className="label py-1 text-sm" htmlFor={id}>
        Staff note (never shown to the candidate)
      </label>
      <textarea
        className="textarea textarea-bordered w-full"
        id={id}
        maxLength={2000}
        onChange={(e) => setValue(e.target.value)}
        rows={3}
        value={value}
      />
      <div className="flex gap-2">
        <button
          className="btn btn-sm btn-primary"
          disabled={pending}
          type="submit"
        >
          Save note
        </button>
        <button
          className="btn btn-sm btn-ghost"
          onClick={onCancel}
          type="button"
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
