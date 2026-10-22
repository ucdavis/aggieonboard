import { createFileRoute, Link, useNavigate } from '@tanstack/react-router';
import { useCreateCandidate } from '@/queries/staff.ts';
import { CandidateForm } from '@/shared/staff/CandidateForm.tsx';

export const Route = createFileRoute('/(authenticated)/candidates/new')({
  component: NewCandidatePage,
});

function NewCandidatePage() {
  const create = useCreateCandidate();
  const navigate = useNavigate();

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <Link className="link link-hover text-sm" to="/">
          ← Pipeline
        </Link>
        <h1 className="mt-1 text-3xl font-bold">New candidate</h1>
        <p className="text-base-content/70">
          Every step starts upcoming. Start the reference check from the
          candidate&apos;s page.
        </p>
      </div>
      <div className="card bg-base-100 shadow-sm">
        <div className="card-body">
          <CandidateForm
            onSave={async (input) => {
              const detail = await create.mutateAsync(input);
              await navigate({
                params: { candidateId: String(detail.id) },
                to: '/candidates/$candidateId',
              });
            }}
            submitLabel="Create candidate"
          />
        </div>
      </div>
    </div>
  );
}
