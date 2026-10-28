import { useQuery } from '@tanstack/react-query';
import { Link, Outlet } from '@tanstack/react-router';
import { roleLabel } from '@/lib/journey.ts';
import {
  emulationStatusQueryOptions,
  useStopEmulation,
} from '@/queries/admin.ts';
import { useUser } from '@/shared/auth/UserContext.tsx';
import { AccessUnavailable } from './AccessUnavailable.tsx';

/**
 * The staff app's frame. Journey staff ("Staff") get the pipeline; members of a team that runs
 * appointments ("Scheduling") get the schedule; admins also get Admin. Someone with neither of the
 * first two sees Access unavailable. The
 * API enforces the same rules, so this only decides what each person is offered.
 */
export function StaffLayout() {
  const user = useUser();
  const journey = user.roles.includes('Staff');
  const scheduling = user.roles.includes('Scheduling');
  const admin = user.roles.includes('Admin');
  const emulating = user.roles.includes('Emulating');
  if (!journey && !scheduling) {
    return (
      <>
        {emulating && <EmulationBanner />}
        <AccessUnavailable />
      </>
    );
  }

  return (
    <div className="min-h-screen bg-base-200">
      {emulating && <EmulationBanner />}
      <header className="bg-primary text-primary-content">
        <div className="container mx-auto flex flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
          <Link className="text-xl font-bold" to="/">
            AggieOnboard
          </Link>
          <nav aria-label="Main" className="flex gap-4 text-sm">
            {journey && (
              <>
                <Link
                  activeOptions={{ includeSearch: false }}
                  activeProps={{ className: 'underline underline-offset-4' }}
                  to="/work"
                >
                  My work
                </Link>
                <Link
                  activeOptions={{ exact: true, includeSearch: false }}
                  activeProps={{ className: 'underline underline-offset-4' }}
                  to="/"
                >
                  Pipeline
                </Link>
                <Link
                  activeProps={{ className: 'underline underline-offset-4' }}
                  to="/candidates/new"
                >
                  New candidate
                </Link>
              </>
            )}
            {scheduling && (
              <Link
                activeOptions={{ includeSearch: false }}
                activeProps={{ className: 'underline underline-offset-4' }}
                to="/schedule"
              >
                Schedule
              </Link>
            )}
            {admin && (
              <Link
                activeProps={{ className: 'underline underline-offset-4' }}
                to="/admin"
              >
                Admin
              </Link>
            )}
          </nav>
          <p className="ml-auto text-sm opacity-90">{user.name}</p>
        </div>
      </header>
      <main className="container mx-auto px-4 py-8">
        <Outlet />
      </main>
    </div>
  );
}

/** Shown on every page while an admin is viewing the app as someone else. */
function EmulationBanner() {
  const { data } = useQuery(emulationStatusQueryOptions());
  const stop = useStopEmulation();
  return (
    <div
      className="flex flex-wrap items-center justify-center gap-3 bg-warning px-4 py-2 text-sm text-warning-content"
      role="status"
    >
      <span>
        {data ? (
          <>
            Viewing as <strong>{data.viewingAs}</strong> ({roleLabel[data.role]}
            ). Read-only. You&apos;re signed in as {data.signedInAs}.
          </>
        ) : (
          'Viewing as someone else. Read-only.'
        )}
      </span>
      <button
        className="btn btn-sm"
        disabled={stop.isPending}
        onClick={() => stop.mutate()}
        type="button"
      >
        Stop viewing as {data?.viewingAs ?? 'them'}
      </button>
    </div>
  );
}
