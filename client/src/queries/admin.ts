import {
  queryOptions,
  useMutation,
  useQueryClient,
} from '@tanstack/react-query';
import { fetchJson } from '../lib/api.ts';
import type { MessagePreview } from './staff.ts';

// Shapes of /api/admin (server.core/Admin). Admins only; everyone else gets a 403.

export type CandidateCondition =
  | 'Always'
  | 'HasQualifications'
  | 'HasLicenses'
  | 'HasDegrees'
  | 'DrugScreen'
  | 'DmvPullNotice'
  | 'Fingerprinting'
  | 'PositionDescription'
  | 'Parking'
  | 'RemoteI9';

export type TemplateSummary = {
  auditReference: string;
  key: string;
  sender: 'App' | 'External';
  steps: string[];
  subject: string;
  updatedAt: string;
  updatedBy: string;
  version: number;
};

export type TemplateSection = { body: string; condition: CandidateCondition };

export type TemplateDetail = {
  auditReference: string;
  createdAt: string;
  createdBy: string;
  fields: { description: string; name: string }[];
  key: string;
  latestVersion: number;
  previewCandidates: { id: number; name: string }[];
  sections: TemplateSection[];
  sender: 'App' | 'External';
  steps: string[];
  subject: string;
  version: number;
  versions: { createdAt: string; createdBy: string; version: number }[];
};

export type TemplateEdit = {
  basedOnVersion: number;
  sections: TemplateSection[];
  subject: string;
};

export const adminKeys = {
  all: ['admin'] as const,
  template: (key: string, version?: number) =>
    ['admin', 'templates', key, version ?? 'latest'] as const,
  templates: () => ['admin', 'templates'] as const,
};

export const templatesQueryOptions = () =>
  queryOptions({
    queryFn: () => fetchJson<TemplateSummary[]>('/api/admin/templates'),
    queryKey: adminKeys.templates(),
  });

export const templateQueryOptions = (key: string, version?: number) =>
  queryOptions({
    queryFn: () =>
      fetchJson<TemplateDetail>(
        `/api/admin/templates/${encodeURIComponent(key)}${version ? `?version=${version}` : ''}`
      ),
    queryKey: adminKeys.template(key, version),
  });

const send = (body: unknown, method = 'POST') => ({
  body: JSON.stringify(body),
  method,
});

export const usePreviewTemplate = (key: string) =>
  useMutation({
    mutationFn: (draft: {
      candidateId: number;
      sections: TemplateSection[];
      subject: string;
    }) =>
      fetchJson<MessagePreview>(
        `/api/admin/templates/${encodeURIComponent(key)}/preview`,
        send(draft)
      ),
  });

export const useSaveTemplate = (key: string) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (edit: TemplateEdit) =>
      fetchJson<TemplateDetail>(
        `/api/admin/templates/${encodeURIComponent(key)}/versions`,
        send(edit)
      ),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: adminKeys.templates() }),
  });
};
