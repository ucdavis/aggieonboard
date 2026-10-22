import {
  queryOptions,
  useMutation,
  useQueryClient,
} from '@tanstack/react-query';
import { fetchJson } from '../lib/api.ts';

// Shapes of /api/staff (server.core/Journey/JourneyModels.cs). Enums travel as their names.

export type StaffRole =
  | 'RecruitingAssistant'
  | 'Recruiter'
  | 'OnboardingSpecialist'
  | 'LearningAndDevelopment';

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
  currentStepStatus: StepStatus | null;
  currentStepTitle: string | null;
  department: string;
  id: number;
  isOverdue: boolean;
  name: string;
  nextDueDate: string | null;
  nextDueTime: string | null;
  onboardingSpecialist: string | null;
  openSteps: number;
  phase: JourneyPhase | null;
  positionTitle: string;
  recruiter: string | null;
  recruitingAssistant: string | null;
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
  staffNote: string | null;
  status: StepStatus;
  title: string;
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
  firstName: string;
  hiringManagerContact: string | null;
  history: HistoryEntry[];
  i9Method: I9Method;
  id: number;
  lastName: string;
  onboardingSpecialist: StaffSummary | null;
  positionTitle: string;
  qualifications: Qualification[];
  recruiter: StaffSummary | null;
  recruitingAssistant: StaffSummary | null;
  requirements: CandidateRequirements;
  steps: StepDetail[];
};

export type CandidateInput = {
  anticipatedStartDate: string | null;
  department: string;
  email: string;
  employeeId: string | null;
  firstName: string;
  hiringManagerContact: string | null;
  i9Method: I9Method;
  lastName: string;
  onboardingSpecialistId: number | null;
  positionTitle: string;
  qualifications: Qualification[];
  recruiterId: number | null;
  recruitingAssistantId: number | null;
  requirements: CandidateRequirements;
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
