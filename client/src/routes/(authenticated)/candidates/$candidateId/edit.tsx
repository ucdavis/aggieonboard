import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router';
import { candidateQueryOptions, useUpdateCandidate } from '@/queries/staff.ts';
import { CandidateForm } from '@/shared/staff/CandidateForm.tsx';

export const Route = createFileRoute(
  '/(authenticated)/candidates/$candidateId/edit'
)({
  component: EditCandidatePage,
});

function EditCandidatePage() {
  const id = Number(Route.useParams().candidateId);
  const { data: candidate, error } = useQuery(candidateQueryOptions(id));
  const update = useUpdateCandidate(id);
  const navigate = useNavigate();

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <Link
          className="link link-hover text-sm"
          params={{ candidateId: String(id) }}
          to="/candidates/$candidateId"
        >
          ← Back to the candidate
        </Link>
        <h1 className="mt-1 text-3xl font-bold">
          Edit{' '}
          {candidate
            ? `${candidate.firstName} ${candidate.lastName}`
            : 'candidate'}
        </h1>
      </div>
      <div className="card bg-base-100 shadow-sm">
        <div className="card-body">
          {error ? (
            <div className="alert alert-error" role="alert">
              This candidate could not be loaded.
            </div>
          ) : candidate ? (
            <CandidateForm
              candidate={candidate}
              onSave={async (input) => {
                await update.mutateAsync(input);
                await navigate({
                  params: { candidateId: String(id) },
                  to: '/candidates/$candidateId',
                });
              }}
              submitLabel="Save changes"
            />
          ) : (
            <div className="flex justify-center py-12">
              <span className="loading loading-spinner loading-lg" />
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
