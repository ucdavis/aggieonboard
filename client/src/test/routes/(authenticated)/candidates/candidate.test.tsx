import { describe, expect, it } from 'vitest';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';
import {
  candidate,
  meAsStaff,
  noAppointments,
  step,
} from '@/test/staffFixtures.ts';

const stepCard = (title: string) =>
  screen.getByRole('listitem', { name: new RegExp(title) });

/** Steps are lines on the timeline; clicking one opens its details. */
const openStep = (title: string) =>
  userEvent.click(
    screen.getByRole('button', { name: new RegExp(`^${title}`) })
  );

describe('candidate route', () => {
  it('shows each step with its rule, open question, and the moves it allows', async () => {
    server.use(
      meAsStaff(),
      noAppointments(),
      http.get('/api/staff/candidates/1', () => HttpResponse.json(candidate()))
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/1' });
    try {
      expect(
        await screen.findByRole('heading', { name: 'Alex Rivera' })
      ).toBeInTheDocument();

      expect(
        screen.getByRole('button', { name: /^Recruiter/ })
      ).toHaveAttribute('aria-expanded', 'true');
      await openStep('Reference check');
      const references = stepCard('Reference check');
      expect(within(references).getByText('Upcoming')).toBeInTheDocument();
      expect(within(references).getByText('Unconfirmed')).toBeInTheDocument();
      expect(
        within(references).getByText(
          'TA: confirm the reference deadlines (audit #1).'
        )
      ).toBeInTheDocument();
      expect(
        within(references).getByRole('button', { name: 'Start' })
      ).toBeInTheDocument();

      await userEvent.click(
        screen.getByRole('button', { name: /^Service Channel/ })
      );
      await userEvent.click(
        screen.getByRole('button', { name: 'Not needed for this position (1)' })
      );
      await openStep('Drug screen');
      const drug = stepCard('Drug screen');
      expect(within(drug).getByText("Doesn't apply")).toBeInTheDocument();
      expect(within(drug).getAllByRole('button')).toHaveLength(1);
    } finally {
      cleanup();
    }
  });

  it('starts a step and shows the state the server returns', async () => {
    const posted: string[] = [];
    server.use(
      meAsStaff(),
      noAppointments(),
      http.get('/api/staff/candidates/1', () => HttpResponse.json(candidate())),
      http.post(
        '/api/staff/candidates/1/steps/:key/actions/:move',
        ({ params }) => {
          posted.push(`${params.key}/${params.move}`);
          return HttpResponse.json(
            candidate({
              history: [
                {
                  action: 'step.activated',
                  actorName: 'Sample User',
                  at: '2026-10-22T16:00:00Z',
                  detail: 'Reference check: upcoming → action needed',
                },
              ],
              steps: [
                step({
                  activatedAt: '2026-10-22T16:00:00Z',
                  availableActions: ['MarkWaiting', 'Complete', 'Block'],
                  dueDate: '2026-10-26',
                  status: 'ActionNeeded',
                }),
              ],
            })
          );
        }
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/1' });
    try {
      await screen.findByRole('heading', { name: 'Alex Rivera' });
      await openStep('Reference check');
      await userEvent.click(
        within(stepCard('Reference check')).getByRole('button', {
          name: 'Start',
        })
      );

      expect(
        await within(stepCard('Reference check')).findByText('Action needed')
      ).toBeInTheDocument();
      expect(screen.getByText('Mon, Oct 26, 2026')).toBeInTheDocument();
      expect(
        screen.getByText('Reference check: upcoming → action needed')
      ).toBeInTheDocument();
      expect(posted).toEqual(['references/Activate']);
    } finally {
      cleanup();
    }
  });

  it('explains a move the server refuses', async () => {
    server.use(
      meAsStaff(),
      noAppointments(),
      http.get('/api/staff/candidates/1', () => HttpResponse.json(candidate())),
      http.post('/api/staff/candidates/1/steps/:key/actions/:move', () =>
        HttpResponse.json(
          {
            detail:
              'Reference check is complete; it cannot be started from there.',
            status: 409,
            title: "That change isn't allowed.",
          },
          { status: 409 }
        )
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/1' });
    try {
      await screen.findByRole('heading', { name: 'Alex Rivera' });
      await openStep('Reference check');
      await userEvent.click(
        within(stepCard('Reference check')).getByRole('button', {
          name: 'Start',
        })
      );

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'Reference check is complete; it cannot be started from there.'
      );
    } finally {
      cleanup();
    }
  });

  it('says so when the candidate does not exist', async () => {
    server.use(
      meAsStaff(),
      noAppointments(),
      http.get(
        '/api/staff/candidates/99',
        () => new HttpResponse(null, { status: 404 })
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/99' });
    try {
      expect(await screen.findByRole('alert')).toHaveTextContent(
        'There is no candidate with that ID.'
      );
    } finally {
      cleanup();
    }
  });
});
