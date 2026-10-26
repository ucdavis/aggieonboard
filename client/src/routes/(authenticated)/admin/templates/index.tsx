import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import { errorMessage, formatTimestamp, messageLabel } from '@/lib/journey.ts';
import { templatesQueryOptions } from '@/queries/admin.ts';

export const Route = createFileRoute('/(authenticated)/admin/templates/')({
  component: TemplatesPage,
});

/** Every message at the version that sends now. Editing one saves a new version. */
function TemplatesPage() {
  const { data, error } = useQuery(templatesQueryOptions());

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-3xl font-bold">Message templates</h1>
        <p className="text-base-content/70">
          The newest version of each message is the one that sends. Editing
          saves a new version; earlier ones are kept, with what was sent from
          them.
        </p>
      </div>
      {error ? (
        <div className="alert alert-error" role="alert">
          {errorMessage(error)}
        </div>
      ) : !data ? (
        <span className="loading loading-spinner" />
      ) : (
        <div className="overflow-x-auto rounded-box bg-base-100 shadow-sm">
          <table className="table">
            <thead>
              <tr>
                <th>Message</th>
                <th>Subject</th>
                <th>Used by</th>
                <th>Version</th>
              </tr>
            </thead>
            <tbody>
              {data.map((t) => (
                <tr key={t.key}>
                  <td className="whitespace-nowrap">
                    <Link
                      className="link link-primary font-medium"
                      params={{ templateKey: t.key }}
                      to="/admin/templates/$templateKey"
                    >
                      {messageLabel(t.auditReference)}
                    </Link>
                    {t.sender === 'External' && (
                      <span className="badge badge-ghost badge-sm ml-2">
                        Vendor wording
                      </span>
                    )}
                    <div className="text-xs text-base-content/60">{t.key}</div>
                  </td>
                  <td>{t.subject}</td>
                  <td className="text-sm">{t.steps.join(', ') || '—'}</td>
                  <td className="whitespace-nowrap text-sm">
                    {t.version}
                    <div className="text-xs text-base-content/60">
                      {formatTimestamp(t.updatedAt)} · {t.updatedBy}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
