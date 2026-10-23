import { useQuery } from '@tanstack/react-query';
import { errorMessage, formatTimestamp, messageLabel } from '@/lib/journey.ts';
import {
  messagePreviewQueryOptions,
  sentMessageQueryOptions,
  useSendMessage,
} from '@/queries/staff.ts';
import { Dialog, EmailFrame } from './Dialog.tsx';

/**
 * The message filled in for this candidate. Send is offered only when nothing is missing and
 * nothing blocks it; otherwise the dialog says what to fix, field by field.
 */
export function MessagePreviewDialog({
  candidateId,
  messageKey,
  onClose,
  onSent,
}: {
  candidateId: number;
  messageKey: string;
  onClose: () => void;
  onSent: (notice: string) => void;
}) {
  const preview = useQuery(messagePreviewQueryOptions(candidateId, messageKey));
  const send = useSendMessage(candidateId);
  const p = preview.data;

  return (
    <Dialog
      onClose={onClose}
      title={
        p
          ? `${messageLabel(p.auditReference)}: ${p.subject}`
          : 'Message preview'
      }
    >
      {preview.error ? (
        <div className="alert alert-error" role="alert">
          {errorMessage(preview.error)}
        </div>
      ) : !p ? (
        <div className="flex justify-center py-12">
          <span className="loading loading-spinner loading-lg" />
        </div>
      ) : (
        <div className="space-y-4">
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
            <dt className="text-base-content/60">To</dt>
            <dd>{p.to}</dd>
            <dt className="text-base-content/60">Replies go to</dt>
            <dd>{p.replyTo ?? '—'}</dd>
            <dt className="text-base-content/60">Subject</dt>
            <dd>{p.subject}</dd>
            {p.startsSteps.length > 0 && (
              <>
                <dt className="text-base-content/60">Sending starts</dt>
                <dd>{p.startsSteps.join(', ')}</dd>
              </>
            )}
            {p.timesSent > 0 && (
              <>
                <dt className="text-base-content/60">Already sent</dt>
                <dd>
                  {p.timesSent === 1 ? 'Once' : `${p.timesSent} times`}; sending
                  again is a reminder
                </dd>
              </>
            )}
          </dl>

          {p.blockers.map((blocker) => (
            <div
              className="alert alert-warning text-sm"
              key={blocker}
              role="status"
            >
              {blocker}
            </div>
          ))}
          {p.missing.length > 0 && (
            <div className="alert alert-warning block text-sm" role="status">
              <p className="font-semibold">
                This message can&apos;t be sent until these are filled in:
              </p>
              <ul className="mt-2 list-inside list-disc space-y-1">
                {p.missing.map((m) => (
                  <li key={m.name}>
                    <span className="font-medium">{m.description}</span> {m.fix}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <EmailFrame html={p.html} title={`Preview of ${p.subject}`} />

          {send.error && (
            <div className="alert alert-error text-sm" role="alert">
              {errorMessage(send.error)}
            </div>
          )}
          <div className="flex justify-end gap-2">
            <button className="btn btn-ghost" onClick={onClose} type="button">
              Close
            </button>
            {p.sender === 'App' && (
              <button
                className="btn btn-primary"
                disabled={!p.canSend || send.isPending}
                onClick={() =>
                  send.mutate(messageKey, {
                    onSuccess: () =>
                      onSent(
                        `${messageLabel(p.auditReference)} was sent to ${p.to}.`
                      ),
                  })
                }
                type="button"
              >
                {send.isPending ? 'Sending…' : `Send to ${p.to}`}
              </button>
            )}
          </div>
        </div>
      )}
    </Dialog>
  );
}

/** A message exactly as it went out. */
export function SentMessageDialog({
  candidateId,
  onClose,
  sentId,
}: {
  candidateId: number;
  onClose: () => void;
  sentId: number;
}) {
  const { data: m, error } = useQuery(
    sentMessageQueryOptions(candidateId, sentId)
  );
  return (
    <Dialog onClose={onClose} title={m ? m.subject : 'Sent message'}>
      {error ? (
        <div className="alert alert-error" role="alert">
          {errorMessage(error)}
        </div>
      ) : !m ? (
        <div className="flex justify-center py-12">
          <span className="loading loading-spinner loading-lg" />
        </div>
      ) : (
        <div className="space-y-4">
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
            <dt className="text-base-content/60">Sent</dt>
            <dd>
              {formatTimestamp(m.sentAt)} by {m.sentBy}
              {m.automatic && ' (automatically)'}
            </dd>
            <dt className="text-base-content/60">To</dt>
            <dd>{m.to}</dd>
            <dt className="text-base-content/60">Template</dt>
            <dd>
              {messageLabel(m.auditReference)}, version {m.templateVersion}
            </dd>
          </dl>
          <EmailFrame html={m.html} title={`Sent copy of ${m.subject}`} />
        </div>
      )}
    </Dialog>
  );
}
