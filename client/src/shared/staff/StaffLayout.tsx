import { Link, Outlet } from '@tanstack/react-router';
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
  if (!journey && !scheduling) {
    return <AccessUnavailable />;
  }

  return (
    <div className="min-h-screen bg-base-200">
      <header className="bg-primary text-primary-content">
        <div className="container mx-auto flex flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
          <Link className="text-xl font-bold" to="/">
            AggieOnboard
          </Link>
          <nav aria-label="Main" className="flex gap-4 text-sm">
            {journey && (
              <>
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
