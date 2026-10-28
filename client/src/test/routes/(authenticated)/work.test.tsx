import { describe, expect, it } from 'vitest';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { WorkItem, WorkList } from '@/queries/staff.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';
import {
  candidate,
  meAsStaff,
  noAppointments,
  step,
} from '@/test/staffFixtures.ts';

const item = (overrides: Partial<WorkItem> = {}): WorkItem => ({
  activatedAt: '2026-10-20T16:00:00Z',
  assigneeId: 3,
  assigneeName: 'Pat Kim',
  candidateId: 6,
  candidateName: 'Casey Brooks',
  dueDate: '2026-11-03',
  dueTime: null,
  isOverdue: false,
  needsStaff: false,
  positionTitle: 'Security Officer',
  status: 'ActionNeeded',
  stepKey: 'position-description',
  stepTitle: 'Position description',
  ...overrides,
});

describe('my work', () => {
  it("lists the group's open steps, what needs them first, clustered by who's responsible, you first", async () => {
    const work: WorkList = {
      group: 'ServiceChannel',
      items: [
        item({
          needsStaff: true,
          status: 'Waiting',
          stepKey: 'dmv',
          stepTitle: 'DMV Employer Pull Notice',
        }),
        item({
          assigneeId: 1,
          assigneeName: 'Sample User',
          candidateId: 5,
          candidateName: 'Morgan Chen',
          isOverdue: true,
          stepKey: 'drug-screen',
          stepTitle: 'Drug screen',
        }),
        item(),
        item({
          assigneeId: null,
          assigneeName: null,
          candidateId: 4,
          candidateName: 'Taylor Nguyen',
          stepKey: 'i9',
          stepTitle: 'I-9 verification',
        }),
      ],
      meId: 1,
    };
    const asked: string[] = [];
    server.use(
      meAsStaff(),
      http.get('/api/staff/work', ({ request }) => {
        asked.push(new URL(request.url).search);
        return HttpResponse.json(work);
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/work' });
    try {
      const needs = await screen.findByRole('region', {
        name: /Needs Service Channel/,
      });
      expect(
        within(needs).getByRole('link', { name: /Casey Brooks · DMV/ })
      ).toHaveAttribute('href', '/candidates/6?step=dmv');
      expect(
        within(needs).getByText("Their part's done: your move")
      ).toBeInTheDocument();

      const withThem = screen.getByRole('region', {
        name: /With new employees/,
      });
      const clusters = within(withThem)
        .getAllByRole('heading', { level: 3 })
        .map((h) => h.textContent);
      expect(clusters).toEqual([
        'Sample User (you)1',
        'Pat Kim1',
        'Nobody assigned1',
      ]);
      expect(within(withThem).getByText('Overdue')).toBeInTheDocument();

      await userEvent.click(screen.getByRole('link', { name: 'Recruiter' }));
      expect(asked).toContain('?group=Recruiter');
    } finally {
      cleanup();
    }
  });
});

describe('candidate timeline', () => {
  it('groups steps by who owns them and opens the step a link points to', async () => {
    server.use(
      meAsStaff(),
      noAppointments(),
      http.get('/api/staff/candidates/1', () =>
        HttpResponse.json(
          candidate({
            steps: [
              step({ completedAt: '2026-10-01T16:00:00Z', status: 'Complete' }),
              step({
                key: 'dmv',
                order: 10,
                owner: 'ServiceChannel',
                phase: 'PreEmployment',
                stage: 5,
                status: 'Waiting',
                title: 'DMV Employer Pull Notice',
              }),
              step({
                key: 'position-description',
                order: 12,
                owner: 'ServiceChannel',
                phase: 'PreEmployment',
                stage: 5,
                status: 'ActionNeeded',
                title: 'Position description',
              }),
              step({
                key: 'new-employee-welcome',
                order: 15,
                owner: 'UnitHr',
                phase: 'FirstDay',
                stage: 7,
                status: 'Upcoming',
                title: 'New Employee Welcome',
              }),
            ],
          })
        )
      )
    );

    const { cleanup } = renderRoute({
      initialPath: '/candidates/1?step=position-description',
    });
    try {
      const service = await screen.findByRole('button', {
        name: /^Service Channel/,
      });
      expect(service).toHaveAttribute('aria-expanded', 'true');
      expect(service).toHaveTextContent('0 of 2 done');
      expect(
        screen.getByRole('button', { name: /^Recruiter/ })
      ).toHaveAttribute('aria-expanded', 'false');
      expect(screen.getByRole('button', { name: /^Unit HR/ })).toHaveAttribute(
        'aria-expanded',
        'false'
      );

      expect(
        screen.getByRole('button', { name: /^DMV Employer Pull Notice/ })
      ).toHaveAttribute('aria-expanded', 'true');
      expect(
        screen.getByRole('button', { name: /^DMV Employer Pull Notice/ })
      ).toHaveTextContent('Waiting on Service Channel to confirm');
      expect(
        screen.getByRole('button', { name: /^Position description/ })
      ).toHaveAttribute('aria-expanded', 'true');
    } finally {
      cleanup();
    }
  });
});
