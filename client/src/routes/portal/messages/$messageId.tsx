import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import { formatTimestamp } from '@/lib/journey.ts';
import { portalMessageQueryOptions } from '@/queries/portal.ts';
import { PortalFrame } from '@/shared/portal/PortalFrame.tsx';
import { EmailFrame } from '@/shared/staff/Dialog.tsx';

export const Route = createFileRoute('/portal/messages/$messageId')({
  component: MessagePage,
});

/** An email we sent, so the instructions are one tap from the step they belong to. */
function MessagePage() {
  const id = Number(Route.useParams().messageId);
  const { data: message, error } = useQuery(portalMessageQueryOptions(id));

  return (
    <PortalFrame>
      <div className="space-y-4">
        <Link className="link" to="/portal">
          ← Back to your checklist
        </Link>
        {error ? (
          <div className="alert alert-warning" role="alert">
            This email isn&apos;t available.{' '}
            <Link className="link" to="/portal">
              Return to your checklist
            </Link>
            .
          </div>
        ) : !message ? (
          <div className="flex justify-center py-12">
            <span className="loading loading-spinner loading-lg" />
          </div>
        ) : (
          <article className="space-y-3">
            <h1 className="text-2xl font-bold">{message.subject}</h1>
            <p className="text-sm text-base-content/70">
              Sent {formatTimestamp(message.sentAt)}
            </p>
            <EmailFrame html={message.html} title={message.subject} />
          </article>
        )}
      </div>
    </PortalFrame>
  );
}
