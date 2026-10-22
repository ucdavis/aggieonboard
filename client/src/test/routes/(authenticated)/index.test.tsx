import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';

describe('staff home route', () => {
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
      expect(
        screen.queryByRole('heading', { name: 'AggieOnboard' })
      ).not.toBeInTheDocument();
    } finally {
      consoleError.mockRestore();
      consoleWarn.mockRestore();
      cleanup?.();
    }
  });

  it('greets the signed-in staff member', async () => {
    server.use(
      http.get('/api/user/me', () =>
        HttpResponse.json({
          email: 'sample.user@example.edu',
          iamId: null,
          id: 'sample-user',
          name: 'Sample User',
          roles: ['User'],
        })
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/' });

    try {
      expect(
        await screen.findByRole('heading', { name: 'AggieOnboard' })
      ).toBeInTheDocument();
      expect(screen.getByText(/Welcome, Sample User\./)).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });
});
