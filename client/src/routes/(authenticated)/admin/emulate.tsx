import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { errorMessage, roleLabel } from '@/lib/journey.ts';
import {
  emulationTargetsQueryOptions,
  useEmulateCandidate,
  useEmulateStaff,
} from '@/queries/admin.ts';
import { useUser } from '@/shared/auth/UserContext.tsx';

export const Route = createFileRoute('/(authenticated)/admin/emulate')({
  component: EmulatePage,
});

/** See the app exactly as someone else does. Read-only, and recorded in the history. */
function EmulatePage() {
  const { data, error } = useQuery(emulationTargetsQueryOptions());
  const user = useUser();
  const staff = useEmulateStaff();
  const candidate = useEmulateCandidate();
  const failure = staff.error ?? candidate.error;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold">Emulate</h1>
        <p className="text-base-content/70">
          See the app exactly as someone else does. It&apos;s read-only: nothing
          is changed, booked or emailed in their name. Each view is recorded in
          the history.
        </p>
      </div>
      {failure && (
        <div className="alert alert-error" role="alert">
          {errorMessage(failure)}
        </div>
      )}
      {error ? (
        <div className="alert alert-error" role="alert">
          {errorMessage(error)}
        </div>
      ) : !data ? (
        <span className="loading loading-spinner" />
      ) : (
        <div className="grid gap-6 lg:grid-cols-2">
          <section aria-labelledby="emulate-staff" className="space-y-3">
            <h2 className="text-xl font-bold" id="emulate-staff">
              Staff
            </h2>
            <ul className="divide-y divide-base-200 rounded-box bg-base-100 shadow-sm">
              {data.staff.map((s) => (
                <li
                  className="flex items-center justify-between gap-3 px-4 py-3"
                  key={s.id}
                >
                  <div>
                    <div className="font-medium">
                      {s.name}
                      {s.isAdmin && (
                        <span className="badge badge-primary badge-sm ml-2">
                          Admin
                        </span>
                      )}
                    </div>
                    <div className="text-xs text-base-content/60">
                      {roleLabel[s.role]}
                      {s.teams.length > 0 && <> · {s.teams.join(', ')}</>}
                    </div>
                  </div>
                  {s.email.toLowerCase() === user.email?.toLowerCase() ? (
                    <span className="text-sm text-base-content/60">You</span>
                  ) : (
                    <button
                      aria-label={`View as ${s.name}`}
                      className="btn btn-sm"
                      disabled={staff.isPending}
                      onClick={() => staff.mutate(s.id)}
                      type="button"
                    >
                      View as
                    </button>
                  )}
                </li>
              ))}
            </ul>
          </section>
          <section aria-labelledby="emulate-candidates" className="space-y-3">
            <h2 className="text-xl font-bold" id="emulate-candidates">
              New employees
            </h2>
            <ul className="divide-y divide-base-200 rounded-box bg-base-100 shadow-sm">
              {data.candidates.map((c) => (
                <li
                  className="flex items-center justify-between gap-3 px-4 py-3"
                  key={c.id}
                >
                  <div>
                    <div className="font-medium">{c.name}</div>
                    <div className="text-xs text-base-content/60">
                      {c.positionTitle}
                      {c.currentStep && <> · now: {c.currentStep}</>}
                    </div>
                  </div>
                  <button
                    aria-label={`View ${c.name}'s checklist`}
                    className="btn btn-sm"
                    disabled={candidate.isPending}
                    onClick={() => candidate.mutate(c.id)}
                    type="button"
                  >
                    View checklist
                  </button>
                </li>
              ))}
            </ul>
          </section>
        </div>
      )}
    </div>
  );
}
