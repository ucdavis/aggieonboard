import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link, Navigate } from '@tanstack/react-router';
import { useUser } from '@/shared/auth/UserContext.tsx';
import type { ColumnDef } from '@tanstack/react-table';
import { useMemo } from 'react';
import {
  formatShortDate,
  formatTime,
  phaseLabel,
  statusBadge,
  statusLabel,
} from '@/lib/journey.ts';
import { pipelineQueryOptions, type PipelineRow } from '@/queries/staff.ts';
import { DataTable } from '@/shared/dataTable.tsx';

type PipelineSearch = { mine?: boolean };

export const Route = createFileRoute('/(authenticated)/')({
  component: PipelinePage,
  validateSearch: (search: Record<string, unknown>): PipelineSearch =>
    search.mine === true || search.mine === 'true' ? { mine: true } : {},
});

function PipelinePage() {
  // Someone who only runs appointments has no pipeline; their home is the schedule.
  return useUser().roles.includes('Staff') ? (
    <Pipeline />
  ) : (
    <Navigate replace to="/schedule" />
  );
}

function Pipeline() {
  const { mine = false } = Route.useSearch();
  const { data, error, isPending } = useQuery(pipelineQueryOptions(mine));
  const columns = useMemo(() => pipelineColumns, []);
  const overdue = data?.filter((row) => row.isOverdue).length ?? 0;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold">Candidate pipeline</h1>
          <p className="mt-1 text-base-content/70">
            {data
              ? `${data.length} candidate${data.length === 1 ? '' : 's'}${
                  overdue ? `, ${overdue} overdue` : ''
                }`
              : ' '}
          </p>
        </div>
        <div className="flex items-center gap-3">
          <div aria-label="Whose candidates" className="join" role="group">
            <Link
              aria-pressed={!mine}
              className={`btn join-item btn-sm ${mine ? '' : 'btn-active'}`}
              search={{}}
              to="/"
            >
              All
            </Link>
            <Link
              aria-pressed={mine}
              className={`btn join-item btn-sm ${mine ? 'btn-active' : ''}`}
              search={{ mine: true }}
              to="/"
            >
              My candidates
            </Link>
          </div>
          <Link className="btn btn-primary btn-sm" to="/candidates/new">
            New candidate
          </Link>
        </div>
      </div>

      <div className="card bg-base-100 shadow-sm">
        <div className="card-body">
          {isPending ? (
            <div className="flex justify-center py-12">
              <span className="loading loading-spinner loading-lg" />
            </div>
          ) : error ? (
            <div className="alert alert-error" role="alert">
              The pipeline could not be loaded. Refresh to try again.
            </div>
          ) : data.length === 0 ? (
            <p className="py-8 text-center text-base-content/70">
              {mine
                ? 'No candidates are assigned to you.'
                : 'No candidates yet.'}
            </p>
          ) : (
            <DataTable
              columns={columns}
              data={data}
              filterPlaceholder="Search candidates..."
              initialState={{ pagination: { pageSize: 25 } }}
            />
          )}
        </div>
      </div>
    </div>
  );
}

const pipelineColumns: ColumnDef<PipelineRow>[] = [
  {
    accessorFn: (row) => `${row.name} ${row.positionTitle} ${row.department}`,
    cell: ({ row: { original } }) => (
      <div>
        <Link
          className="link link-hover font-semibold"
          params={{ candidateId: String(original.id) }}
          to="/candidates/$candidateId"
        >
          {original.name}
        </Link>
        <div className="text-sm text-base-content/70">
          {original.positionTitle} · {original.department}
        </div>
      </div>
    ),
    header: 'Candidate',
    id: 'candidate',
    sortingFn: (a, b) => a.original.name.localeCompare(b.original.name),
  },
  {
    accessorFn: (row) => row.currentStepTitle ?? 'Journey complete',
    cell: ({ row: { original } }) =>
      original.currentStepTitle && original.currentStepStatus ? (
        <div className="space-y-1">
          <div>{original.currentStepTitle}</div>
          <div className="flex flex-wrap items-center gap-2 text-xs text-base-content/70">
            <span
              className={`badge badge-sm ${statusBadge[original.currentStepStatus]}`}
            >
              {statusLabel[original.currentStepStatus]}
            </span>
            {original.phase && <span>{phaseLabel[original.phase]}</span>}
            {original.openSteps > 1 && (
              <span>{original.openSteps} steps open</span>
            )}
          </div>
        </div>
      ) : (
        <span className="text-base-content/70">Journey complete</span>
      ),
    header: 'Current step',
    id: 'current',
  },
  {
    accessorFn: (row) =>
      row.nextDueDate ? `${row.nextDueDate} ${row.nextDueTime ?? ''}` : '9999',
    cell: ({ row: { original } }) =>
      original.nextDueDate ? (
        <span className={original.isOverdue ? 'font-semibold text-error' : ''}>
          {formatShortDate(original.nextDueDate)}
          {original.nextDueTime && `, ${formatTime(original.nextDueTime)}`}
          {original.isOverdue && (
            <span className="badge badge-error badge-sm ml-2">Overdue</span>
          )}
        </span>
      ) : (
        <span className="text-base-content/50">—</span>
      ),
    header: 'Next due',
    id: 'due',
  },
  {
    accessorFn: (row) =>
      row.applicableSteps ? row.completedSteps / row.applicableSteps : 0,
    cell: ({ row: { original } }) => (
      <div className="min-w-24">
        <progress
          aria-label={`${original.completedSteps} of ${original.applicableSteps} steps complete`}
          className="progress progress-primary w-full"
          max={original.applicableSteps}
          value={original.completedSteps}
        />
        <div className="text-xs text-base-content/70">
          {original.completedSteps} of {original.applicableSteps}
        </div>
      </div>
    ),
    header: 'Progress',
    id: 'progress',
  },
  {
    accessorFn: (row) => row.anticipatedStartDate ?? '9999',
    cell: ({ row: { original } }) =>
      formatShortDate(original.anticipatedStartDate),
    header: 'Start date',
    id: 'start',
  },
  {
    accessorFn: (row) =>
      [row.onboardingSpecialist, row.recruitingAssistant]
        .filter(Boolean)
        .join(' '),
    cell: ({ row: { original } }) => (
      <div className="text-sm">
        {original.onboardingSpecialist ?? original.recruitingAssistant ?? '—'}
        <div className="text-xs text-base-content/60">
          {original.onboardingSpecialist
            ? 'Onboarding specialist'
            : original.recruitingAssistant
              ? 'Recruiting assistant'
              : ''}
        </div>
      </div>
    ),
    header: 'Owner',
    id: 'owner',
  },
];
