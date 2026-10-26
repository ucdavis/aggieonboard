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
  contact: null,
  dueDate: '2026-10-26',
  dueTime: '16:54:00',
  i9Choice: null,
  isOverdue: false,
  key: 'drug-screen',
  kind: 'External',
  messages: [],
  phase: 'PreEmployment',
  representative: null,
  stage: 5,
  state: 'ToDo',
  summary:
    'Complete your drug screen at a testing location and time that works for you.',
  taskLabel: 'Complete your drug screen',
  title: 'Drug screen',
  watchFor: {
    fromAddress: 'noreply@questdiagnostics.com',
    sender: 'Quest Diagnostics',
    subject: 'Your Drug Screen QPassport',
  },
  ...overrides,
});

const dmv = (overrides: Partial<PortalStep> = {}) =>
  portalStep({
    dueDate: null,
    dueTime: null,
    key: 'dmv',
    summary: null,
    taskLabel: 'Complete the DMV Employer Pull Notice form',
    title: 'DMV Employer Pull Notice',
    watchFor: null,
    ...overrides,
  });

const view = (steps: PortalStep[], done = 1): PortalView => ({
  anticipatedStartDate: '2026-11-19',
  clearanceDeadline: '2026-11-10',
  contact: { details: 'pat.kim@example.test or 916-555-0142', name: 'Pat Kim' },
  department: 'Respiratory Care',
  firstName: 'Morgan',
  positionTitle: 'Respiratory Therapist',
  steps,
  stepsDone: done,
  stepsTotal: 12,
});

/** A task page asks for the task's email text; most tests don't need any. */
const taskBody = (html: string | null = null) =>
  http.get('/api/portal/tasks/:key', ({ params }) =>
    HttpResponse.json({ html, key: String(params.key) })
  );

