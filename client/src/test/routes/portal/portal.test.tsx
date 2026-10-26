import { describe, expect, it } from 'vitest';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type {
  PortalAppointment,
  PortalStep,
  PortalView,
} from '@/queries/portal.ts';
import type { BookingOptions } from '@/queries/schedule.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';

// Unhandled requests fail these tests, which is how they also prove the portal never calls a
// staff endpoint such as /api/user/me.

const portalStep = (overrides: Partial<PortalStep> = {}): PortalStep => ({
  atiNumber: null,
  booking: null,
  completedAt: null,
  contact: { details: 'pat.kim@example.test or 916-555-0142', name: 'Pat Kim' },
  dueDate: '2026-10-26',
  dueTime: '16:54:00',
  i9Choice: null,
  isOverdue: false,
  key: 'drug-screen',
  messages: [],
  phase: 'PreEmployment',
  representative: null,
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

const fingerprinting = (
  appointment: PortalAppointment | null,
  canBook = true
): PortalStep =>
  portalStep({
    booking: {
      appointment,
      canBook,
      typeKey: 'fingerprinting',
      typeName: 'Fingerprinting (Live Scan)',
    },
    key: 'fingerprinting',
    state: appointment ? 'InProgress' : 'ToDo',
    title: 'Fingerprinting (Live Scan)',
    watchFor: null,
  });

const appointment = (
  overrides: Partial<PortalAppointment> = {}
): PortalAppointment => ({
  address: null,
  canChange: true,
  changeCutoff: '2026-10-25T09:15:00',
  end: '2026-10-26T09:30:00',
  id: 41,
  locationName: 'UC Davis Health Police Department, Sacramento',
  start: '2026-10-26T09:15:00',
  status: 'Booked',
  ...overrides,
});

const options: BookingOptions = {
  current: null,
  earliest: '2026-10-23T09:00:00',
  latest: '2026-11-10',
  locations: [
    {
      address: null,
      locationId: 2,
      name: 'UC Davis Health Police Department, Sacramento',
      slots: [
        {
          capacity: 1,
          end: '2026-10-26T09:30:00',
          remaining: 1,
          start: '2026-10-26T09:15:00',
        },
      ],
    },
  ],
  typeKey: 'fingerprinting',
  typeName: 'Fingerprinting (Live Scan)',
  unavailable: null,
};

describe('booking from the checklist', () => {
  it('books a time and shows it', async () => {
    let booked: unknown = null;
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(view([fingerprinting(booked ? appointment() : null)]))
      ),
      http.get('/api/portal/appointments/fingerprinting/options', () =>
        HttpResponse.json(options)
      ),
      http.post('/api/portal/appointments', async ({ request }) => {
        booked = await request.json();
        return new HttpResponse(null, { status: 204 });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      await userEvent.click(
        await screen.findByRole('button', {
          name: 'Book Fingerprinting (Live Scan)',
        })
      );
      const dialog = await screen.findByRole('dialog', {
        name: 'Book Fingerprinting (Live Scan)',
      });
      await userEvent.click(
        await within(dialog).findByRole('button', { name: '9:15 a.m.' })
      );
      await userEvent.click(
        within(dialog).getByRole('button', {
          name: 'Book Mon, Oct 26, 2026, 9:15 a.m.',
        })
      );

      expect(
        await screen.findByText(
          "Your Fingerprinting (Live Scan) appointment is booked. We're emailing you the details."
        )
      ).toBeInTheDocument();
      expect(booked).toEqual({
        locationId: 2,
        start: '2026-10-26T09:15:00',
        typeKey: 'fingerprinting',
      });
      expect(
        await screen.findByText('Mon, Oct 26, 2026, 9:15–9:30 a.m.')
      ).toBeInTheDocument();
      expect(
        screen.getByRole('button', { name: 'Change time' })
      ).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it('cancels after asking', async () => {
    let cancelled: string | null = null;
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(
          view([fingerprinting(cancelled ? null : appointment())])
        )
      ),
      http.post('/api/portal/appointments/:id/cancel', ({ params }) => {
        cancelled = String(params.id);
        return new HttpResponse(null, { status: 204 });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      expect(
        await screen.findByText(
          'Until Sun, Oct 25, 2026, 9:15 a.m., you can change or cancel it here.'
        )
      ).toBeInTheDocument();
      await userEvent.click(
        screen.getByRole('button', { name: 'Cancel appointment' })
      );
      await userEvent.click(
        screen.getByRole('button', { name: 'Yes, cancel it' })
      );

      expect(
        await screen.findByText(
          'Your Fingerprinting (Live Scan) appointment is cancelled.'
        )
      ).toBeInTheDocument();
      expect(cancelled).toBe('41');
    } finally {
      cleanup();
    }
  });

  it('sends the candidate to email once it is too close to change', async () => {
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(
          view([fingerprinting(appointment({ canChange: false }), false)])
        )
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      expect(
        await screen.findByText(/reply to your confirmation email/)
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: /Change|Cancel/ })
      ).toBeNull();
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

describe('answering in the checklist', () => {
  it('chooses remote I-9 verification after asking, with in person still offered until then', async () => {
    let remote = false;
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(
          view([
            portalStep({
              booking: remote
                ? null
                : {
                    appointment: null,
                    canBook: true,
                    typeKey: 'i9-in-person',
                    typeName: 'In-person I-9 verification',
                  },
              i9Choice: remote
                ? { canChoose: false, method: 'Remote' }
                : { canChoose: true, method: 'NotChosen' },
              key: 'i9',
              state: remote ? 'InProgress' : 'ToDo',
              title: 'I-9 verification',
            }),
          ])
        )
      ),
      http.post('/api/portal/i9/remote', () => {
        remote = true;
        return new HttpResponse(null, { status: 204 });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      expect(
        await screen.findByText("Choose how you'll verify your documents")
      ).toBeInTheDocument();
      expect(
        screen.getByRole('button', { name: 'Book In-person I-9 verification' })
      ).toBeInTheDocument();

      await userEvent.click(
        screen.getByRole('button', { name: 'Verify remotely' })
      );
      expect(remote).toBe(false);
      await userEvent.click(
        screen.getByRole('button', { name: 'Yes, verify remotely' })
      );

      expect(
        await screen.findByText(
          "You chose remote verification. We're emailing you the next steps."
        )
      ).toBeInTheDocument();
      expect(
        screen.getByText(
          'You chose to verify your documents remotely, with an authorized representative.'
        )
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('button', {
          name: 'Book In-person I-9 verification',
        })
      ).not.toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it("sends the representative's details, showing the server's field errors", async () => {
    const sent: unknown[] = [];
    let saved: { email: string; name: string } | null = null;
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(
          view([
            portalStep({
              key: 'i9-remote',
              representative: saved ?? { email: null, name: null },
              state: saved ? 'InProgress' : 'ToDo',
              title: 'Remote I-9 representative',
            }),
          ])
        )
      ),
      http.put('/api/portal/i9/representative', async ({ request }) => {
        const body = (await request.json()) as { email: string; name: string };
        sent.push(body);
        if (!body.email.includes('@')) {
          return HttpResponse.json(
            {
              errors: {
                email: ['Enter an email address like name@example.com.'],
              },
              status: 400,
            },
            { status: 400 }
          );
        }
        saved = body;
        return new HttpResponse(null, { status: 204 });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      await userEvent.type(
        await screen.findByLabelText('Full name'),
        'Jesse Brooks'
      );
      await userEvent.type(screen.getByLabelText('Email address'), 'jesse');
      await userEvent.click(screen.getByRole('button', { name: 'Send' }));

      expect(
        await screen.findByText('Enter an email address like name@example.com.')
      ).toBeInTheDocument();
      expect(screen.getByLabelText('Email address')).toHaveAttribute(
        'aria-invalid',
        'true'
      );

      await userEvent.type(
        screen.getByLabelText('Email address'),
        '@example.test'
      );
      await userEvent.click(screen.getByRole('button', { name: 'Send' }));

      expect(
        await screen.findByText(
          "Thanks. We have your representative's details."
        )
      ).toBeInTheDocument();
      expect(
        screen.getByText('Jesse Brooks (jesse@example.test)')
      ).toBeInTheDocument();
      expect(sent).toHaveLength(2);
      expect(
        screen.getByRole('button', { name: 'Change' })
      ).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it('sends the ATI number after fingerprinting', async () => {
    let saved: string | null = null;
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(
          view([
            portalStep({
              atiNumber: { value: saved },
              key: 'fingerprinting',
              state: 'InProgress',
              title: 'Fingerprinting (Live Scan)',
              watchFor: null,
            }),
          ])
        )
      ),
      http.put('/api/portal/fingerprinting/ati', async ({ request }) => {
        saved = ((await request.json()) as { atiNumber: string }).atiNumber;
        return new HttpResponse(null, { status: 204 });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      await userEvent.type(
        await screen.findByLabelText('ATI number'),
        'A1B2C3D4E5'
      );
      await userEvent.click(screen.getByRole('button', { name: 'Send' }));

      expect(
        await screen.findByText('Thanks. We have your ATI number.')
      ).toBeInTheDocument();
      expect(saved).toBe('A1B2C3D4E5');
      expect(screen.getByText('A1B2C3D4E5')).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });
});
