import {
  queryOptions,
  useMutation,
  useQueryClient,
} from '@tanstack/react-query';
import { fetchJson } from '../lib/api.ts';
import type { MessagePreview, StaffRole } from './staff.ts';

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

// The staff whitelist and schedule teams.

export type StaffAdminView = {
  assignedCandidates: number;
  email: string;
  id: number;
  isActive: boolean;
  isAdmin: boolean;
  name: string;
  phone: string | null;
  role: StaffRole;
  teamIds: number[];
};

export type TeamView = {
  id: number;
  key: string;
  locations: string[];
  name: string;
};

export type StaffInput = {
  email: string;
  isActive: boolean;
  isAdmin: boolean;
  name: string;
  phone: string;
  role: StaffRole;
  teamIds: number[];
};

export const staffListQueryOptions = () =>
  queryOptions({
    queryFn: () =>
      fetchJson<{ staff: StaffAdminView[]; teams: TeamView[] }>(
        '/api/admin/staff'
      ),
    queryKey: ['admin', 'staff'] as const,
  });

export const useSaveStaff = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...input }: StaffInput & { id: number | null }) =>
      fetchJson<{ member: StaffAdminView; notice: string | null }>(
        id ? `/api/admin/staff/${id}` : '/api/admin/staff',
        send(input, id ? 'PUT' : 'POST')
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: adminKeys.all });
      await queryClient.invalidateQueries({ queryKey: ['staff', 'members'] });
    },
  });
};

// Emulation: see the app as a staff member, or a candidate's checklist. Read-only.

export type EmulationTargets = {
  candidates: {
    currentStep: string | null;
    id: number;
    name: string;
    positionTitle: string;
  }[];
  staff: {
    email: string;
    id: number;
    isAdmin: boolean;
    name: string;
    role: StaffRole;
    teams: string[];
  }[];
};

export type EmulationStatus = {
  role: StaffRole;
  signedInAs: string;
  viewingAs: string;
};

export const emulationTargetsQueryOptions = () =>
  queryOptions({
    queryFn: () => fetchJson<EmulationTargets>('/api/admin/emulation'),
    queryKey: ['admin', 'emulation'] as const,
  });

/** Null when nobody is being emulated (the API answers 204). */
export const emulationStatusQueryOptions = () =>
  queryOptions({
    queryFn: async () =>
      (await fetchJson<EmulationStatus | ''>('/api/user/emulation')) || null,
    queryKey: ['user', 'emulation'] as const,
  });

// Starting or stopping changes who the whole app shows, so each reloads the page rather than
// patching cached data.
export const useEmulateStaff = () =>
  useMutation({
    mutationFn: (id: number) =>
      fetchJson<void>(`/api/admin/emulation/staff/${id}`, { method: 'POST' }),
    onSuccess: () => window.location.assign('/'),
  });

export const useEmulateCandidate = () =>
  useMutation({
    mutationFn: (id: number) =>
      fetchJson<void>(`/api/admin/emulation/candidates/${id}`, {
        method: 'POST',
      }),
    onSuccess: () => window.location.assign('/portal'),
  });

export const useStopEmulation = () =>
  useMutation({
    mutationFn: () =>
      fetchJson<void>('/api/user/emulation/stop', { method: 'POST' }),
    onSuccess: () => window.location.assign('/admin/emulate'),
  });
