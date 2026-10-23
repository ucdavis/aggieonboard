import { describe, expect, it } from 'vitest';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { pacificToday } from '@/lib/journey.ts';
import type { RosterLocation } from '@/queries/schedule.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';
import { candidate, meAsStaff, staffUser } from '@/test/staffFixtures.ts';

const today = pacificToday();

const roster = (): RosterLocation[] => [
  {
    address: null,
    appointments: [
      {
        appointmentId: 11,
        bookedBy: 'Candidate',
        candidateId: 6,
        candidateName: 'Casey Brooks',
        department: 'Police and Security Services',
        end: `${today}T09:15:00`,
        positionTitle: 'Security Officer',
        start: `${today}T09:00:00`,
        status: 'Booked',
        typeName: 'Fingerprinting (Live Scan)',
      },
    ],
    locationId: 2,
    name: 'UC Davis Health Police Department, Sacramento',
    teamName: 'UC Davis Health Police Department',
  },
];

const locations = () =>
  http.get('/api/schedule/locations', () =>
    HttpResponse.json([
      {
        address: null,
        appointmentTypeKey: 'fingerprinting',
        appointmentTypeName: 'Fingerprinting (Live Scan)',
        id: 2,
        name: 'UC Davis Health Police Department, Sacramento',
        teamName: 'UC Davis Health Police Department',
      },
    ])
  );

const meAsTeamMember = () =>
  http.get('/api/user/me', () =>
    HttpResponse.json({
      ...staffUser,
      name: 'Schedule User',
      roles: ['TeamMember', 'Scheduling'],
    })
  );

