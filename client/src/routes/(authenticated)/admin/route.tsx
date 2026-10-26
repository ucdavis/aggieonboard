import { createFileRoute, Link, Outlet } from '@tanstack/react-router';
import { useUser } from '@/shared/auth/UserContext.tsx';

export const Route = createFileRoute('/(authenticated)/admin')({
  component: AdminLayout,
});

const sections = [
  { label: 'Templates', to: '/admin/templates' },
  { label: 'Settings', to: '/admin/settings' },
  { label: 'Staff', to: '/admin/staff' },
] as const;

/** Admin pages: templates, settings and holidays, staff. The API refuses anyone else; this only says so. */
function AdminLayout() {
  const user = useUser();
  if (!user.roles.includes('Admin')) {
    return (
      <div className="alert alert-warning" role="alert">
        Admin pages are for AggieOnboard administrators.
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <nav aria-label="Admin" className="tabs tabs-border">
        {sections.map((s) => (
          <Link
            activeProps={{ className: 'tab-active' }}
            className="tab"
            key={s.to}
            to={s.to}
          >
            {s.label}
          </Link>
        ))}
      </nav>
      <Outlet />
    </div>
  );
}
