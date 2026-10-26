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
    onSuccess: () => queryClient.invalidateQueries({ queryKey: adminKeys.all }),
  });
};

// Settings, step deadlines, holidays and the shared-data history.

export type SettingKind = 'Text' | 'Url' | 'Integer';

export type Setting = {
  description: string;
  isConfirmed: boolean;
  key: string;
  kind: SettingKind;
  openQuestion: string | null;
  value: string | null;
};

export type DeadlineAnchor =
  'None' | 'Activation' | 'Clearance' | 'BeforeStart' | 'AfterStart';

export type DeadlineUnit =
  'Hours' | 'BusinessHours' | 'CalendarDays' | 'BusinessDays' | 'Weeks';

export type StepDeadline = {
  amount: number;
  anchor: DeadlineAnchor;
  auditReference: string;
  confirmed: boolean;
  key: string;
  order: number;
  question: string | null;
  rule: string;
  title: string;
  unit: DeadlineUnit;
};

export type Holiday = { date: string; name: string };

export type AdminChangeResult = { notice: string };

export type AdminHistoryEntry = {
  action: string;
  actorName: string;
  at: string;
  detail: string;
};

const settingsKeys = {
  history: () => ['admin', 'history'] as const,
  holidays: () => ['admin', 'holidays'] as const,
  settings: () => ['admin', 'settings'] as const,
  steps: () => ['admin', 'steps'] as const,
};

export const settingsQueryOptions = () =>
  queryOptions({
    queryFn: () => fetchJson<Setting[]>('/api/admin/settings'),
    queryKey: settingsKeys.settings(),
  });

export const stepDeadlinesQueryOptions = () =>
  queryOptions({
    queryFn: () => fetchJson<StepDeadline[]>('/api/admin/steps'),
    queryKey: settingsKeys.steps(),
  });

export const holidaysQueryOptions = () =>
  queryOptions({
    queryFn: () => fetchJson<Holiday[]>('/api/admin/holidays'),
    queryKey: settingsKeys.holidays(),
  });

export const adminHistoryQueryOptions = () =>
  queryOptions({
    queryFn: () => fetchJson<AdminHistoryEntry[]>('/api/admin/history'),
    queryKey: settingsKeys.history(),
  });

/** A change to shared data: the lists, the history and any candidate's due dates may all move. */
const useAdminChange = <TVariables>(
  request: (variables: TVariables) => Promise<AdminChangeResult>
) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: request,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: adminKeys.all });
      await queryClient.invalidateQueries({ queryKey: ['staff'] });
    },
  });
};

export const useSaveSetting = () =>
  useAdminChange(
    ({
      key,
      ...edit
    }: {
      isConfirmed: boolean;
      key: string;
      openQuestion: string;
      value: string;
    }) =>
      fetchJson<AdminChangeResult>(
        `/api/admin/settings/${encodeURIComponent(key)}`,
        send(edit, 'PUT')
      )
  );

export const useSaveStepDeadline = () =>
  useAdminChange(
    ({
      key,
      ...edit
    }: {
      amount: number;
      anchor: DeadlineAnchor;
      confirmed: boolean;
      key: string;
      question: string;
      unit: DeadlineUnit;
    }) =>
      fetchJson<AdminChangeResult>(
        `/api/admin/steps/${encodeURIComponent(key)}/deadline`,
        send(edit, 'PUT')
      )
  );

export const useAddHoliday = () =>
  useAdminChange((holiday: { date: string; name: string }) =>
    fetchJson<AdminChangeResult>('/api/admin/holidays', send(holiday))
  );

export const useRemoveHoliday = () =>
  useAdminChange((date: string) =>
    fetchJson<AdminChangeResult>(`/api/admin/holidays/${date}`, {
      method: 'DELETE',
    })
  );
