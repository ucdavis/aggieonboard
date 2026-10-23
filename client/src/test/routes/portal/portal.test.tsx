import { describe, expect, it } from 'vitest';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { PortalStep, PortalView } from '@/queries/portal.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';

// Unhandled requests fail these tests, which is how they also prove the portal never calls a
// staff endpoint such as /api/user/me.

const portalStep = (overrides: Partial<PortalStep> = {}): PortalStep => ({
  completedAt: null,
  contact: { details: 'pat.kim@example.test or 916-555-0142', name: 'Pat Kim' },
  dueDate: '2026-10-26',
  dueTime: '16:54:00',
  isOverdue: false,
  key: 'drug-screen',
  messages: [],
  phase: 'PreEmployment',
  state: 'ToDo',
  title: 'Drug screen',
  watchFor: {
    fromAddress: 'noreply@questdiagnostics.com',
    sender: 'Quest Diagnostics',
    subject: 'Your Drug Screen QPassport',
  },
  ...overrides,
});

const view = (steps: PortalStep[]): PortalView => ({
  anticipatedStartDate: '2026-11-19',
  clearanceDeadline: '2026-11-10',
  department: 'Respiratory Care',
  firstName: 'Morgan',
  positionTitle: 'Respiratory Therapist',
  steps,
});

describe('candidate portal', () => {
  it('shows what to do, by when, what to watch for and who to ask', async () => {
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(
          view([
            portalStep({
              messages: [
                {
                  id: 7,
                  sentAt: '2026-10-22T16:00:00Z',
                  subject: 'Your UC Davis Health pre-employment requirements',
                },
              ],
            }),
            portalStep({
              contact: null,
              dueDate: null,
              dueTime: null,
              key: 'background-check',
              state: 'InProgress',
              title: 'Background check',
              watchFor: null,
            }),
            portalStep({
              dueDate: null,
              dueTime: null,
              key: 'cleared',
              state: 'Upcoming',
              title: 'Cleared to start',
              watchFor: null,
            }),
            portalStep({
              completedAt: '2026-10-01T16:00:00Z',
              key: 'references',
              state: 'Done',
              title: 'Reference check',
              watchFor: null,
            }),
          ])
        )
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      expect(
        await screen.findByRole('heading', {
          name: 'Your onboarding checklist',
        })
      ).toBeInTheDocument();
      expect(screen.getByText('Thu, Nov 19, 2026')).toBeInTheDocument();

      const toDo = screen.getByRole('region', { name: /To do/ });
      expect(
        within(toDo).getByText('Due Mon, Oct 26, 2026, 4:54 p.m.')
      ).toBeInTheDocument();
      expect(within(toDo).getByText('Quest Diagnostics')).toBeInTheDocument();
      expect(
        within(toDo).getByText(/“Your Drug Screen QPassport”/)
      ).toBeInTheDocument();
      expect(
        within(toDo).getByText(
          'Questions? Contact Pat Kim at pat.kim@example.test or 916-555-0142.'
        )
      ).toBeInTheDocument();
      expect(
        within(toDo).getByRole('link', {
          name: 'Read our email: Your UC Davis Health pre-employment requirements',
        })
      ).toHaveAttribute('href', '/portal/messages/7');

      const inProgress = screen.getByRole('region', { name: /In progress/ });
      expect(
        within(inProgress).getByText('Nothing to do right now')
      ).toBeInTheDocument();
      expect(
        screen.getByRole('region', { name: /Coming up/ })
      ).toHaveTextContent('Cleared to start');
      expect(screen.getByRole('region', { name: /Done/ })).toHaveTextContent(
        'Reference check'
      );
    } finally {
      cleanup();
    }
  });

  it("says so when there's nothing to do", async () => {
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(view([portalStep({ state: 'Done', watchFor: null })]))
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      expect(
        await screen.findByText(/You're all caught up/)
      ).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it('offers a new link, with the same answer for any address, when not signed in', async () => {
    let requested: unknown;
    server.use(
      http.get('/api/portal/me', () => new HttpResponse(null, { status: 401 })),
      http.post('/api/portal/link', async ({ request }) => {
        requested = await request.json();
        return new HttpResponse(null, { status: 202 });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      const input = await screen.findByLabelText(
        /The email address we've been writing to/
      );
      await userEvent.type(input, 'morgan.chen@example.test');
      await userEvent.click(
        screen.getByRole('button', { name: 'Email me a new link' })
      );

      expect(
        await screen.findByText(
          /If that address is on file, we've emailed it a new link/
        )
      ).toBeInTheDocument();
      expect(requested).toEqual({ email: 'morgan.chen@example.test' });
    } finally {
      cleanup();
    }
  });

  it('signs out', async () => {
    let signedOut = false;
    server.use(
      http.get('/api/portal/me', () =>
        signedOut
          ? new HttpResponse(null, { status: 401 })
          : HttpResponse.json(view([]))
      ),
      http.post('/api/portal/sign-out', () => {
        signedOut = true;
        return new HttpResponse(null, { status: 204 });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      await userEvent.click(
        await screen.findByRole('button', { name: 'Sign out' })
      );
      expect(
        await screen.findByRole('button', { name: 'Email me a new link' })
      ).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });
});

describe('emailed link', () => {
  it('does nothing until Continue, so an email scanner cannot use it up', async () => {
    const redeemed: unknown[] = [];
    server.use(
      http.post('/api/portal/sign-in', async ({ request }) => {
        redeemed.push(await request.json());
        return new HttpResponse(null, { status: 204 });
      }),
      http.get('/api/portal/me', () => HttpResponse.json(view([])))
    );

    const { cleanup } = renderRoute({ initialPath: '/c/abc123' });
    try {
      const button = await screen.findByRole('button', { name: 'Continue' });
      expect(redeemed).toEqual([]);

      await userEvent.click(button);
      expect(await screen.findByText(/Hi Morgan/)).toBeInTheDocument();
      expect(redeemed).toEqual([{ token: 'abc123' }]);
    } finally {
      cleanup();
    }
  });

  it('explains a used link and offers a new one', async () => {
    server.use(
      http.post('/api/portal/sign-in', () =>
        HttpResponse.json(
          {
            detail: 'This link has already been used.',
            reason: 'used',
            status: 400,
            title: "We couldn't sign you in.",
          },
          { status: 400 }
        )
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/c/spent' });
    try {
      await userEvent.click(
        await screen.findByRole('button', { name: 'Continue' })
      );
      expect(await screen.findByRole('alert')).toHaveTextContent(
        'This link has already been used.'
      );
      expect(
        screen.getByRole('button', { name: 'Email me a new link' })
      ).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });
});
