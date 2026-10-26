import {
  queryOptions,
  useMutation,
  useQueryClient,
} from '@tanstack/react-query';
import { fetchJson } from '../lib/api.ts';

// Shapes of /api/staff (server.core/Journey/JourneyModels.cs). Enums travel as their names.

/** The groups that own steps (Recruiter 1–4, Service Channel 5–14, Unit HR 15), and schedule-only team members. */
export type StaffRole =
  | 'Recruiter'
  | 'ServiceChannel'
  | 'UnitHr'
  /** Schedule only: no pipeline or candidate records. */
  | 'TeamMember';

export type JourneyPhase = 'TalentAcquisition' | 'PreEmployment' | 'FirstDay';

export type StepStatus =
  'Upcoming' | 'ActionNeeded' | 'Waiting' | 'Complete' | 'Skipped' | 'Blocked';

export type StepAction =
  'Activate' | 'MarkWaiting' | 'Complete' | 'Block' | 'Unblock' | 'Reopen';

export type I9Method = 'NotChosen' | 'InPerson' | 'Remote';

export type QualificationKind = 'License' | 'Certification' | 'Degree';

export type StaffSummary = {
  email: string;
  id: number;
  name: string;
  role: StaffRole;
};

export type PipelineRow = {
  anticipatedStartDate: string | null;
  applicableSteps: number;
  completedSteps: number;
  currentStepKey: string | null;
  /** The group whose step is current: whose move it is. */
  currentStepOwner: StaffRole | null;
  currentStepStatus: StepStatus | null;
  currentStepTitle: string | null;
  department: string;
  id: number;
  isOverdue: boolean;
  name: string;
  nextDueDate: string | null;
  nextDueTime: string | null;
  openSteps: number;
  phase: JourneyPhase | null;
  positionTitle: string;
  recruiter: string | null;
  serviceChannel: string | null;
  unitHr: string | null;
};

export type CandidateRequirements = {
  dmvPullNotice: boolean;
  drugScreen: boolean;
  fingerprinting: boolean;
  parking: boolean;
  positionDescription: boolean;
};

export type Qualification = {
  documentToSubmit: string | null;
  kind: QualificationKind;
  name: string;
};

export type StepDetail = {
  acknowledgmentMessageKey: string | null;
  activatedAt: string | null;
  auditReference: string;
  availableActions: StepAction[];
  completedAt: string | null;
  completedBy: string | null;
  condition: string;
  deadlineConfirmed: boolean;
  deadlineQuestion: string | null;
  deadlineRule: string;
  dueDate: string | null;
  dueOverridden: boolean;
  dueTime: string | null;
  isOverdue: boolean;
  key: string;
  order: number;
  owner: StaffRole;
  phase: JourneyPhase;
  requestMessageKey: string | null;
  staffNote: string | null;
  status: StepStatus;
  title: string;
  vendorMessageKey: string | null;
  watchForSender: string | null;
};

export type HistoryEntry = {
  action: string;
  actorName: string;
  at: string;
  detail: string;
};

export type CandidateDetail = {
  anticipatedStartDate: string | null;
  clearanceDeadline: string | null;
  createdAt: string;
  department: string;
  email: string;
  employeeId: string | null;
  fingerprintAtiNumber: string | null;
  firstName: string;
  hiringManagerContact: string | null;
  history: HistoryEntry[];
  i9Method: I9Method;
  i9RepresentativeEmail: string | null;
  i9RepresentativeName: string | null;
  id: number;
  lastName: string;
  /** A side effect of the change just made, such as an automatic message. */
  notice?: string | null;
  portalLastSignInAt: string | null;
  positionTitle: string;
  qualifications: Qualification[];
  recruiter: StaffSummary | null;
  requirements: CandidateRequirements;
  sentMessages: SentMessageSummary[];
  serviceChannel: StaffSummary | null;
  steps: StepDetail[];
  unitHr: StaffSummary | null;
};

export type MissingField = { description: string; fix: string; name: string };

export type MessagePreview = {
  auditReference: string;
  blockers: string[];
  canSend: boolean;
  html: string;
  key: string;
  missing: MissingField[];
  replyTo: string | null;
  sender: 'App' | 'External';
  startsSteps: string[];
  subject: string;
  templateVersion: number;
  text: string;
  timesSent: number;
  to: string;
};

export type SentMessageSummary = {
  auditReference: string;
  automatic: boolean;
  id: number;
  key: string;
  sentAt: string;
  sentBy: string;
  subject: string;
  to: string;
};

export type SentMessageView = SentMessageSummary & {
  html: string;
  replyTo: string | null;
  templateVersion: number;
  text: string;
};

