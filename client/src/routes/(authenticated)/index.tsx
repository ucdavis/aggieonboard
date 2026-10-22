import { useUser } from '@/shared/auth/UserContext.tsx';
import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/(authenticated)/')({
  component: RouteComponent,
});

// Placeholder staff home until the candidate pipeline lands (docs/PLAN.md, Phase 2).
function RouteComponent() {
  const user = useUser();
  return (
    <main className="min-h-screen bg-base-100">
      <div className="container mx-auto px-4 py-16">
        <header className="text-center">
          <img
            alt="UC Davis"
            className="mx-auto mb-8"
            height={77}
            src="/ucdavis.svg"
            width={419}
          />
          <h1 className="text-5xl font-bold mb-4">AggieOnboard</h1>
          <p className="text-xl max-w-2xl mx-auto text-base-content/70">
            Welcome, {user.name}. The candidate pipeline will appear here.
          </p>
        </header>
      </div>
    </main>
  );
}
