import { http, HttpResponse } from 'msw';
import type {
  CandidateDetail,
  PipelineRow,
  StepDetail,
} from '@/queries/staff.ts';

// Fictional data in the API's shapes, for route tests.

export const staffUser = {
  email: 'sample@example.test',
  iamId: null,
  id: 'sandbox-sample',
  name: 'Sample User',
  roles: ['Staff', 'OnboardingSpecialist'],
};

export const meAsStaff = () =>
  http.get('/api/user/me', () => HttpResponse.json(staffUser));

export const pipelineRow = (
  overrides: Partial<PipelineRow> = {}
): PipelineRow => ({
  anticipatedStartDate: null,
  applicableSteps: 8,
  completedSteps: 1,
  currentStepKey: 'offer',
  currentStepStatus: 'ActionNeeded',
  currentStepTitle: 'Offer',
  department: 'Department of Surgery',
  id: 3,
  isOverdue: false,
  name: 'Sam Patel',
  nextDueDate: null,
  nextDueTime: null,
  onboardingSpecialist: null,
  openSteps: 1,
  phase: 'TalentAcquisition',
  positionTitle: 'Administrative Assistant II',
  recruiter: 'Chris Alvarez',
  recruitingAssistant: 'Dana Ortiz',
  ...overrides,
});

export const step = (overrides: Partial<StepDetail> = {}): StepDetail => ({
  activatedAt: null,
  auditReference: '1–3',
  availableActions: ['Activate', 'Complete', 'Block'],
  completedAt: null,
  completedBy: null,
  condition: 'Always',
  deadlineConfirmed: false,
  deadlineQuestion: 'TA: confirm the reference deadlines (audit #1).',
  deadlineRule: '2 business days after the step starts',
  dueDate: null,
  dueOverridden: false,
  dueTime: null,
  isOverdue: false,
  key: 'references',
  order: 1,
  owner: 'RecruitingAssistant',
  phase: 'TalentAcquisition',
  staffNote: null,
  status: 'Upcoming',
  title: 'Reference check',
  watchForSender: 'SkillSurvey',
  ...overrides,
});

export const candidate = (
  overrides: Partial<CandidateDetail> = {}
): CandidateDetail => ({
  anticipatedStartDate: '2026-11-30',
  clearanceDeadline: '2026-11-18',
  createdAt: '2026-10-01T16:00:00Z',
  department: 'Emergency Department',
  email: 'alex.rivera@example.test',
  employeeId: null,
  firstName: 'Alex',
  hiringManagerContact: null,
  history: [],
  i9Method: 'NotChosen',
  id: 1,
  lastName: 'Rivera',
  onboardingSpecialist: null,
  positionTitle: 'Clinical Nurse II',
  qualifications: [],
  recruiter: null,
  recruitingAssistant: null,
  requirements: {
    dmvPullNotice: false,
    drugScreen: false,
    fingerprinting: false,
    parking: false,
    positionDescription: false,
  },
  steps: [
    step(),
    step({
      auditReference: '10',
      availableActions: [],
      condition: 'DrugScreen',
      key: 'drug-screen',
      order: 7,
      owner: 'OnboardingSpecialist',
      phase: 'PreEmployment',
      status: 'Skipped',
      title: 'Drug screen',
      watchForSender: 'Quest Diagnostics',
    }),
  ],
  ...overrides,
});
