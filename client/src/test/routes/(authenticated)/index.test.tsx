import { describe, expect, it, vi } from 'vitest';
import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';
import { meAsStaff, pipelineRow, staffUser } from '@/test/staffFixtures.ts';

describe('pipeline route', () => {
  it('renders the not-authorized state when the signed-in user is forbidden', async () => {
    const consoleError = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);
    const consoleWarn = vi
      .spyOn(console, 'warn')
      .mockImplementation(() => undefined);

    server.use(
      http.get('/api/user/me', () => new HttpResponse(null, { status: 403 }))
    );

    let cleanup: (() => void) | undefined;

    try {
      ({ cleanup } = renderRoute({ initialPath: '/' }));
      expect(
        await screen.findByRole('heading', { name: 'Access unavailable' })
      ).toBeInTheDocument();
    } finally {
      consoleError.mockRestore();
      consoleWarn.mockRestore();
      cleanup?.();
    }
  });

  it('turns away a signed-in account that is not on the staff whitelist, without loading candidates', async () => {
    let pipelineRequests = 0;
    server.use(
      http.get('/api/user/me', () =>
        HttpResponse.json({ ...staffUser, name: 'Basic User', roles: [] })
      ),
      http.get('/api/staff/pipeline', () => {
        pipelineRequests += 1;
        return HttpResponse.json([]);
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/' });
    try {
      expect(
        await screen.findByRole('heading', { name: 'Access unavailable' })
      ).toBeInTheDocument();
      expect(pipelineRequests).toBe(0);
    } finally {
      cleanup();
    }
  });

  it('lists candidates and flags the overdue ones', async () => {
    server.use(
      meAsStaff(),
      http.get('/api/staff/pipeline', () =>
        HttpResponse.json([
          pipelineRow({
            currentStepKey: 'employee-data',
            currentStepTitle: 'Employee Data Gathering Form',
            id: 4,
            isOverdue: true,
            name: 'Taylor Nguyen',
            nextDueDate: '2026-10-19',
            phase: 'PreEmployment',
            positionTitle: 'Patient Care Technician',
          }),
          pipelineRow(),
        ])
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/' });
    try {
      expect(
        await screen.findByRole('heading', { name: 'Candidate pipeline' })
      ).toBeInTheDocument();
      expect(
        await screen.findByText('2 candidates, 1 overdue')
      ).toBeInTheDocument();

      const taylor = screen.getByRole('link', { name: 'Taylor Nguyen' });
      expect(taylor).toHaveAttribute('href', '/candidates/4');
      const row = taylor.closest('tr')!;
      expect(within(row).getByText('Overdue')).toBeInTheDocument();
      expect(
        within(row).getByText('Employee Data Gathering Form')
      ).toBeInTheDocument();
      expect(within(row).getByText('1 of 8')).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it('asks the API for only my candidates when filtered', async () => {
    const requested: string[] = [];
    server.use(
      meAsStaff(),
      http.get('/api/staff/pipeline', ({ request }) => {
        requested.push(new URL(request.url).search);
        return HttpResponse.json([]);
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/?mine=true' });
    try {
      expect(
        await screen.findByText('No candidates are assigned to you.')
      ).toBeInTheDocument();
      expect(requested).toEqual(['?mine=true']);
    } finally {
      cleanup();
    }
  });
});
