import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { errorMessage, formatTimestamp } from '@/lib/journey.ts';
import { adminHistoryQueryOptions } from '@/queries/admin.ts';

export const Route = createFileRoute('/(authenticated)/admin/history')({
  component: HistoryPage,
});

/** Who changed templates, settings, deadlines, holidays or staff, newest first. Candidates' own history is on their page. */
function HistoryPage() {
  const { data, error } = useQuery(adminHistoryQueryOptions());
  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-3xl font-bold">History</h1>
        <p className="text-base-content/70">
          The last 100 changes to templates, settings, deadlines, holidays and
          staff.
        </p>
      </div>
      {error ? (
        <div className="alert alert-error" role="alert">
          {errorMessage(error)}
        </div>
      ) : !data ? (
        <span className="loading loading-spinner" />
      ) : data.length === 0 ? (
        <p className="rounded-box bg-base-100 p-4">No changes yet.</p>
      ) : (
        <ol className="divide-y divide-base-200 rounded-box bg-base-100 shadow-sm">
          {data.map((entry, i) => (
            <li className="px-4 py-3 text-sm" key={i}>
              <div>{entry.detail}</div>
              <div className="text-xs text-base-content/60">
                {formatTimestamp(entry.at)} · {entry.actorName}
              </div>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
