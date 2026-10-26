import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { TemplateDetail } from '@/queries/admin.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';
import { meAsAdmin, meAsStaff, messagePreview } from '@/test/staffFixtures.ts';

const template = (overrides: Partial<TemplateDetail> = {}): TemplateDetail => ({
  auditReference: '9',
  createdAt: '2026-10-22T16:00:00Z',
  createdBy: 'seed',
  fields: [{ description: "The candidate's first name.", name: 'FirstName' }],
  key: 'sso-welcome',
  latestVersion: 1,
  previewCandidates: [{ id: 5, name: 'Morgan Chen' }],
  sections: [{ body: 'Hi {{FirstName}},\n', condition: 'Always' }],
  sender: 'App',
  steps: ['Employee Health (OEHC)'],
  subject: 'Welcome to UC Davis Health',
  version: 1,
  versions: [
    { createdAt: '2026-10-22T16:00:00Z', createdBy: 'seed', version: 1 },
  ],
  ...overrides,
});

describe('template editor', () => {
  it('is only for admins', async () => {
    server.use(meAsStaff());
    const { cleanup } = renderRoute({
      initialPath: '/admin/templates/sso-welcome',
    });
    try {
      expect(
        await screen.findByText(
          'Admin pages are for AggieOnboard administrators.'
        )
      ).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });

  it('previews unsaved wording, shows field errors, then saves the next version', async () => {
    let current = template();
    const posted: unknown[] = [];
    server.use(
      meAsAdmin(),
      http.get('/api/admin/templates/sso-welcome', () =>
        HttpResponse.json(current)
      ),
      http.post(
        '/api/admin/templates/sso-welcome/preview',
        async ({ request }) => {
          const body = (await request.json()) as { subject: string };
          return HttpResponse.json(messagePreview({ subject: body.subject }));
        }
      ),
      http.post(
        '/api/admin/templates/sso-welcome/versions',
        async ({ request }) => {
          const body = (await request.json()) as {
            basedOnVersion: number;
            sections: { body: string }[];
            subject: string;
          };
          posted.push(body);
          if (body.sections[0].body.includes('{{Nickname}}')) {
            return HttpResponse.json(
              {
                errors: {
                  'sections[0].body': [
                    "{{Nickname}} isn't a field the app can fill. Use one from the list.",
                  ],
                },
                status: 400,
              },
              { status: 400 }
            );
          }
          current = template({
            latestVersion: 2,
            sections: [{ body: body.sections[0].body, condition: 'Always' }],
            subject: body.subject,
            version: 2,
            versions: [
              {
                createdAt: '2026-10-23T16:00:00Z',
                createdBy: 'sample@example.test',
                version: 2,
              },
              ...template().versions,
            ],
          });
          return HttpResponse.json(current);
        }
      )
    );

    const { cleanup } = renderRoute({
      initialPath: '/admin/templates/sso-welcome',
    });
    try {
      const subject = await screen.findByLabelText('Subject');
      await userEvent.clear(subject);
      await userEvent.type(subject, 'Welcome aboard');
      await userEvent.click(
        screen.getByRole('button', { name: 'Preview the wording above' })
      );
      expect(
        await screen.findByText('Welcome aboard', { selector: 'p' })
      ).toBeInTheDocument();

      const body = screen.getByLabelText('Section 1 wording');
      await userEvent.type(body, 'Hello {{{{Nickname}}');
      await userEvent.click(
        screen.getByRole('button', { name: 'Save as version 2' })
      );
      expect(
        await screen.findByText(
          "{{Nickname}} isn't a field the app can fill. Use one from the list."
        )
      ).toBeInTheDocument();
      expect(body).toHaveAttribute('aria-invalid', 'true');

      await userEvent.clear(body);
      await userEvent.type(body, 'Hello {{{{FirstName}}!');
      await userEvent.click(
        screen.getByRole('button', { name: 'Save as version 2' })
      );

      expect(
        await screen.findByText('Version 2 is saved. It sends from now on.')
      ).toBeInTheDocument();
      expect(posted.at(-1)).toEqual({
        basedOnVersion: 1,
        sections: [
          {
            body: 'Hello {{FirstName}}!',
            condition: 'Always',
          },
        ],
        subject: 'Welcome aboard',
      });
      expect(
        screen.getByRole('button', { name: 'Save as version 3' })
      ).toBeInTheDocument();
    } finally {
      cleanup();
    }
  });
});
