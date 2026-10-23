import type {
  I9Method,
  JourneyPhase,
  StaffRole,
  StepAction,
  StepStatus,
} from '@/queries/staff.ts';
import { HttpError } from './api.ts';

// Words for the journey's enums, and dates as staff read them. Dates from the API are Pacific
// calendar dates ("2026-11-03"); they are shown as dates, never converted through a time zone.

export const statusLabel: Record<StepStatus, string> = {
  ActionNeeded: 'Action needed',
  Blocked: 'Blocked',
  Complete: 'Complete',
  Skipped: "Doesn't apply",
  Upcoming: 'Upcoming',
  Waiting: 'Waiting',
};

export const statusBadge: Record<StepStatus, string> = {
  ActionNeeded: 'badge-warning',
  Blocked: 'badge-error',
  Complete: 'badge-success',
  Skipped: 'badge-ghost',
  Upcoming: 'badge-neutral badge-outline',
  Waiting: 'badge-info',
};

export const actionLabel: Record<StepAction, string> = {
  Activate: 'Start',
  Block: 'Block',
  Complete: 'Complete',
  MarkWaiting: 'Candidate done',
  Reopen: 'Reopen',
  Unblock: 'Unblock',
};

export const phaseLabel: Record<JourneyPhase, string> = {
  FirstDay: 'Cleared and first day',
  PreEmployment: 'Pre-employment',
  TalentAcquisition: 'Talent Acquisition',
};

export const roleLabel: Record<StaffRole, string> = {
  LearningAndDevelopment: 'Learning and Development',
  OnboardingSpecialist: 'Onboarding specialist',
  Recruiter: 'Recruiter',
  RecruitingAssistant: 'Recruiting assistant',
};

export const i9Label: Record<I9Method, string> = {
  InPerson: 'In person',
  NotChosen: 'Not chosen yet',
  Remote: 'Remote',
};

const parseDate = (value: string) => {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
};

/** "Thu, Nov 19, 2026" */
export const formatDate = (value: string | null | undefined) =>
  value
    ? parseDate(value).toLocaleDateString('en-US', {
        day: 'numeric',
        month: 'short',
        weekday: 'short',
        year: 'numeric',
      })
    : '—';

/** "Nov 19" for tables; the year only when it is not this year. */
export const formatShortDate = (value: string | null | undefined) => {
  if (!value) {
    return '—';
  }
  const date = parseDate(value);
  return date.toLocaleDateString('en-US', {
    day: 'numeric',
    month: 'short',
    ...(date.getFullYear() === new Date().getFullYear()
      ? {}
      : { year: 'numeric' }),
  });
};

/** "15:56:00" → "3:56 p.m." */
export const formatTime = (value: string) => {
  const [hours, minutes] = value.split(':').map(Number);
  const suffix = hours < 12 ? 'a.m.' : 'p.m.';
  const hour12 = hours % 12 === 0 ? 12 : hours % 12;
  return `${hour12}:${String(minutes).padStart(2, '0')} ${suffix}`;
};

export const formatDue = (date: string | null, time: string | null) =>
  date ? `${formatDate(date)}${time ? `, ${formatTime(time)}` : ''}` : '—';

export const formatTimestamp = (value: string) =>
  new Date(value).toLocaleString('en-US', {
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    month: 'short',
    year: 'numeric',
  });

/** The server's explanation of a refused change, or a generic one. */
export const errorMessage = (error: unknown) => {
  if (
    error instanceof HttpError &&
    typeof error.body === 'object' &&
    error.body
  ) {
    const body = error.body as { detail?: string; title?: string };
    return body.detail ?? body.title ?? `Request failed (${error.status}).`;
  }
  return 'Something went wrong. Try again.';
};

/** "Message 4" for the audit's messages; the app's own messages (audit "portal") have no number. */
export const messageLabel = (auditReference: string) =>
  auditReference === 'portal' ? 'Sign-in link' : `Message ${auditReference}`;
