import { createFileRoute, useNavigate } from '@tanstack/react-router';
import { HttpError } from '@/lib/api.ts';
import { useRedeemLink } from '@/queries/portal.ts';
import { NewLinkForm } from '@/shared/portal/NewLinkForm.tsx';
import { PortalFrame } from '@/shared/portal/PortalFrame.tsx';

export const Route = createFileRoute('/c/$token')({
  component: ContinuePage,
});

/**
 * Where an emailed link lands. Opening the page does not use the link: email security scanners
 * open links too, and would spend a one-time link before the candidate ever clicked. Only the
 * Continue button does.
 */
function ContinuePage() {
  const { token } = Route.useParams();
  const redeem = useRedeemLink();
  const navigate = useNavigate();

  const reason =
    redeem.error instanceof HttpError &&
    typeof redeem.error.body === 'object' &&
    redeem.error.body
      ? (redeem.error.body as { detail?: string }).detail
      : undefined;

  return (
    <PortalFrame>
      <div className="card bg-base-100 shadow-sm">
        <div className="card-body gap-4">
          <h1 className="text-2xl font-bold">Your onboarding checklist</h1>
          {redeem.isError ? (
            <>
              <div className="alert alert-warning" role="alert">
                {reason ?? "We couldn't open your checklist."} You can get a new
                link by email.
              </div>
              <NewLinkForm />
            </>
          ) : (
            <>
              <p>
                For your security, this link works once. Continue to open your
                checklist on this device.
              </p>
              <button
                className="btn btn-primary w-full sm:w-auto"
                disabled={redeem.isPending}
                onClick={() =>
                  redeem.mutate(token, {
                    onSuccess: () => navigate({ replace: true, to: '/portal' }),
                  })
                }
                type="button"
              >
                {redeem.isPending ? 'Opening…' : 'Continue'}
              </button>
            </>
          )}
        </div>
      </div>
    </PortalFrame>
  );
}
