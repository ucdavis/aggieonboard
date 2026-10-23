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
  messagePreview,
} from '@/test/staffFixtures.ts';

const openPreview = async () => {
  await screen.findByRole('heading', { name: 'Alex Rivera' });
  const references = screen.getByRole('listitem', { name: /Reference check/ });
  await userEvent.click(
    within(references).getByRole('button', { name: 'Preview message' })
  );
  return screen.findByRole('dialog');
};

describe('candidate messages', () => {
  it('shows what is missing and will not send until it is fixed', async () => {
    server.use(
      meAsStaff(),
      noAppointments(),
      http.get('/api/staff/candidates/1', () => HttpResponse.json(candidate())),
      http.get(
        '/api/staff/candidates/1/messages/references-request/preview',
        () =>
          HttpResponse.json(
            messagePreview({
              canSend: false,
              missing: [
                {
                  description:
                    'When references should finish their SkillSurvey (message 1).',
                  fix: 'The ReferenceSurveyBusinessDays setting has no value yet.',
                  name: 'ReferenceSurveyDeadline',
                },
              ],
            })
          )
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/1' });
    try {
      const dialog = await openPreview();
      expect(
        await within(dialog).findByText(
          /can't be sent until these are filled in/
        )
      ).toBeInTheDocument();
      expect(
        within(dialog).getByText(
          'The ReferenceSurveyBusinessDays setting has no value yet.'
        )
      ).toBeInTheDocument();
      expect(
        within(dialog).getByRole('button', {
          name: 'Send to alex.rivera@example.test',
        })
      ).toBeDisabled();
      expect(within(dialog).getByTitle(/Preview of/)).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it('sends a ready message and says so on the step', async () => {
    let sends = 0;
    server.use(
      meAsStaff(),
      noAppointments(),
      http.get('/api/staff/candidates/1', () => HttpResponse.json(candidate())),
      http.get(
        '/api/staff/candidates/1/messages/references-request/preview',
        () => HttpResponse.json(messagePreview())
      ),
      http.post(
        '/api/staff/candidates/1/messages/references-request/send',
        () => {
          sends += 1;
          return HttpResponse.json(candidate());
        }
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/1' });
    try {
      const dialog = await openPreview();
      expect(within(dialog).getByText('Reference check')).toBeInTheDocument();
      await userEvent.click(
        await within(dialog).findByRole('button', {
          name: 'Send to alex.rivera@example.test',
        })
      );

      expect(
        await screen.findByText(
          'Message 1 was sent to alex.rivera@example.test.'
        )
      ).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(sends).toBe(1);
    } finally {
      cleanup();
    }
  });

  it('offers no send for a vendor’s wording', async () => {
    server.use(
      meAsStaff(),
      noAppointments(),
      http.get('/api/staff/candidates/1', () => HttpResponse.json(candidate())),
      http.get(
        '/api/staff/candidates/1/messages/skillsurvey-request/preview',
        () =>
          HttpResponse.json(
            messagePreview({
              blockers: [
                'SkillSurvey sends this message, not AggieOnboard. This is the wording proposed to them.',
              ],
              canSend: false,
              sender: 'External',
            })
          )
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/1' });
    try {
      await screen.findByRole('heading', { name: 'Alex Rivera' });
      const references = screen.getByRole('listitem', {
        name: /Reference check/,
      });
      await userEvent.click(
        within(references).getByRole('button', { name: "Vendor's wording" })
      );

      const dialog = await screen.findByRole('dialog');
      expect(
        await within(dialog).findByText(/SkillSurvey sends this message/)
      ).toBeInTheDocument();
      expect(
        within(dialog).queryByRole('button', { name: /Send to/ })
      ).not.toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it('opens a sent message exactly as it went out', async () => {
    server.use(
      meAsStaff(),
      noAppointments(),
      http.get('/api/staff/candidates/1', () =>
        HttpResponse.json(
          candidate({
            sentMessages: [
              {
                auditReference: '1',
                automatic: false,
                id: 42,
                key: 'references-request',
                sentAt: '2026-10-22T16:00:00Z',
                sentBy: 'Sample User',
                subject:
                  'Next step for Clinical Nurse II: Complete your reference check',
                to: 'alex.rivera@example.test',
              },
            ],
          })
        )
      ),
      http.get('/api/staff/candidates/1/sent/42', () =>
        HttpResponse.json({
          auditReference: '1',
          automatic: false,
          html: '<html><body>Hi Alex,</body></html>',
          id: 42,
          key: 'references-request',
          replyTo: 'sample@example.test',
          sentAt: '2026-10-22T16:00:00Z',
          sentBy: 'Sample User',
          subject:
            'Next step for Clinical Nurse II: Complete your reference check',
          templateVersion: 1,
          text: 'Hi Alex,',
          to: 'alex.rivera@example.test',
        })
      )
    );

    const { cleanup } = renderRoute({ initialPath: '/candidates/1' });
    try {
      await screen.findByRole('heading', { name: 'Alex Rivera' });
      await userEvent.click(
        screen.getByRole('button', {
          name: 'Next step for Clinical Nurse II: Complete your reference check',
        })
      );

      const dialog = await screen.findByRole('dialog');
      expect(
        await within(dialog).findByText('Message 1, version 1')
      ).toBeInTheDocument();
      expect(within(dialog).getByTitle(/Sent copy of/)).toHaveAttribute(
        'sandbox',
        ''
      );
    } finally {
      cleanup();
    }
  });
});
