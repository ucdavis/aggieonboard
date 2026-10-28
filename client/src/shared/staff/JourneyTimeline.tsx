import { useState } from 'react';
import { roleLabel } from '@/lib/journey.ts';
import type {
  CandidateDetail,
  StaffRole,
  StepDetail,
} from '@/queries/staff.ts';
import { StepRow } from './StepRow.tsx';

const groups: { responsible: keyof CandidateDetail; role: StaffRole }[] = [
  { responsible: 'recruiter', role: 'Recruiter' },
  { responsible: 'serviceChannel', role: 'ServiceChannel' },
  { responsible: 'unitHr', role: 'UnitHr' },
];

type GroupState = 'Done' | 'Now' | 'Later';

const groupBadge: Record<GroupState, string> = {
  Done: 'badge-success',
  Later: 'badge-ghost',
  Now: 'badge-info',
};

/**
 * A candidate's journey as one timeline per group (Recruiter, Service Channel, Unit HR), in order.
 * The group whose steps are under way is open; finished and later ones fold away. Steps waiting
 * on staff, and the one named in the link (?step=), open on their own.
 */
export function JourneyTimeline({
  candidate,
  focus,
}: {
  candidate: CandidateDetail;
  focus?: string;
}) {
  // The current group is the first, in journey order, with a step still to finish.
  const current = groups.find(({ role }) =>
    candidate.steps.some(
      (s) =>
        s.owner === role && s.status !== 'Complete' && s.status !== 'Skipped'
    )
  )?.role;

  return (
    <div className="space-y-4">
      {groups.map(({ responsible, role }) => {
        const steps = candidate.steps
          .filter((s) => s.owner === role)
          .sort((a, b) => a.order - b.order);
        if (steps.length === 0) {
          return null;
        }
        const person = candidate[responsible] as { name: string } | null;
        return (
          <GroupTimeline
            candidateId={candidate.id}
            current={role === current}
            focus={focus}
            key={role}
            person={person?.name ?? null}
            role={role}
            steps={steps}
          />
        );
      })}
    </div>
  );
}

function GroupTimeline({
  candidateId,
  current,
  focus,
  person,
  role,
  steps,
}: {
  candidateId: number;
  current: boolean;
  focus?: string;
  person: string | null;
  role: StaffRole;
  steps: StepDetail[];
}) {
  const applicable = steps.filter((s) => s.status !== 'Skipped');
  const skipped = steps.filter((s) => s.status === 'Skipped');
  const done = applicable.filter((s) => s.status === 'Complete').length;
  const state: GroupState =
    applicable.length > 0 && done === applicable.length
      ? 'Done'
      : current
        ? 'Now'
        : 'Later';
  const holdsFocus = steps.some((s) => s.key === focus);
  const [open, setOpen] = useState(state === 'Now' || holdsFocus);
  const [showSkipped, setShowSkipped] = useState(
    skipped.some((s) => s.key === focus)
  );
  const headingId = `group-${role}`;

  return (
    <section
      aria-labelledby={headingId}
      className="rounded-box bg-base-100 shadow-sm"
    >
      <h3 id={headingId}>
        <button
          aria-expanded={open}
          className="flex w-full flex-wrap items-center justify-between gap-3 p-4 text-left"
          onClick={() => setOpen((o) => !o)}
          type="button"
        >
          <span>
            <span className="text-lg font-semibold">{roleLabel[role]}</span>
            <span className="block text-sm font-normal text-base-content/70">
              {person ?? 'Nobody assigned'} · {done} of {applicable.length} done
            </span>
          </span>
          <span className={`badge ${groupBadge[state]}`}>
            {state === 'Now' ? 'Under way' : state}
          </span>
        </button>
      </h3>
      {open && (
        <div className="px-4 pb-2">
          <ol>
            {applicable.map((step, i) => (
              <StepRow
                candidateId={candidateId}
                defaultOpen={
                  step.status === 'Waiting' || step.status === 'Blocked'
                }
                focused={step.key === focus}
                key={step.key}
                last={i === applicable.length - 1}
                step={step}
              />
            ))}
          </ol>
          {skipped.length > 0 && (
            <div className="pb-3">
              <button
                aria-expanded={showSkipped}
                className="btn btn-ghost btn-xs"
                onClick={() => setShowSkipped((s) => !s)}
                type="button"
              >
                Not needed for this position ({skipped.length})
              </button>
              {showSkipped && (
                <ol className="mt-2">
                  {skipped.map((step, i) => (
                    <StepRow
                      candidateId={candidateId}
                      focused={step.key === focus}
                      key={step.key}
                      last={i === skipped.length - 1}
                      step={step}
                    />
                  ))}
                </ol>
              )}
            </div>
          )}
        </div>
      )}
    </section>
  );
}
