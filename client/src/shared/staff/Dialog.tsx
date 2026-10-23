import { type ReactNode, useEffect, useId, useRef } from 'react';

/**
 * A modal dialog: labelled by its title, focused when it opens, closed by Escape or the backdrop.
 * A div with role="dialog" rather than <dialog>, because jsdom does not implement showModal().
 */
export function Dialog({
  children,
  onClose,
  title,
}: {
  children: ReactNode;
  onClose: () => void;
  title: string;
}) {
  const titleId = useId();
  const panel = useRef<HTMLDivElement>(null);

  useEffect(() => {
    panel.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="modal modal-open">
      <div
        aria-labelledby={titleId}
        aria-modal="true"
        className="modal-box w-11/12 max-w-3xl"
        ref={panel}
        role="dialog"
        tabIndex={-1}
      >
        <div className="mb-4 flex items-start justify-between gap-4">
          <h2 className="text-xl font-bold" id={titleId}>
            {title}
          </h2>
          <button
            aria-label="Close"
            className="btn btn-sm btn-circle btn-ghost"
            onClick={onClose}
            type="button"
          >
            ✕
          </button>
        </div>
        {children}
      </div>
      <button
        aria-hidden="true"
        className="modal-backdrop"
        onClick={onClose}
        tabIndex={-1}
        type="button"
      />
    </div>
  );
}

/** An email's HTML, isolated: no scripts, no access to the app. */
export function EmailFrame({ html, title }: { html: string; title: string }) {
  return (
    <iframe
      className="h-[28rem] w-full rounded-box border border-base-300 bg-white"
      sandbox=""
      srcDoc={html}
      title={title}
    />
  );
}
