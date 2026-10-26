import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useState } from 'react';
import { HttpError } from '@/lib/api.ts';
import {
  formatDate,
  formatTimestamp,
  i9Label,
  messageLabel,
  phaseLabel,
} from '@/lib/journey.ts';
import {
  candidateQueryOptions,
  type CandidateDetail,
  type JourneyPhase,
} from '@/queries/staff.ts';
import {
  MessagePreviewDialog,
  SentMessageDialog,
} from '@/shared/staff/MessageDialogs.tsx';
import { Appointments } from '@/shared/staff/Appointments.tsx';
import { StepRow } from '@/shared/staff/StepRow.tsx';

export const Route = createFileRoute(
  '/(authenticated)/candidates/$candidateId/'
)({
  component: CandidatePage,
});

const phases: JourneyPhase[] = [
  'TalentAcquisition',
  'PreEmployment',
  'FirstDay',
];

const requirementLabels: Record<keyof CandidateDetail['requirements'], string> =
  {
    dmvPullNotice: 'DMV Employer Pull Notice',
    drugScreen: 'Drug screen',
    fingerprinting: 'Fingerprinting (Live Scan)',
    parking: 'Complimentary parking',
    positionDescription: 'Position description',
  };

function CandidatePage() {
  const id = Number(Route.useParams().candidateId);
  const {
    data: candidate,
    error,
    isPending,
  } = useQuery(candidateQueryOptions(id));
  const [viewing, setViewing] = useState<number | null>(null);
  const [sendingLink, setSendingLink] = useState(false);
  const [linkNotice, setLinkNotice] = useState<string | null>(null);

  if (isPending) {
    return (
      <div className="flex justify-center py-16">
        <span className="loading loading-spinner loading-lg" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="alert alert-error" role="alert">
        {error instanceof HttpError && error.status === 404
          ? 'There is no candidate with that ID.'
          : 'This candidate could not be loaded. Refresh to try again.'}{' '}
        <Link className="link" to="/">
          Back to the pipeline
        </Link>
      </div>
    );
  }

  const requirements = (
    Object.keys(requirementLabels) as (keyof typeof requirementLabels)[]
  ).filter((key) => candidate.requirements[key]);

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link className="link link-hover text-sm" to="/">
            ← Pipeline
          </Link>
          <h1 className="mt-1 text-3xl font-bold">
            {candidate.firstName} {candidate.lastName}
          </h1>
          <p className="text-base-content/70">
            {candidate.positionTitle} · {candidate.department}
          </p>
          <p className="text-sm text-base-content/70">{candidate.email}</p>
        </div>
        <Link
          className="btn btn-outline btn-sm"
          params={{ candidateId: String(id) }}
          to="/candidates/$candidateId/edit"
        >
          Edit details
        </Link>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <section aria-labelledby="journey-heading" className="space-y-6">
          <h2 className="sr-only" id="journey-heading">
            Journey
          </h2>
          <Appointments candidateId={id} />
          {phases.map((phase) => (
            <div className="space-y-3" key={phase}>
              <h3 className="text-lg font-semibold">{phaseLabel[phase]}</h3>
              <ol className="space-y-3">
                {candidate.steps
                  .filter((step) => step.phase === phase)
                  .map((step) => (
                    <StepRow candidateId={id} key={step.key} step={step} />
                  ))}
              </ol>
            </div>
          ))}
        </section>

        <aside className="space-y-6">
          <section className="card bg-base-100 shadow-sm">
            <div className="card-body gap-3 text-sm">
              <h2 className="card-title text-base">Details</h2>
              <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2">
                <dt className="text-base-content/60">Start date</dt>
                <dd>{formatDate(candidate.anticipatedStartDate)}</dd>
                <dt className="text-base-content/60">Clearance by</dt>
                <dd>{formatDate(candidate.clearanceDeadline)}</dd>
                <dt className="text-base-content/60">Employee ID</dt>
                <dd>{candidate.employeeId ?? '—'}</dd>
                <dt className="text-base-content/60">I-9</dt>
                <dd>{i9Label[candidate.i9Method]}</dd>
                {candidate.i9RepresentativeName && (
                  <>
                    <dt className="text-base-content/60">I-9 representative</dt>
                    <dd>
                      {candidate.i9RepresentativeName} (
                      {candidate.i9RepresentativeEmail})
                    </dd>
                  </>
                )}
                {candidate.fingerprintAtiNumber && (
                  <>
                    <dt className="text-base-content/60">ATI number</dt>
                    <dd>{candidate.fingerprintAtiNumber}</dd>
                  </>
                )}
                <dt className="text-base-content/60">Hiring manager</dt>
                <dd>{candidate.hiringManagerContact ?? '—'}</dd>
              </dl>
            </div>
          </section>

          <section className="card bg-base-100 shadow-sm">
            <div className="card-body gap-3 text-sm">
              <h2 className="card-title text-base">Team</h2>
              <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2">
                <dt className="text-base-content/60">Recruiting assistant</dt>
                <dd>{candidate.recruitingAssistant?.name ?? '—'}</dd>
                <dt className="text-base-content/60">Recruiter</dt>
                <dd>{candidate.recruiter?.name ?? '—'}</dd>
                <dt className="text-base-content/60">Onboarding specialist</dt>
                <dd>{candidate.onboardingSpecialist?.name ?? '—'}</dd>
              </dl>
            </div>
          </section>

          <section className="card bg-base-100 shadow-sm">
            <div className="card-body gap-3 text-sm">
              <h2 className="card-title text-base">Position requires</h2>
              {requirements.length ? (
                <ul className="list-inside list-disc">
                  {requirements.map((key) => (
                    <li key={key}>{requirementLabels[key]}</li>
                  ))}
                </ul>
              ) : (
                <p className="text-base-content/70">
                  No additional requirements.
                </p>
              )}
              <h3 className="font-semibold">Qualifications to verify</h3>
              {candidate.qualifications.length ? (
                <ul className="space-y-1">
                  {candidate.qualifications.map((q, i) => (
                    <li key={i}>
                      {q.name}
                      <div className="text-xs text-base-content/60">
                        {q.kind}: {q.documentToSubmit}
                      </div>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="text-base-content/70">None.</p>
              )}
            </div>
          </section>

          <section className="card bg-base-100 shadow-sm">
            <div className="card-body gap-3 text-sm">
              <h2 className="card-title text-base">Portal</h2>
              <p>
                {candidate.portalLastSignInAt
                  ? `Last signed in ${formatTimestamp(candidate.portalLastSignInAt)}.`
                  : 'Has not signed in to the portal yet.'}
              </p>
              <p className="text-base-content/70">
                Every email includes a button to the candidate&apos;s checklist.
              </p>
              {linkNotice && (
                <div className="alert alert-info py-2 text-sm" role="status">
                  {linkNotice}
                </div>
              )}
              <button
                className="btn btn-outline btn-sm"
                onClick={() => {
                  setLinkNotice(null);
                  setSendingLink(true);
                }}
                type="button"
              >
                Email a sign-in link
              </button>
            </div>
          </section>
          {sendingLink && (
            <MessagePreviewDialog
              candidateId={id}
              messageKey="portal-link"
              onClose={() => setSendingLink(false)}
              onSent={() => {
                setSendingLink(false);
                setLinkNotice(`A sign-in link was sent to ${candidate.email}.`);
              }}
            />
          )}

          <section className="card bg-base-100 shadow-sm">
            <div className="card-body gap-3 text-sm">
              <h2 className="card-title text-base">Messages sent</h2>
              {candidate.sentMessages.length ? (
                <ol className="space-y-3">
                  {candidate.sentMessages.map((m) => (
                    <li key={m.id}>
                      <button
                        className="link link-hover text-left"
                        onClick={() => setViewing(m.id)}
                        type="button"
                      >
                        {m.subject}
                      </button>
                      <div className="text-xs text-base-content/60">
                        {messageLabel(m.auditReference)} · {m.sentBy}
                        {m.automatic && ' (automatic)'} ·{' '}
                        {formatTimestamp(m.sentAt)}
                      </div>
                    </li>
                  ))}
                </ol>
              ) : (
                <p className="text-base-content/70">Nothing sent yet.</p>
              )}
            </div>
          </section>
          {viewing !== null && (
            <SentMessageDialog
              candidateId={id}
              onClose={() => setViewing(null)}
              sentId={viewing}
            />
          )}

          <section className="card bg-base-100 shadow-sm">
            <div className="card-body gap-3 text-sm">
              <h2 className="card-title text-base">History</h2>
              {candidate.history.length ? (
                <ol className="space-y-3">
                  {candidate.history.map((entry, i) => (
                    <li key={i}>
                      <div>{entry.detail}</div>
                      <div className="text-xs text-base-content/60">
                        {entry.actorName} · {formatTimestamp(entry.at)}
                      </div>
                    </li>
                  ))}
                </ol>
              ) : (
                <p className="text-base-content/70">No changes recorded yet.</p>
              )}
            </div>
          </section>
        </aside>
      </div>
    </div>
  );
}
