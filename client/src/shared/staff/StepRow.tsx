import { useEffect, useId, useRef, useState } from 'react';
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
import { MessagePreviewDialog } from './MessageDialogs.tsx';

const actionStyle: Record<StepAction, string> = {
  Activate: 'btn-primary',
  Block: 'btn-ghost text-error',
  Complete: 'btn-success',
  MarkWaiting: 'btn-outline',
  Reopen: 'btn-ghost',
  Unblock: 'btn-outline',
};

/** Where a step stands, in a few words, for the collapsed timeline line. */
const standing = (step: StepDetail) => {
  switch (step.status) {
    case 'Complete':
      return step.completedAt
        ? `Done ${formatTimestamp(step.completedAt)}`
        : 'Done';
    case 'Waiting':
      return `Waiting on ${roleLabel[step.owner]} to confirm`;
    case 'Blocked':
      return 'Blocked: see the staff note';
    case 'ActionNeeded':
      return step.dueDate
        ? `With the new employee · due ${formatDue(step.dueDate, step.dueTime)}`
        : 'With the new employee';
    case 'Skipped':
      return 'Not needed for this position';
    default:
      return 'Opens when the earlier steps are done';
  }
};

const marker: Record<StepDetail['status'], string> = {
  ActionNeeded: 'border-info bg-info text-info-content',
  Blocked: 'border-error bg-error text-error-content',
  Complete: 'border-success bg-success text-success-content',
  Skipped: 'border-base-300 bg-base-200 text-base-content/40',
  Upcoming: 'border-base-300 bg-base-100 text-base-content/40',
  Waiting: 'border-warning bg-warning text-warning-content',
};

/**
 * One step of one candidate's journey, as a line on their timeline. Click it to open the details
 * and the moves its status allows.
 */
export function StepRow({
  candidateId,
  defaultOpen = false,
  focused = false,
  last = false,
  step,
}: {
  candidateId: number;
  defaultOpen?: boolean;
  /** Linked to (?step=): open and scrolled into view. */
  focused?: boolean;
  last?: boolean;
  step: StepDetail;
}) {
  const item = useRef<HTMLLIElement>(null);
  useEffect(() => {
    if (focused) {
      item.current?.scrollIntoView?.({ block: 'center' });
    }
  }, [focused]);
  const action = useStepAction(candidateId);
  const due = useStepDue(candidateId);
  const note = useStepNote(candidateId);
  const [open, setOpen] = useState(defaultOpen || focused);
  const [editing, setEditing] = useState<'due' | 'note' | null>(null);
  const [previewing, setPreviewing] = useState<string | null>(null);
  const [sentNotice, setSentNotice] = useState<string | null>(null);
  const error = action.error ?? due.error ?? note.error;
  // Reported by the server after a move with a side effect (message 5), or after a send from here.
  const notice = sentNotice ?? action.data?.notice ?? null;
  const skipped = step.status === 'Skipped';
  const headingId = useId();
  const panelId = useId();

  const reset = () => {
    setSentNotice(null);
    action.reset();
    due.reset();
    note.reset();
  };

  return (
    <li
      aria-labelledby={headingId}
      className="grid grid-cols-[1.75rem_minmax(0,1fr)] gap-x-3"
      id={`step-${step.key}`}
      ref={item}
    >
      <div aria-hidden="true" className="flex flex-col items-center">
        <span
          className={`mt-1 flex size-6 items-center justify-center rounded-full border-2 text-xs font-bold ${marker[step.status]}`}
        >
          {step.status === 'Complete' ? '✓' : step.order}
        </span>
        {!last && <span className="w-0.5 grow bg-base-300" />}
      </div>
      <div className={`pb-4 ${skipped ? 'text-base-content/60' : ''}`}>
        <h3 className="font-semibold" id={headingId}>
          <button
            aria-controls={panelId}
            aria-expanded={open}
            className="flex w-full flex-wrap items-start justify-between gap-2 rounded-box px-2 py-1 text-left hover:bg-base-200"
            onClick={() => setOpen((o) => !o)}
            type="button"
          >
            <span className="min-w-0">
              {step.title}
              <span className="block text-sm font-normal text-base-content/70">
                {standing(step)}
                {step.isOverdue && (
                  <span className="badge badge-error badge-sm ml-2">
                    Overdue
                  </span>
                )}
              </span>
            </span>
            <span className={`badge ${statusBadge[step.status]}`}>
              {statusLabel[step.status]}
            </span>
          </button>
        </h3>
        {open && (
          <div
            className="mt-2 rounded-box border border-base-300 bg-base-100 p-4"
            id={panelId}
          >
            <p className="text-xs text-base-content/60">
              Step {step.order} · stage {step.stage} · audit #
              {step.auditReference} · {roleLabel[step.owner]}
            </p>
            {!skipped && (
              <dl className="mt-3 grid gap-x-6 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
                <dt className="text-base-content/60">Due</dt>
                <dd
                  className={step.isOverdue ? 'font-semibold text-error' : ''}
                >
                  {formatDue(step.dueDate, step.dueTime)}
                  {step.isOverdue && (
                    <span className="badge badge-error badge-sm ml-2">
                      Overdue
                    </span>
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
                    <dd className="text-base-content/80">
                      {step.deadlineQuestion}
                    </dd>
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
            {notice && !error && (
              <div
                className={`alert mt-3 py-2 text-sm ${
                  notice.includes('not sent') ? 'alert-warning' : 'alert-info'
                }`}
                role="status"
              >
                {notice}
              </div>
            )}
            {step.acknowledgmentMessageKey &&
              step.status === 'ActionNeeded' && (
                <p className="mt-3 text-xs text-base-content/60">
                  “Candidate done” emails the candidate an acknowledgment that
                  their documents arrived.
                </p>
              )}
            {previewing && (
              <MessagePreviewDialog
                candidateId={candidateId}
                messageKey={previewing}
                onClose={() => setPreviewing(null)}
                onSent={(text) => {
                  setPreviewing(null);
                  setSentNotice(text);
                }}
              />
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
                {step.requestMessageKey && (
                  <button
                    className="btn btn-sm btn-secondary btn-outline"
                    onClick={() => {
                      reset();
                      setPreviewing(step.requestMessageKey);
                    }}
                    type="button"
                  >
                    Preview message
                  </button>
                )}
                {step.vendorMessageKey && (
                  <button
                    className="btn btn-sm btn-ghost"
                    onClick={() => {
                      reset();
                      setPreviewing(step.vendorMessageKey);
                    }}
                    type="button"
                  >
                    Vendor&apos;s wording
                  </button>
                )}
              </div>
            )}
          </div>
        )}
      </div>
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
