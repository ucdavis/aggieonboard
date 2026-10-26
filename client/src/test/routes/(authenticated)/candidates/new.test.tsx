import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';
import { candidate, meAsStaff, noAppointments } from '@/test/staffFixtures.ts';

const staffMembers = () =>
  http.get('/api/staff/members', () =>
    HttpResponse.json([
      {
        email: 'dana.ortiz@example.test',
        id: 1,
        name: 'Dana Ortiz',
        role: 'RecruitingAssistant',
      },
      {
        email: 'pat.kim@example.test',
        id: 3,
        name: 'Pat Kim',
        role: 'OnboardingSpecialist',
      },
    ])
  );

const fillRequired = async () => {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText('First name'), 'Jesse');
  await user.type(screen.getByLabelText('Last name'), 'Rowe');
  await user.type(screen.getByLabelText('Email'), 'jesse.rowe@example.test');
  await user.type(
    screen.getByLabelText('Position title'),
    'Clinical Lab Scientist'
  );
  await user.type(screen.getByLabelText('Department'), 'Pathology');
  return user;
};

describe('new candidate route', () => {
  it('checks required details before sending anything', async () => {
    let posts = 0;
    server.use(
      meAsStaff(),
      noAppointments(),
      staffMembers(),
      http.post('/api/staff/candidates', () => {
        posts += 1;
        return HttpResponse.json(candidate());
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/new' });
    try {
      await screen.findByRole('heading', { name: 'New candidate' });
      await userEvent.click(
        screen.getByRole('button', { name: 'Create candidate' })
      );

      expect(
        await screen.findByText('First name is required.')
      ).toBeInTheDocument();
      expect(screen.getByText('Department is required.')).toBeInTheDocument();
      expect(posts).toBe(0);
    } finally {
      cleanup();
    }
  });

  it('sends the details, with requirements and team, and opens the new candidate', async () => {
    let body: Record<string, unknown> | undefined;
    server.use(
      meAsStaff(),
      noAppointments(),
      staffMembers(),
      http.post('/api/staff/candidates', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          candidate({ firstName: 'Jesse', id: 9, lastName: 'Rowe' }),
          { status: 201 }
        );
      }),
      http.get('/api/staff/candidates/9', () =>
        HttpResponse.json(
          candidate({ firstName: 'Jesse', id: 9, lastName: 'Rowe' })
        )
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/new' });
    try {
      await screen.findByRole('heading', { name: 'New candidate' });
      const user = await fillRequired();
      await user.click(screen.getByLabelText('Drug screen'));
      await user.selectOptions(
        await screen.findByLabelText('Recruiting assistant'),
        'Dana Ortiz'
      );
      await user.click(
        screen.getByRole('button', { name: 'Create candidate' })
      );

      expect(
        await screen.findByRole('heading', { name: 'Jesse Rowe' })
      ).toBeInTheDocument();
      expect(body).toMatchObject({
        anticipatedStartDate: null,
        firstName: 'Jesse',
        onboardingSpecialistId: null,
        recruitingAssistantId: 1,
        requirements: { drugScreen: true, parking: false },
      });
    } finally {
      cleanup();
    }
  });

  it('asks for the representative and ATI number only when they apply', async () => {
    let body: Record<string, unknown> | undefined;
    server.use(
      meAsStaff(),
      noAppointments(),
      staffMembers(),
      http.post('/api/staff/candidates', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(candidate({ id: 9 }), { status: 201 });
      }),
      http.get('/api/staff/candidates/9', () =>
        HttpResponse.json(candidate({ id: 9 }))
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/new' });
    try {
      await screen.findByRole('heading', { name: 'New candidate' });
      const user = await fillRequired();
      expect(screen.queryByLabelText('I-9 representative')).toBeNull();
      expect(screen.queryByLabelText('Fingerprinting ATI number')).toBeNull();

      await user.selectOptions(
        screen.getByLabelText('I-9 verification'),
        'Remote'
      );
      await user.click(screen.getByLabelText('Fingerprinting (Live Scan)'));
      await user.type(screen.getByLabelText('I-9 representative'), 'Jo Park');
      await user.type(
        screen.getByLabelText("Representative's email"),
        'jo@example.test'
      );
      await user.type(
        screen.getByLabelText('Fingerprinting ATI number'),
        'a1b2'
      );
      await user.click(
        screen.getByRole('button', { name: 'Create candidate' })
      );

      await screen.findByRole('heading', { name: 'Alex Rivera' });
      expect(body).toMatchObject({
        fingerprintAtiNumber: 'a1b2',
        i9Method: 'Remote',
        i9RepresentativeEmail: 'jo@example.test',
        i9RepresentativeName: 'Jo Park',
      });
    } finally {
      cleanup();
    }
  });

  it('puts the server’s field errors on the matching fields', async () => {
    server.use(
      meAsStaff(),
      noAppointments(),
      staffMembers(),
      http.post('/api/staff/candidates', () =>
        HttpResponse.json(
          {
            errors: {
              recruitingAssistantId: ['Pat Kim is not a recruiting assistant.'],
            },
            status: 400,
            title: 'One or more validation errors occurred.',
          },
          { status: 400 }
        )
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/new' });
    try {
      await screen.findByRole('heading', { name: 'New candidate' });
      const user = await fillRequired();
      await user.click(
        screen.getByRole('button', { name: 'Create candidate' })
      );

      expect(
        await screen.findByText('Pat Kim is not a recruiting assistant.')
      ).toBeInTheDocument();
      expect(screen.getByLabelText('Recruiting assistant')).toHaveAttribute(
        'aria-invalid',
        'true'
      );
      expect(
        screen.getByText(
          'Some details need fixing. See the highlighted fields.'
        )
      ).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });
});
