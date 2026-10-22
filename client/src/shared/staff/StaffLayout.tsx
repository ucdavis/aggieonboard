import { Link, Outlet } from '@tanstack/react-router';
import { useUser } from '@/shared/auth/UserContext.tsx';
import { AccessUnavailable } from './AccessUnavailable.tsx';

/**
 * The staff app's frame. Only staff whitelist members (role "Staff") get past it; the API enforces
 * the same rule, so this only decides what an outsider sees.
 */
export function StaffLayout() {
  const user = useUser();
  if (!user.roles.includes('Staff')) {
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
