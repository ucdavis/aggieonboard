import { describe, expect, it } from 'vitest';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { StaffAdminView, TeamView } from '@/queries/admin.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';
import { meAsAdmin } from '@/test/staffFixtures.ts';

const sample: StaffAdminView = {
  assignedCandidates: 2,
  email: 'sample@example.test',
  id: 1,
  isActive: true,
  isAdmin: true,
  name: 'Sample User',
  phone: null,
  role: 'OnboardingSpecialist',
  teamIds: [1],
};

const pat: StaffAdminView = {
  ...sample,
  assignedCandidates: 1,
  email: 'pat.kim@example.test',
  id: 2,
  isAdmin: false,
  name: 'Pat Kim',
  phone: '916-555-0142',
  teamIds: [],
};

const teams: TeamView[] = [
  {
    id: 1,
    key: 'i9-desk',
    locations: ['Rancho Cordova'],
    name: 'SSO I-9 desk',
  },
  {
    id: 2,
    key: 'oehc',
    locations: ['Employee Health (OEHC), Sacramento'],
    name: 'Employee Health',
  },
];

describe('admin staff', () => {
  it('adds a schedule-only team member', async () => {
    let staff = [sample, pat];
    let posted: unknown = null;
    server.use(
      meAsAdmin(),
      http.get('/api/admin/staff', () => HttpResponse.json({ staff, teams })),
      http.post('/api/admin/staff', async ({ request }) => {
        posted = await request.json();
        const member: StaffAdminView = {
          ...pat,
          email: 'lee.park@example.test',
          id: 3,
          name: 'Lee Park',
          phone: null,
          role: 'TeamMember',
          teamIds: [2],
        };
        staff = [...staff, member];
        return HttpResponse.json({ member, notice: null });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/admin/staff' });
    try {
      await userEvent.click(
        await screen.findByRole('button', { name: 'Add staff member' })
      );
      const dialog = screen.getByRole('dialog', { name: 'Add staff member' });
      await userEvent.type(within(dialog).getByLabelText('Name'), 'Lee Park');
      await userEvent.type(
        within(dialog).getByLabelText('Email'),
        'lee.park@example.test'
      );
      await userEvent.selectOptions(
        within(dialog).getByLabelText('Role'),
        'TeamMember'
      );
      await userEvent.click(within(dialog).getByLabelText(/^Employee Health/));
      await userEvent.click(
        within(dialog).getByRole('button', { name: 'Add' })
      );

      expect(await screen.findByText('Lee Park is saved.')).toBeInTheDocument();
      expect(posted).toEqual({
        email: 'lee.park@example.test',
        isActive: true,
        isAdmin: false,
        name: 'Lee Park',
        phone: '',
        role: 'TeamMember',
        teamIds: [2],
      });
      expect(
        await screen.findByText('Team member (schedule only)')
      ).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it('keeps an admin from removing their own access and explains a refused change', async () => {
    server.use(
      meAsAdmin(),
      http.get('/api/admin/staff', () =>
        HttpResponse.json({ staff: [sample, pat], teams })
      ),
      http.put('/api/admin/staff/2', () =>
        HttpResponse.json(
          {
            detail:
              "Pat Kim is the onboarding specialist for 1 candidate. Reassign them on each candidate's page first.",
            status: 409,
          },
          { status: 409 }
        )
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/admin/staff' });
    try {
      await userEvent.click(
        await screen.findByRole('button', { name: 'Edit Sample User' })
      );
      const mine = screen.getByRole('dialog', { name: 'Edit Sample User' });
      expect(within(mine).getByLabelText('Can sign in')).toBeDisabled();
      expect(within(mine).getByLabelText(/^Admin:/)).toBeDisabled();
      await userEvent.click(
        within(mine).getByRole('button', { name: 'Cancel' })
      );

      await userEvent.click(
        screen.getByRole('button', { name: 'Edit Pat Kim' })
      );
      const dialog = screen.getByRole('dialog', { name: 'Edit Pat Kim' });
      await userEvent.selectOptions(
        within(dialog).getByLabelText('Role'),
        'Recruiter'
      );
      await userEvent.click(
        within(dialog).getByRole('button', { name: 'Save' })
      );

      expect(
        await within(dialog).findByText(
          "Pat Kim is the onboarding specialist for 1 candidate. Reassign them on each candidate's page first."
        )
      ).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });
});
