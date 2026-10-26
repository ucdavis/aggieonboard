import { useQuery, useQueryClient } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import { HttpError } from '@/lib/api.ts';
import { formatDate } from '@/lib/journey.ts';
import {
  portalKeys,
  portalQueryOptions,
  type PortalStep,
  type PortalView,
  useSignOut,
} from '@/queries/portal.ts';
import { PortalFrame } from '@/shared/portal/PortalFrame.tsx';
import { SignedOut } from '@/shared/portal/SignedOut.tsx';
import { Due, ViewedBy } from '@/shared/portal/TaskParts.tsx';
import { callToAction } from '@/shared/portal/tasks.ts';

type PortalSearch = { completed?: string };

export const Route = createFileRoute('/portal/')({
  component: PortalPage,
  validateSearch: (search: Record<string, unknown>): PortalSearch => ({
    completed:
      typeof search.completed === 'string' ? search.completed : undefined,
  }),
});

/**
 * The new employee's one place: the next thing to do, front and centre, with what else is open now
 * below it. Finishing a task brings them back here with the next one waiting. Later stages stay
 * out of sight until they open; nothing here names a vendor.
 */
function PortalPage() {
  const { data: view, error, isPending } = useQuery(portalQueryOptions());
  const { completed } = Route.useSearch();
  const signOut = useSignOut();
  const queryClient = useQueryClient();

  if (error instanceof HttpError && error.status === 401) {
    return (
      <PortalFrame>
        <SignedOut />
      </PortalFrame>
    );
  }

  const actions = view && (
    <button
      className="btn btn-ghost btn-sm"
      disabled={signOut.isPending}
      onClick={() =>
        signOut.mutate(undefined, {
          onSuccess: () =>
            queryClient.removeQueries({ queryKey: portalKeys.all }),
        })
      }
      type="button"
    >
      Sign out
    </button>
  );

  return (
    <PortalFrame actions={actions}>
      {isPending ? (
        <div className="flex justify-center py-16">
          <span className="loading loading-spinner loading-lg" />
        </div>
      ) : error ? (
        <div className="alert alert-error" role="alert">
          Your checklist couldn&apos;t be loaded. Refresh the page to try again.
        </div>
      ) : (
        <Checklist completed={completed} view={view} />
      )}
    </PortalFrame>
  );
}

function Checklist({
  completed,
  view,
}: {
  completed?: string;
  view: PortalView;
}) {
  const toDo = view.steps.filter((s) => s.state === 'ToDo');
  const working = view.steps.filter((s) => s.state === 'InProgress');
  const done = view.steps.filter((s) => s.state === 'Done');
  const [next, ...alsoNow] = toDo;
  const finished = view.steps.find((s) => s.key === completed);

  return (
    <div className="space-y-6">
      <ViewedBy view={view} />

      {finished && (
        <div className="alert alert-success" role="status">
          <span>
            <span className="font-semibold">Done: {finished.taskLabel}.</span>{' '}
            {next
              ? `Next up: ${next.taskLabel}.`
              : "That's everything for now. We'll email you when there's more."}
          </span>
        </div>
      )}

      <header className="space-y-3">
        <h1 className="text-3xl font-bold">Welcome, {view.firstName}</h1>
        <p className="text-lg">
          Your onboarding for{' '}
          <span className="font-semibold">{view.positionTitle}</span>,{' '}
          {view.department}.
        </p>
        <Progress done={view.stepsDone} total={view.stepsTotal} />
        {view.anticipatedStartDate && (
          <p className="text-sm text-base-content/70">
            Anticipated start date: {formatDate(view.anticipatedStartDate)}
          </p>
        )}
      </header>

      {next ? (
        <NextStep step={next} />
      ) : (
        <section className="rounded-box bg-base-100 p-6 text-center shadow-sm">
          <h2 className="text-xl font-bold">You&apos;re all caught up</h2>
          <p className="mt-2">
            {working.length > 0
              ? "We're working on what you've done. We'll email you when there's something new to do."
              : "We'll email you when there's something new to do."}
          </p>
        </section>
      )}

      {alsoNow.length > 0 && (
        <section aria-labelledby="also-heading" className="space-y-3">
          <h2 className="text-xl font-bold" id="also-heading">
            Also to do now
          </h2>
          <ul className="space-y-2">
            {alsoNow.map((step) => (
              <li
                className="flex flex-wrap items-center justify-between gap-3 rounded-box bg-base-100 p-4 shadow-sm"
                key={step.key}
              >
                <div>
                  <p className="font-semibold">{step.taskLabel}</p>
                  <Due step={step} />
                </div>
                <TaskLink step={step} />
              </li>
            ))}
          </ul>
        </section>
      )}

      {working.length > 0 && (
        <section aria-labelledby="working-heading" className="space-y-3">
          <h2 className="text-xl font-bold" id="working-heading">
            We&apos;re working on it
          </h2>
          <ul className="space-y-2">
            {working.map((step) => (
              <li
                className="rounded-box bg-base-100 p-4 shadow-sm"
                key={step.key}
              >
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <p className="font-semibold">{step.taskLabel}</p>
                  <Link
                    aria-label={`View ${step.taskLabel}`}
                    className="link link-primary text-sm"
                    params={{ taskKey: step.key }}
                    to="/portal/tasks/$taskKey"
                  >
                    View
                  </Link>
                </div>
                <p className="text-sm text-base-content/70">
                  You&apos;ve done your part. We&apos;ll let you know if we need
                  anything else.
                </p>
              </li>
            ))}
          </ul>
        </section>
      )}

      {done.length > 0 && (
        <details className="rounded-box bg-base-100 p-4 shadow-sm">
          <summary className="cursor-pointer font-semibold">
            Done ({done.length})
          </summary>
          <ul className="mt-2 space-y-1">
            {done.map((step) => (
              <li className="flex items-center gap-2" key={step.key}>
                <span aria-hidden="true" className="text-success">
                  ✓
                </span>
                {step.taskLabel}
              </li>
            ))}
          </ul>
        </details>
      )}

      <p className="text-sm text-base-content/70">
        More steps appear here as you finish these.
      </p>
      {view.contact && (
        <p className="rounded-box bg-base-100 p-4 text-sm shadow-sm">
          Questions? Contact {view.contact.name} at {view.contact.details}.
        </p>
      )}
    </div>
  );
}

function Progress({ done, total }: { done: number; total: number }) {
  if (total === 0) {
    return null;
  }
  return (
    <div className="space-y-1">
      <progress
        aria-label="Onboarding progress"
        className="progress progress-primary w-full"
        max={total}
        value={done}
      />
      <p className="text-sm">
        {done} of {total} steps done
      </p>
    </div>
  );
}

function NextStep({ step }: { step: PortalStep }) {
  return (
    <section
      aria-labelledby="next-heading"
      className="space-y-3 rounded-box border-l-4 border-primary bg-base-100 p-6 shadow-sm"
    >
      <p
        className="text-sm font-semibold uppercase tracking-wide text-primary"
        id="next-heading"
      >
        Your next step
      </p>
      <h2 className="text-2xl font-bold">{step.taskLabel}</h2>
      <Due step={step} />
      <TaskLink large step={step} />
    </section>
  );
}

function TaskLink({ large, step }: { large?: boolean; step: PortalStep }) {
  return (
    <Link
      className={`btn btn-primary ${large ? 'btn-lg w-full sm:w-auto' : 'btn-sm'}`}
      params={{ taskKey: step.key }}
      to="/portal/tasks/$taskKey"
    >
      {callToAction(step.taskLabel)}
    </Link>
  );
}
