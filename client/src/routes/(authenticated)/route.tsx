import {
  createFileRoute,
  type ErrorComponentProps,
} from '@tanstack/react-router';
import { HttpError } from '../../lib/api.ts';
import { RouterContext } from '../../main.tsx';
import { meQueryOptions } from '../../queries/user.ts';
import { UserProvider } from '@/shared/auth/UserContext.tsx';
import { AccessUnavailable } from '@/shared/staff/AccessUnavailable.tsx';
import { StaffLayout } from '@/shared/staff/StaffLayout.tsx';

export const Route = createFileRoute('/(authenticated)')({
  beforeLoad: async ({ context }: { context: RouterContext }) => {
    await context.queryClient.ensureQueryData(meQueryOptions());
  },
  component: () => (
    <UserProvider>
      <StaffLayout />
    </UserProvider>
  ),
  errorComponent: AuthenticatedRouteError,
});

function AuthenticatedRouteError({ error }: ErrorComponentProps<unknown>) {
  if (error instanceof HttpError && error.status === 403) {
    return <AccessUnavailable />;
  }

  return (
    <main className="min-h-screen flex items-center justify-center px-4 py-12">
      <section className="max-w-lg text-center">
        <h1 className="text-3xl font-bold text-gray-900">
          We could not load this page
        </h1>
        <p className="mt-4 text-gray-600">
          Refresh the page or try again later.
        </p>
      </section>
    </main>
  );
}
