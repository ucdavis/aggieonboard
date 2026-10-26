import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { createFileRoute, Link } from '@tanstack/react-router';
import { HttpError } from '@/lib/api.ts';
import { formatDate, formatDue } from '@/lib/journey.ts';
import {
  portalKeys,
  portalQueryOptions,
  type PortalStep,
  type PortalStepState,
  useSignOut,
} from '@/queries/portal.ts';
import { PortalFrame } from '@/shared/portal/PortalFrame.tsx';
import { SignedOut } from '@/shared/portal/SignedOut.tsx';
import {
  AtiNumberForm,
  I9Choice,
  RepresentativeForm,
} from '@/shared/portal/StepAnswers.tsx';
import { StepBooking } from '@/shared/portal/StepBooking.tsx';

export const Route = createFileRoute('/portal/')({
  component: PortalPage,
});

const sections: { empty?: string; heading: string; state: PortalStepState }[] =
  [
    {
      empty:
        "You're all caught up. We'll email you when there's something to do.",
      heading: 'To do',
      state: 'ToDo',
    },
    { heading: 'In progress', state: 'InProgress' },
    { heading: 'Coming up', state: 'Upcoming' },
  ];

function PortalPage() {
  const { data: view, error, isPending } = useQuery(portalQueryOptions());
  const signOut = useSignOut();
  const queryClient = useQueryClient();
  const [notice, setNotice] = useState<string | null>(null);

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
        <div className="space-y-8">
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
          <div className="space-y-2">
            <h1 className="text-3xl font-bold">Your onboarding checklist</h1>
            <p className="text-lg">
              Hi {view.firstName}. This is everything for your{' '}
              <span className="font-semibold">{view.positionTitle}</span>{' '}
              position with {view.department}, in one place.
            </p>
            {view.anticipatedStartDate && (
              <dl className="grid gap-x-4 gap-y-1 rounded-box bg-base-100 p-4 sm:grid-cols-[auto_1fr]">
                <dt className="font-medium">Anticipated start date</dt>
                <dd>{formatDate(view.anticipatedStartDate)}</dd>
                {view.clearanceDeadline && (
                  <>
                    <dt className="font-medium">Clearances complete by</dt>
                    <dd>{formatDate(view.clearanceDeadline)}</dd>
                  </>
                )}
              </dl>
            )}
          </div>

          {sections.map(({ empty, heading, state }) => {
            const steps = view.steps.filter((s) => s.state === state);
            if (!steps.length && !empty) {
              return null;
            }
            return (
              <section
                aria-labelledby={`${state}-heading`}
                className="space-y-3"
                key={state}
              >
                <h2 className="text-xl font-bold" id={`${state}-heading`}>
                  {heading}
                  {steps.length > 0 && (
                    <span className="ml-2 text-base font-normal text-base-content/60">
                      {steps.length}
                    </span>
                  )}
                </h2>
                {steps.length ? (
                  <ul className="space-y-3">
                    {steps.map((step) => (
                      <StepCard key={step.key} onDone={setNotice} step={step} />
                    ))}
                  </ul>
                ) : (
                  <p className="rounded-box bg-base-100 p-4">{empty}</p>
                )}
              </section>
            );
          })}

          <DoneSection steps={view.steps.filter((s) => s.state === 'Done')} />
        </div>
      )}
    </PortalFrame>
  );
}

function StepCard({
  onDone,
  step,
}: {
  onDone: (message: string) => void;
  step: PortalStep;
}) {
  return (
    <li className="card bg-base-100 shadow-sm">
      <div className="card-body gap-3 p-4 sm:p-6">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <h3 className="text-lg font-semibold">{step.title}</h3>
          {step.state === 'InProgress' && (
            <span className="badge badge-info">Nothing to do right now</span>
          )}
        </div>
        {step.dueDate && step.state !== 'InProgress' && (
          <p className={step.isOverdue ? 'font-semibold text-error' : ''}>
            {step.isOverdue ? 'Past due: ' : 'Due '}
            {formatDue(step.dueDate, step.dueTime)}
          </p>
        )}
        {step.i9Choice ? (
          <I9Choice
            booking={step.booking}
            choice={step.i9Choice}
            onDone={onDone}
          />
        ) : (
          step.booking && <StepBooking booking={step.booking} onDone={onDone} />
        )}
        {step.representative && (
          <RepresentativeForm
            onDone={onDone}
            representative={step.representative}
          />
        )}
        {step.atiNumber && (
          <AtiNumberForm atiNumber={step.atiNumber} onDone={onDone} />
        )}
        {step.watchFor && (
          <div className="rounded-box bg-base-200 p-3 text-sm">
            <p>
              Watch for an email from{' '}
              <span className="font-semibold">{step.watchFor.sender}</span>
              {step.watchFor.fromAddress && <> ({step.watchFor.fromAddress})</>}
              {step.watchFor.subject && (
                <> with the subject “{step.watchFor.subject}”</>
              )}
              . Check your spam or junk folder if you don&apos;t see it.
            </p>
          </div>
        )}
        {step.messages.length > 0 && (
          <ul className="space-y-1 text-sm">
            {step.messages.map((m) => (
              <li key={m.id}>
                <Link
                  className="link link-primary"
                  params={{ messageId: String(m.id) }}
                  to="/portal/messages/$messageId"
                >
                  Read our email: {m.subject}
                </Link>
              </li>
            ))}
          </ul>
        )}
        {step.contact && (
          <p className="text-sm text-base-content/80">
            Questions? Contact {step.contact.name} at {step.contact.details}.
          </p>
        )}
      </div>
    </li>
  );
}

function DoneSection({ steps }: { steps: PortalStep[] }) {
  if (!steps.length) {
    return null;
  }
  return (
    <section aria-labelledby="done-heading" className="space-y-3">
      <h2 className="text-xl font-bold" id="done-heading">
        Done
        <span className="ml-2 text-base font-normal text-base-content/60">
          {steps.length}
        </span>
      </h2>
      <ul className="rounded-box bg-base-100 p-4">
        {steps.map((step) => (
          <li className="flex items-center gap-2 py-1" key={step.key}>
            <span aria-hidden="true" className="text-success">
              ✓
            </span>
            {step.title}
          </li>
        ))}
      </ul>
    </section>
  );
}