describe('checklist', () => {
  it('leads with the next step, then what else is open, what we are working on and what is done', async () => {
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(
          view([
            portalStep({
              completedAt: '2026-10-01T16:00:00Z',
              key: 'references',
              state: 'Done',
              taskLabel: 'Add your references',
            }),
            portalStep({
              key: 'background-check',
              state: 'InProgress',
              taskLabel: 'Complete your background check',
            }),
            portalStep(),
            dmv(),
          ])
        )
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      expect(
        await screen.findByRole('heading', { name: 'Welcome, Morgan' })
      ).toBeInTheDocument();
      expect(screen.getByText('1 of 12 steps done')).toBeInTheDocument();

      const next = screen.getByRole('region', { name: 'Your next step' });
      expect(
        within(next).getByRole('heading', { name: 'Complete your drug screen' })
      ).toBeInTheDocument();
      expect(
        within(next).getByText('Due Mon, Oct 26, 2026, 4:54 p.m.')
      ).toBeInTheDocument();
      expect(
        within(next).getByRole('link', {
          name: 'Click here to complete your drug screen',
        })
      ).toHaveAttribute('href', '/portal/tasks/drug-screen');

      expect(
        screen.getByRole('region', { name: 'Also to do now' })
      ).toHaveTextContent('Complete the DMV Employer Pull Notice form');
      expect(
        screen.getByRole('region', { name: "We're working on it" })
      ).toHaveTextContent('Complete your background check');
      expect(screen.getByText('Done (1)')).toBeInTheDocument();
      expect(
        screen.getByText(
          'Questions? Contact Pat Kim at pat.kim@example.test or 916-555-0142.'
        )
      ).toBeInTheDocument();
      expect(document.body).not.toHaveTextContent(
        /Quest Diagnostics|Watch for/
      );
    } finally {
      cleanup();
    }
  });

  it("says so when there's nothing to do", async () => {
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(view([portalStep({ state: 'Done' })]))
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

describe('a task', () => {
  it("shows the email's text, takes I've done this for a task done elsewhere, and returns with the next step", async () => {
    let done = false;
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(
          view(
            done
              ? [dmv({ state: 'InProgress' }), portalStep()]
              : [dmv(), portalStep()]
          )
        )
      ),
      taskBody(
        '<p>Hi Morgan,</p><p>Complete the <strong>DMV</strong> form.</p>'
      ),
      http.post('/api/portal/tasks/dmv/done', () => {
        done = true;
        return new HttpResponse(null, { status: 204 });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/portal' });
    try {
      await userEvent.click(
        await screen.findByRole('link', {
          name: 'Click here to complete the DMV Employer Pull Notice form',
        })
      );
      expect(
        await screen.findByRole('heading', {
          name: 'Complete the DMV Employer Pull Notice form',
        })
      ).toBeInTheDocument();
      expect(await screen.findByText('Hi Morgan,')).toBeInTheDocument();
      expect(
        screen.getByText('DMV', { selector: 'strong' })
      ).toBeInTheDocument();

      await userEvent.click(
        screen.getByRole('button', {
          name: 'Click here to complete the DMV Employer Pull Notice form',
        })
      );
      expect(done).toBe(false);
      await userEvent.click(
        screen.getByRole('button', { name: "I've done this" })
      );

      expect(
        await screen.findByText('Next up: Complete your drug screen.')
      ).toBeInTheDocument();
      expect(
        screen.getByText('Done: Complete the DMV Employer Pull Notice form.')
      ).toBeInTheDocument();
      expect(done).toBe(true);
    } finally {
      cleanup();
    }
  });

  it('shows its summary when it has no email of its own', async () => {
    server.use(
      http.get('/api/portal/me', () => HttpResponse.json(view([portalStep()]))),
      taskBody()
    );

    const { cleanup } = renderRoute({
      initialPath: '/portal/tasks/drug-screen',
    });
    try {
      expect(
        await screen.findByText(
          'Complete your drug screen at a testing location and time that works for you.'
        )
      ).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it("says so for a step that hasn't opened", async () => {
    server.use(
      http.get('/api/portal/me', () => HttpResponse.json(view([portalStep()])))
    );

    const { cleanup } = renderRoute({ initialPath: '/portal/tasks/cleared' });
    try {
      expect(
        await screen.findByText(/This step isn't open yet/)
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
    kind: 'Schedule',
    state: appointment ? 'InProgress' : 'ToDo',
    summary: null,
    taskLabel: 'Schedule your fingerprinting',
    title: 'Fingerprinting (Live Scan)',
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

describe('booking from a task', () => {
  it('books a time and returns to the checklist', async () => {
    let booked: unknown = null;
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(
          view([fingerprinting(booked ? appointment() : null), portalStep()])
        )
      ),
      taskBody(),
      http.get('/api/portal/appointments/fingerprinting/options', () =>
        HttpResponse.json(options)
      ),
      http.post('/api/portal/appointments', async ({ request }) => {
        booked = await request.json();
        return new HttpResponse(null, { status: 204 });
      })
    );

    const { cleanup } = renderRoute({
      initialPath: '/portal/tasks/fingerprinting',
    });
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
        await screen.findByText('Done: Schedule your fingerprinting.')
      ).toBeInTheDocument();
      expect(
        screen.getByText('Next up: Complete your drug screen.')
      ).toBeInTheDocument();
      expect(booked).toEqual({
        locationId: 2,
        start: '2026-10-26T09:15:00',
        typeKey: 'fingerprinting',
      });
    } finally {
      cleanup();
    }
  });

  it('cancels after asking and stays to book again', async () => {
    let cancelled: string | null = null;
    server.use(
      http.get('/api/portal/me', () =>
        HttpResponse.json(
          view([fingerprinting(cancelled ? null : appointment())])
        )
      ),
      taskBody(),
      http.post('/api/portal/appointments/:id/cancel', ({ params }) => {
        cancelled = String(params.id);
        return new HttpResponse(null, { status: 204 });
      })
    );

    const { cleanup } = renderRoute({
      initialPath: '/portal/tasks/fingerprinting',
    });
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
      expect(
        await screen.findByRole('button', {
          name: 'Book Fingerprinting (Live Scan)',
        })
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
      ),
      taskBody()
    );

    const { cleanup } = renderRoute({
      initialPath: '/portal/tasks/fingerprinting',
    });
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
      expect(await screen.findByText(/Welcome, Morgan/)).toBeInTheDocument();
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

describe('answering in a task', () => {
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
              kind: 'I9',
              state: remote ? 'InProgress' : 'ToDo',
              summary: null,
              taskLabel: 'Complete your I-9',
            }),
            ...(remote
              ? [
                  portalStep({
                    key: 'i9-remote',
                    kind: 'Representative',
                    representative: { email: null, name: null },
                    taskLabel: 'Name your I-9 representative',
                  }),
                ]
              : []),
          ])
        )
      ),
      taskBody(),
      http.post('/api/portal/i9/remote', () => {
        remote = true;
        return new HttpResponse(null, { status: 204 });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/portal/tasks/i9' });
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
        await screen.findByText('Next up: Name your I-9 representative.')
      ).toBeInTheDocument();
      expect(remote).toBe(true);
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
              kind: 'Representative',
              representative: saved ?? { email: null, name: null },
              state: saved ? 'InProgress' : 'ToDo',
              summary: null,
              taskLabel: 'Name your I-9 representative',
            }),
          ])
        )
      ),
      taskBody(),
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

    const { cleanup } = renderRoute({
      initialPath: '/portal/tasks/i9-remote',
    });
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
        await screen.findByText('Done: Name your I-9 representative.')
      ).toBeInTheDocument();
      expect(sent).toHaveLength(2);
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
            {
              ...fingerprinting(appointment({ canChange: false }), false),
              atiNumber: { value: saved },
            },
          ])
        )
      ),
      taskBody(),
      http.put('/api/portal/fingerprinting/ati', async ({ request }) => {
        saved = ((await request.json()) as { atiNumber: string }).atiNumber;
        return new HttpResponse(null, { status: 204 });
      })
    );

    const { cleanup } = renderRoute({
      initialPath: '/portal/tasks/fingerprinting',
    });
    try {
      await userEvent.type(
        await screen.findByLabelText('ATI number'),
        'A1B2C3D4E5'
      );
      await userEvent.click(screen.getByRole('button', { name: 'Send' }));

      expect(
        await screen.findByText('Done: Schedule your fingerprinting.')
      ).toBeInTheDocument();
      expect(saved).toBe('A1B2C3D4E5');
    } finally {
      cleanup();
    }
  });
});
