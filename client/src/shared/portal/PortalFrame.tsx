import type { ReactNode } from 'react';

/**
 * The portal's page frame: plain, mobile-first, and UC Davis Health rather than the staff app.
 * The skip link and landmarks are for keyboard and screen-reader users.
 */
export function PortalFrame({
  actions,
  children,
}: {
  actions?: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className="min-h-screen bg-base-200">
      <a
        className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-10 focus:rounded focus:bg-base-100 focus:p-2"
        href="#main"
      >
        Skip to main content
      </a>
      <header className="bg-primary text-primary-content">
        <div className="mx-auto flex max-w-3xl items-center justify-between gap-4 px-4 py-3">
          <p className="text-lg font-bold">UC Davis Health</p>
          {actions}
        </div>
      </header>
      <main className="mx-auto max-w-3xl px-4 py-6 sm:py-10" id="main">
        {children}
      </main>
    </div>
  );
}
