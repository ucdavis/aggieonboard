import { describe, expect, it } from 'vitest';
import { screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { Holiday, Setting, StepDeadline } from '@/queries/admin.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';
import { meAsAdmin } from '@/test/staffFixtures.ts';

const clearance: Setting = {
  description:
    'Business days before the start date that every pre-employment clearance must be complete.',
  isConfirmed: false,
  key: 'ClearanceBusinessDaysBeforeStart',
  kind: 'Integer',
  openQuestion: 'SSO: confirm (audit #9).',
  value: '6',
};

const drugScreen: StepDeadline = {
  amount: 48,
  anchor: 'Activation',
  auditReference: '10',
  confirmed: false,
  key: 'drug-screen',
  order: 7,
  question: 'SSO: what does 48 business hours mean?',
  rule: '48 business hours after the step starts',
  title: 'Drug screen',
  unit: 'BusinessHours',
};

describe('admin settings', () => {
  it('saves a setting after showing why a value was refused, and reports moved due dates', async () => {
    let setting = clearance;
    let holidays: Holiday[] = [];
    const steps = [drugScreen];
    server.use(
      meAsAdmin(),
      http.get('/api/admin/settings', () => HttpResponse.json([setting])),
      http.get('/api/admin/steps', () => HttpResponse.json(steps)),
      http.get('/api/admin/holidays', () => HttpResponse.json(holidays)),
      http.put(
        '/api/admin/settings/ClearanceBusinessDaysBeforeStart',
        async ({ request }) => {
          const body = (await request.json()) as {
            isConfirmed: boolean;
            value: string;
          };
          if (!/^\d+$/.test(body.value)) {
            return HttpResponse.json(
              {
                errors: { value: ['Enter a whole number from 0 to 365.'] },
                status: 400,
              },
              { status: 400 }
            );
          }
          setting = {
            ...setting,
            isConfirmed: body.isConfirmed,
            value: body.value,
          };
          return HttpResponse.json({
            notice:
              "ClearanceBusinessDaysBeforeStart saved. 3 open due dates moved; each is noted in that candidate's history.",
          });
        }
      ),
      http.post('/api/admin/holidays', async ({ request }) => {
        holidays = [(await request.json()) as Holiday];
        return HttpResponse.json({ notice: 'Sandbox day added.' });
      })
    );

    const { cleanup } = renderRoute({ initialPath: '/admin/settings' });
    try {
      const row = await screen.findByRole('listitem', {
        name: 'ClearanceBusinessDaysBeforeStart',
      });
      await userEvent.click(within(row).getByRole('button', { name: 'Edit' }));
      const value = within(row).getByLabelText(
        'Value (leave blank if nobody knows yet)'
      );
      await userEvent.clear(value);
      await userEvent.type(value, 'seven');
      await userEvent.click(within(row).getByRole('button', { name: 'Save' }));
      expect(
        await within(row).findByText('Enter a whole number from 0 to 365.')
      ).toBeInTheDocument();

      await userEvent.clear(value);
      await userEvent.type(value, '7');
      await userEvent.click(
        within(row).getByLabelText('Confirmed by TA or SSO')
      );
      await userEvent.click(within(row).getByRole('button', { name: 'Save' }));

      expect(
        await screen.findByText(
          "ClearanceBusinessDaysBeforeStart saved. 3 open due dates moved; each is noted in that candidate's history."
        )
      ).toBeInTheDocument();
      expect(await within(row).findByText('Confirmed')).toBeInTheDocument();

      await userEvent.type(screen.getByLabelText('Date'), '2026-12-28');
      await userEvent.type(screen.getByLabelText('Holiday'), 'Sandbox day');
      await userEvent.click(
        screen.getByRole('button', { name: 'Add holiday' })
      );
      expect(await screen.findByText('Sandbox day added.')).toBeInTheDocument();
      expect(await screen.findByText(/Sandbox day$/)).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it('offers only day units for a deadline counted from the start date', async () => {
    server.use(
      meAsAdmin(),
      http.get('/api/admin/settings', () => HttpResponse.json([])),
      http.get('/api/admin/steps', () => HttpResponse.json([drugScreen])),
      http.get('/api/admin/holidays', () => HttpResponse.json([]))
    );

    const { cleanup } = renderRoute({ initialPath: '/admin/settings' });
    try {
      const row = await screen.findByRole('listitem', { name: 'Drug screen' });
      await userEvent.click(within(row).getByRole('button', { name: 'Edit' }));
      expect(
        within(row).getByRole('option', { name: 'business hours' })
      ).toBeInTheDocument();

      await userEvent.selectOptions(
        within(row).getByLabelText('Due'),
        'BeforeStart'
      );

      expect(
        within(row).queryByRole('option', { name: 'business hours' })
      ).toBeNull();
      expect(within(row).getByLabelText('Unit')).toHaveValue('BusinessDays');
    } finally {
      cleanup();
    }
  });
});
