import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import { errorMessage, formatDue, roleLabel } from '@/lib/journey.ts';
import {
  type StaffRole,
  type WorkItem,
  type WorkList,
  workQueryOptions,
} from '@/queries/staff.ts';

const groupRoles = ['Recruiter', 'ServiceChannel', 'UnitHr'] as const;
type Group = (typeof groupRoles)[number];

type WorkSearch = { group?: Group };

export const Route = createFileRoute('/(authenticated)/work')({
  component: WorkPage,
  validateSearch: (search: Record<string, unknown>): WorkSearch => ({
    group: groupRoles.includes(search.group as Group)
      ? (search.group as Group)
      : undefined,
  }),
});

/**
 * A group's open steps across every candidate, clustered by who in the group is responsible:
 * first what needs the group (the new employee has done their part), then what is with new
 * employees. Each one opens that candidate's timeline at the step.
 */
function WorkPage() {
  const { group } = Route.useSearch();
  const { data, error } = useQuery(workQueryOptions(group));
  const shown: StaffRole | undefined = data?.group ?? group;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-3xl font-bold">My work</h1>
          <p className="text-base-content/70">
            Open steps for {shown ? roleLabel[shown] : 'your group'}, by
            who&apos;s responsible.
          </p>
        </div>
        <nav aria-label="Group" className="join">
          {groupRoles.map((role) => (
            <Link
              className={`btn join-item btn-sm ${shown === role ? 'btn-active btn-primary' : ''}`}
              key={role}
              search={{ group: role }}
              to="/work"
            >
              {roleLabel[role]}
            </Link>
          ))}
        </nav>
      </div>
      {error ? (
        <div className="alert alert-error" role="alert">
          {errorMessage(error)}
        </div>
      ) : !data ? (
        <span className="loading loading-spinner" />
      ) : (
        <>
          <WorkSection
            empty="Nothing is waiting on your group."
            heading={`Needs ${roleLabel[data.group]}`}
            intro="The new employee has done their part, or it's blocked. Your move."
            items={data.items.filter((i) => i.needsStaff)}
            list={data}
          />
          <WorkSection
            empty="No open steps are with new employees."
            heading="With new employees"
            intro="Open steps they still have to do. Overdue ones first: a nudge may help."
            items={data.items.filter((i) => !i.needsStaff)}
            list={data}
          />
        </>
      )}
    </div>
  );
}

function WorkSection({
  empty,
  heading,
  intro,
  items,
  list,
}: {
  empty: string;
  heading: string;
  intro: string;
  items: WorkItem[];
  list: WorkList;
}) {
  const id = heading.replaceAll(/\W+/g, '-').toLowerCase();
  // Clustered by the responsible person: you first, then by name, then nobody assigned.
  const clusters = [
    ...new Map(
      items.map((i) => [i.assigneeId ?? 0, i.assigneeName] as const)
    ).entries(),
  ].sort(([a, an], [b, bn]) =>
    a === list.meId
      ? -1
      : b === list.meId
        ? 1
        : a === 0
          ? 1
          : b === 0
            ? -1
            : (an ?? '').localeCompare(bn ?? '')
  );

  return (
    <section aria-labelledby={id} className="space-y-3">
      <div>
        <h2 className="text-xl font-bold" id={id}>
          {heading}
          <span className="ml-2 text-base font-normal text-base-content/60">
            {items.length}
          </span>
        </h2>
        <p className="text-sm text-base-content/70">{intro}</p>
      </div>
      {items.length === 0 ? (
        <p className="rounded-box bg-base-100 p-4 text-sm">{empty}</p>
      ) : (
        clusters.map(([assigneeId, name]) => {
          const theirs = items.filter(
            (i) => (i.assigneeId ?? 0) === assigneeId
          );
          return (
            <div className="rounded-box bg-base-100 shadow-sm" key={assigneeId}>
              <h3 className="border-b border-base-200 px-4 py-2 font-semibold">
                {name ?? 'Nobody assigned'}
                {assigneeId !== 0 && assigneeId === list.meId && ' (you)'}
                <span className="ml-2 font-normal text-base-content/60">
                  {theirs.length}
                </span>
              </h3>
              <ul className="divide-y divide-base-200">
                {theirs.map((item) => (
                  <li key={`${item.candidateId}-${item.stepKey}`}>
                    <Link
                      className="flex flex-wrap items-center justify-between gap-2 px-4 py-3 hover:bg-base-200"
                      params={{ candidateId: String(item.candidateId) }}
                      search={{ step: item.stepKey }}
                      to="/candidates/$candidateId"
                    >
                      <span>
                        <span className="font-medium">
                          {item.candidateName}
                        </span>{' '}
                        · {item.stepTitle}
                        <span className="block text-xs text-base-content/60">
                          {item.positionTitle}
                        </span>
                      </span>
                      <span className="text-sm">
                        {item.status === 'Blocked'
                          ? 'Blocked'
                          : item.needsStaff
                            ? "Their part's done: your move"
                            : item.dueDate
                              ? `Due ${formatDue(item.dueDate, item.dueTime)}`
                              : 'Open'}
                        {item.isOverdue && (
                          <span className="badge badge-error badge-sm ml-2">
                            Overdue
                          </span>
                        )}
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
          );
        })
      )}
    </section>
  );
}