export type CandidateInput = {
  anticipatedStartDate: string | null;
  department: string;
  email: string;
  employeeId: string | null;
  fingerprintAtiNumber: string | null;
  firstName: string;
  hiringManagerContact: string | null;
  i9Method: I9Method;
  i9RepresentativeEmail: string | null;
  i9RepresentativeName: string | null;
  lastName: string;
  positionTitle: string;
  qualifications: Qualification[];
  recruiterId: number | null;
  requirements: CandidateRequirements;
  serviceChannelId: number | null;
  unitHrId: number | null;
};

export const staffKeys = {
  all: ['staff'] as const,
  candidate: (id: number) => ['staff', 'candidates', id] as const,
  members: () => ['staff', 'members'] as const,
  pipeline: (mine: boolean) => ['staff', 'pipeline', { mine }] as const,
};

export const pipelineQueryOptions = (mine: boolean) =>
  queryOptions({
    queryFn: () =>
      fetchJson<PipelineRow[]>(
        `/api/staff/pipeline${mine ? '?mine=true' : ''}`
      ),
    queryKey: staffKeys.pipeline(mine),
  });

export const candidateQueryOptions = (id: number) =>
  queryOptions({
    queryFn: () => fetchJson<CandidateDetail>(`/api/staff/candidates/${id}`),
    queryKey: staffKeys.candidate(id),
  });

/** Always fetched fresh: a preview reflects deadlines and settings as of now. */
export const messagePreviewQueryOptions = (id: number, key: string) =>
  queryOptions({
    gcTime: 0,
    queryFn: () =>
      fetchJson<MessagePreview>(
        `/api/staff/candidates/${id}/messages/${key}/preview`
      ),
    queryKey: ['staff', 'candidates', id, 'preview', key] as const,
    staleTime: 0,
  });

export const sentMessageQueryOptions = (id: number, sentId: number) =>
  queryOptions({
    queryFn: () =>
      fetchJson<SentMessageView>(`/api/staff/candidates/${id}/sent/${sentId}`),
    queryKey: ['staff', 'candidates', id, 'sent', sentId] as const,
    staleTime: Infinity,
  });

export const staffMembersQueryOptions = () =>
  queryOptions({
    queryFn: () => fetchJson<StaffSummary[]>('/api/staff/members'),
    queryKey: staffKeys.members(),
    staleTime: 5 * 60_000,
  });

/** Every change returns the candidate as it now stands: cache it, and let the pipeline refetch. */
const useCandidateMutation = <TVariables>(
  request: (variables: TVariables) => Promise<CandidateDetail>
) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: request,
    onSuccess: (detail) => {
      queryClient.setQueryData(staffKeys.candidate(detail.id), detail);
      void queryClient.invalidateQueries({
        queryKey: ['staff', 'pipeline'],
      });
    },
  });
};

const send = (method: string, body: unknown) => ({
  body: JSON.stringify(body),
  method,
});

export const useCreateCandidate = () =>
  useCandidateMutation((input: CandidateInput) =>
    fetchJson<CandidateDetail>('/api/staff/candidates', send('POST', input))
  );

export const useUpdateCandidate = (id: number) =>
  useCandidateMutation((input: CandidateInput) =>
    fetchJson<CandidateDetail>(
      `/api/staff/candidates/${id}`,
      send('PUT', input)
    )
  );

export const useStepAction = (id: number) =>
  useCandidateMutation(
    ({
      action,
      reason,
      stepKey,
    }: {
      action: StepAction;
      reason?: string;
      stepKey: string;
    }) =>
      fetchJson<CandidateDetail>(
        `/api/staff/candidates/${id}/steps/${stepKey}/actions/${action}`,
        send('POST', { reason: reason || null })
      )
  );

export const useStepDue = (id: number) =>
  useCandidateMutation(
    ({ date, stepKey }: { date: string | null; stepKey: string }) =>
      fetchJson<CandidateDetail>(
        `/api/staff/candidates/${id}/steps/${stepKey}/due`,
        send('PUT', { date, time: null })
      )
  );

export const useStepNote = (id: number) =>
  useCandidateMutation(
    ({ note, stepKey }: { note: string | null; stepKey: string }) =>
      fetchJson<CandidateDetail>(
        `/api/staff/candidates/${id}/steps/${stepKey}/note`,
        send('PUT', { note })
      )
  );

export const useSendMessage = (id: number) =>
  useCandidateMutation((key: string) =>
    fetchJson<CandidateDetail>(
      `/api/staff/candidates/${id}/messages/${key}/send`,
      { method: 'POST' }
    )
  );