describe('schedule', () => {
  it('sends a team member straight to the schedule, without the pipeline', async () => {
    server.use(
      meAsTeamMember(),
      locations(),
      http.get('/api/schedule/roster', () => HttpResponse.json(roster()))
    );

    const { cleanup } = renderRoute({ initialPath: '/' });
    try {
      expect(
        await screen.findByRole('heading', { name: 'Schedule' })
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('link', { name: 'Pipeline' })
      ).not.toBeInTheDocument();
      expect(await screen.findByText('Casey Brooks')).toBeInTheDocument();
      expect(
        screen.queryByRole('link', { name: 'Casey Brooks' })
      ).not.toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it("shows the day's roster by location and records attendance", async () => {
    const posted: unknown[] = [];
    server.use(
      meAsStaff(),
      locations(),
      http.get('/api/schedule/roster', ({ request }) => {
        expect(new URL(request.url).searchParams.get('date')).toBe(today);
        return HttpResponse.json(roster());
      }),
      http.post(
        '/api/schedule/appointments/11/attendance',
        async ({ request }) => {
          posted.push(await request.json());
          return HttpResponse.json({});
        }
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/schedule' });
    try {
      const location = await screen.findByRole('region', {
        name: 'UC Davis Health Police Department, Sacramento',
      });
      expect(
        within(location).getByText(
          'Address to be confirmed · UC Davis Health Police Department'
        )
      ).toBeInTheDocument();
      expect(within(location).getByText('9:00 a.m.')).toBeInTheDocument();
      expect(
        within(location).getByRole('link', { name: 'Casey Brooks' })
      ).toHaveAttribute('href', '/candidates/6');

      await userEvent.click(
        within(location).getByRole('button', { name: 'Attended' })
      );
      await expect.poll(() => posted).toEqual([{ attended: true }]);
    } finally {
      cleanup();
    }
  });

  it('asks for a reason before cancelling, and shows a refusal', async () => {
    server.use(
      meAsStaff(),
      locations(),
      http.get('/api/schedule/roster', () => HttpResponse.json(roster())),
      http.post('/api/schedule/appointments/11/cancel', () =>
        HttpResponse.json(
          {
            detail:
              'This appointment is attended; only a booked one can be cancelled.',
          },
          { status: 409 }
        )
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/schedule' });
    try {
      await userEvent.click(
        await screen.findByRole('button', { name: 'Cancel' })
      );
      await userEvent.type(
        screen.getByLabelText(/Reason/),
        'Rescheduled by phone'
      );
      await userEvent.click(
        screen.getByRole('button', { name: 'Cancel appointment' })
      );

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'only a booked one can be cancelled'
      );
    } finally {
      cleanup();
    }
  });

  it('saves weekly hours as a whole', async () => {
    let saved: unknown;
    server.use(
      meAsStaff(),
      http.get('/api/schedule/locations/2/availability', () =>
        HttpResponse.json({
          closures: [],
          hours: [
            {
              capacity: 1,
              day: 'Monday',
              end: '12:00:00',
              slotMinutes: 15,
              start: '09:00:00',
            },
          ],
          location: {
            address: null,
            appointmentTypeKey: 'fingerprinting',
            appointmentTypeName: 'Fingerprinting (Live Scan)',
            id: 2,
            name: 'UC Davis Health Police Department, Sacramento',
            teamName: 'UC Davis Health Police Department',
          },
          sessions: [],
        })
      ),
      http.put('/api/schedule/locations/2/hours', async ({ request }) => {
        saved = await request.json();
        return HttpResponse.json({
          closures: [],
          hours: [],
          location: {},
          sessions: [],
        });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/schedule/locations/2' });
    try {
      await screen.findByRole('heading', { name: 'Weekly hours' });
      await userEvent.click(screen.getByRole('button', { name: 'Add hours' }));
      await userEvent.click(screen.getByRole('button', { name: 'Save hours' }));

      await expect
        .poll(() => saved)
        .toEqual([
          {
            capacity: 1,
            day: 'Monday',
            end: '12:00',
            slotMinutes: 15,
            start: '09:00',
          },
          {
            capacity: 1,
            day: 'Monday',
            end: '12:00',
            slotMinutes: 15,
            start: '09:00',
          },
        ]);
    } finally {
      cleanup();
    }
  });
});

describe('booking for a candidate', () => {
  it('offers open times by day and books the chosen one as written', async () => {
    let booked: unknown;
    server.use(
      meAsStaff(),
      http.get('/api/staff/candidates/1', () => HttpResponse.json(candidate())),
      http.get('/api/staff/candidates/1/appointments', () =>
        HttpResponse.json([
          {
            current: null,
            past: [],
            stepKey: 'fingerprinting',
            typeKey: 'fingerprinting',
            typeName: 'Fingerprinting (Live Scan)',
          },
        ])
      ),
      http.get(
        '/api/staff/candidates/1/appointments/fingerprinting/options',
        () =>
          HttpResponse.json({
            current: null,
            earliest: `${today}T09:00:00`,
            latest: '2026-11-03',
            locations: [
              {
                address: null,
                locationId: 2,
                name: 'UC Davis Health Police Department, Sacramento',
                slots: [
                  {
                    capacity: 1,
                    end: '2026-10-27T09:15:00',
                    remaining: 1,
                    start: '2026-10-27T09:00:00',
                  },
                  {
                    capacity: 1,
                    end: '2026-10-27T09:30:00',
                    remaining: 1,
                    start: '2026-10-27T09:15:00',
                  },
                ],
              },
            ],
            typeKey: 'fingerprinting',
            typeName: 'Fingerprinting (Live Scan)',
            unavailable: null,
          })
      ),
      http.post('/api/staff/candidates/1/appointments', async ({ request }) => {
        booked = await request.json();
        return HttpResponse.json({});
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/1' });
    try {
      const row = await screen.findByRole('listitem', {
        name: 'Fingerprinting (Live Scan)',
      });
      await userEvent.click(within(row).getByRole('button', { name: 'Book' }));

      const dialog = await screen.findByRole('dialog');
      expect(
        await within(dialog).findByText(
          'Must be on or before Tue, Nov 3, 2026.'
        )
      ).toBeInTheDocument();
      expect(within(dialog).getByText('Tue, Oct 27, 2026')).toBeInTheDocument();
      await userEvent.click(
        within(dialog).getByRole('button', { name: '9:15 a.m.' })
      );
      await userEvent.click(
        within(dialog).getByRole('button', {
          name: 'Book Tue, Oct 27, 2026, 9:15 a.m.',
        })
      );

      await expect
        .poll(() => booked)
        .toEqual({
          locationId: 2,
          start: '2026-10-27T09:15:00',
          typeKey: 'fingerprinting',
        });
    } finally {
      cleanup();
    }
  });
});
