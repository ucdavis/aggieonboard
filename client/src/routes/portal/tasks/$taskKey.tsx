import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';
import { HttpError } from '@/lib/api.ts';
import { errorMessage } from '@/lib/journey.ts';
import {
  type PortalStep,
  portalQueryOptions,
  portalTaskQueryOptions,
  useMarkTaskDone,
} from '@/queries/portal.ts';
import { PortalFrame } from '@/shared/portal/PortalFrame.tsx';
import { SignedOut } from '@/shared/portal/SignedOut.tsx';
import {
  AtiNumberForm,
  I9Choice,
  RepresentativeForm,
} from '@/shared/portal/StepAnswers.tsx';
import { StepBooking } from '@/shared/portal/StepBooking.tsx';
import { Due, ViewedBy } from '@/shared/portal/TaskParts.tsx';
import { callToAction } from '@/shared/portal/tasks.ts';

export const Route = createFileRoute('/portal/tasks/$taskKey')({
  component: TaskPage,
});

/**
 * One task: what our email about it says, then the way to do it. Booking and answers happen here;
 * a task done in another system offers a stand-in for it until that system is connected. Either
 * way, finishing returns to the checklist with the next task waiting.
 */
function TaskPage() {
  const { taskKey } = Route.useParams();
  const navigate = useNavigate();
  const { data: view, error } = useQuery(portalQueryOptions());
  const step = view?.steps.find((s) => s.key === taskKey);
  const finish = () =>
    navigate({ search: { completed: taskKey }, to: '/portal' });

  if (error instanceof HttpError && error.status === 401) {
    return (
      <PortalFrame>
        <SignedOut />
      </PortalFrame>
    );
  }

  return (
    <PortalFrame>
      <div className="space-y-6">
        <Link className="link" to="/portal">
          ← Your checklist
        </Link>
        {error ? (
          <div className="alert alert-error" role="alert">
            Your checklist couldn&apos;t be loaded. Refresh the page to try
            again.
          </div>
        ) : !view ? (
          <span className="loading loading-spinner" />
        ) : !step ? (
          <div className="alert alert-info" role="status">
            This step isn&apos;t open yet. It will appear in your checklist when
            it is.
          </div>
        ) : (
          <>
            <ViewedBy view={view} />
            <header className="space-y-1">
              <h1 className="text-3xl font-bold">{step.taskLabel}</h1>
              <Due step={step} />
            </header>
            {step.state === 'InProgress' && (
              <div className="alert alert-info" role="status">
                You&apos;ve done your part. We&apos;ll let you know if we need
                anything else.
              </div>
            )}
            {step.state === 'Done' && (
              <div className="alert alert-success" role="status">
                This step is done.
              </div>
            )}
            <Instructions step={step} />
            {step.state !== 'Done' && <Action onDone={finish} step={step} />}
            {view.contact && (
              <p className="text-sm text-base-content/70">
                Questions? Contact {view.contact.name} at {view.contact.details}
                .
              </p>
            )}
          </>
        )}
      </div>
    </PortalFrame>
  );
}

/** The text of the task's email, filled in for them; or its summary when it has no email of its own. */
function Instructions({ step }: { step: PortalStep }) {
  const { data } = useQuery(portalTaskQueryOptions(step.key));
  if (data?.html) {
    return (
      <article
        className="space-y-3 rounded-box bg-base-100 p-5 shadow-sm [&_a]:text-primary [&_a]:underline [&_li]:my-1 [&_ol]:list-decimal [&_ol]:pl-6 [&_strong]:font-semibold [&_ul]:list-disc [&_ul]:pl-6"
        // Rendered on the server from the message's Markdown, with raw HTML disabled and every
        // merged value escaped; links are limited to http(s) and mailto (TemplateAdmin).
        dangerouslySetInnerHTML={{ __html: data.html }}
      />
    );
  }
  return step.summary ? (
    <p className="rounded-box bg-base-100 p-5 text-lg shadow-sm">
      {step.summary}
    </p>
  ) : null;
}

function Action({ onDone, step }: { onDone: () => void; step: PortalStep }) {
  const [notice, setNotice] = useState<string | null>(null);
  // Booking finishes the task; cancelling leaves them here to book again.
  const booked = (message: string, outcome?: 'booked' | 'cancelled') =>
    outcome === 'cancelled' ? setNotice(message) : onDone();

  let action = null;
  if (step.kind === 'Schedule') {
    action = (
      <div className="space-y-3">
        {step.booking && <StepBooking booking={step.booking} onDone={booked} />}
        {step.atiNumber && (
          <AtiNumberForm atiNumber={step.atiNumber} onDone={() => onDone()} />
        )}
      </div>
    );
  } else if (step.kind === 'I9' && step.i9Choice) {
    action = (
      <I9Choice booking={step.booking} choice={step.i9Choice} onDone={booked} />
    );
  } else if (step.kind === 'Representative' && step.representative) {
    action = (
      <RepresentativeForm
        onDone={() => onDone()}
        representative={step.representative}
      />
    );
  } else if (step.kind === 'External' && step.state === 'ToDo') {
    action = <ElsewhereTask onDone={onDone} step={step} />;
  }

  return (
    <div className="space-y-3">
      {notice && (
        <div className="alert alert-info" role="status">
          {notice}
        </div>
      )}
      {action}
    </div>
  );
}

/**
 * A task done in another system today (a form, an e-signature). Until that system is connected,
 * the button opens a stand-in and the new employee says when it's done.
 */
function ElsewhereTask({
  onDone,
  step,
}: {
  onDone: () => void;
  step: PortalStep;
}) {
  const [open, setOpen] = useState(false);
  const done = useMarkTaskDone();

  if (!open) {
    return (
      <button
        className="btn btn-primary btn-lg w-full sm:w-auto"
        onClick={() => setOpen(true)}
        type="button"
      >
        {callToAction(step.taskLabel)}
      </button>
    );
  }

  return (
    <section
      aria-label={step.taskLabel}
      className="space-y-3 rounded-box border-2 border-dashed border-primary/40 bg-base-100 p-5"
    >
      <p className="font-semibold">This is where you&apos;ll complete it.</p>
      <p className="text-sm text-base-content/70">
        In the finished checklist, this opens right here. For this preview,
        complete it the way the instructions above describe, then come back and
        tell us it&apos;s done.
      </p>
      {done.error && (
        <div className="alert alert-error py-2 text-sm" role="alert">
          {errorMessage(done.error)}
        </div>
      )}
      <div className="flex flex-wrap gap-2">
        <button
          className="btn btn-primary"
          disabled={done.isPending}
          onClick={() => done.mutate(step.key, { onSuccess: onDone })}
          type="button"
        >
          I&apos;ve done this
        </button>
        <button
          className="btn btn-ghost"
          onClick={() => setOpen(false)}
          type="button"
        >
          Not yet
        </button>
      </div>
    </section>
  );
}
