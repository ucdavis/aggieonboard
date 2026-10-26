import { formatDue } from '@/lib/journey.ts';
import {
  type PortalStep,
  type PortalView,
  useSignOut,
} from '@/queries/portal.ts';

/** For an admin viewing someone's checklist (Admin → Emulate): it's read-only. */
export function ViewedBy({ view }: { view: PortalView }) {
  const signOut = useSignOut();
  if (!view.viewedBy) {
    return null;
  }
  return (
    <div className="alert alert-warning" role="status">
      <span>
        You&apos;re viewing {view.firstName}&apos;s checklist as {view.viewedBy}
        . Read-only.
      </span>
      <button
        className="btn btn-sm"
        disabled={signOut.isPending}
        onClick={() =>
          signOut.mutate(undefined, {
            onSuccess: () => window.location.assign('/admin/emulate'),
          })
        }
        type="button"
      >
        Back to Admin
      </button>
    </div>
  );
}

export function Due({ step }: { step: PortalStep }) {
  if (!step.dueDate || step.state === 'Done') {
    return null;
  }
  return (
    <p
      className={`text-sm ${step.isOverdue ? 'font-semibold text-error' : 'text-base-content/70'}`}
    >
      {step.isOverdue ? 'Past due: ' : 'Due '}
      {formatDue(step.dueDate, step.dueTime)}
    </p>
  );
}
