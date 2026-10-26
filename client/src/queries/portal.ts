import {
  queryOptions,
  useMutation,
  useQueryClient,
} from '@tanstack/react-query';
import { fetchJson } from '../lib/api.ts';
import type { AppointmentStatus, BookingOptions } from './schedule.ts';
import type { I9Method, JourneyPhase } from './staff.ts';

// Shapes of /api/portal (server.core/Portal/PortalService.cs). Portal requests never redirect to
// staff sign-in on a 401: an expired session shows "email me a new link" instead.

export type PortalStepState = 'ToDo' | 'InProgress' | 'Upcoming' | 'Done';

/** An appointment as the candidate sees it. Times are Pacific wall-clock strings. */
export type PortalAppointment = {
  address: string | null;
  canChange: boolean;
  changeCutoff: string;
  end: string;
  id: number;
  locationName: string;
  start: string;
  status: AppointmentStatus;
};

export type PortalBooking = {
  appointment: PortalAppointment | null;
  canBook: boolean;
  typeKey: string;
  typeName: string;
};

/** Step 8: in person (book) or remote, until one is under way. */
export type PortalI9Choice = { canChoose: boolean; method: I9Method };

/** Step 9: message 11B's answer. */
export type PortalRepresentative = {
  email: string | null;
  name: string | null;
};

/** Step 11: message 12's ATI number, once the fingerprinting appointment has happened. */
export type PortalAtiNumber = { value: string | null };

export type PortalStep = {
  atiNumber: PortalAtiNumber | null;
  booking: PortalBooking | null;
  completedAt: string | null;
  contact: { details: string; name: string } | null;
  dueDate: string | null;
  dueTime: string | null;
  i9Choice: PortalI9Choice | null;
  isOverdue: boolean;
  key: string;
  messages: { id: number; sentAt: string; subject: string }[];
  phase: JourneyPhase;
  representative: PortalRepresentative | null;
  state: PortalStepState;
  title: string;
  watchFor: {
    fromAddress: string | null;
    sender: string;
    subject: string | null;
  } | null;
};

export type PortalView = {
  anticipatedStartDate: string | null;
  clearanceDeadline: string | null;
  department: string;
  firstName: string;
  positionTitle: string;
  steps: PortalStep[];
};

export type PortalMessage = {
  html: string;
  id: number;
  sentAt: string;
  subject: string;
  text: string;
};

const portal = { skipRedirectOn401: true } as const;

export const portalKeys = {
  all: ['portal'] as const,
  me: () => ['portal', 'me'] as const,
  message: (id: number) => ['portal', 'messages', id] as const,
};

export const portalQueryOptions = () =>
  queryOptions({
    queryFn: () => fetchJson<PortalView>('/api/portal/me', portal),
    queryKey: portalKeys.me(),
    retry: false,
  });

export const portalMessageQueryOptions = (id: number) =>
  queryOptions({
    queryFn: () =>
      fetchJson<PortalMessage>(`/api/portal/messages/${id}`, portal),
    queryKey: portalKeys.message(id),
    retry: false,
    staleTime: Infinity,
  });

export const portalBookingOptionsQueryOptions = (
  typeKey: string,
  from: string,
  to: string
) =>
  queryOptions({
    queryFn: () =>
      fetchJson<BookingOptions>(
        `/api/portal/appointments/${typeKey}/options?from=${from}&to=${to}`,
        portal
      ),
    queryKey: ['portal', 'options', typeKey, from, to] as const,
    retry: false,
    staleTime: 0,
  });

const post = (body?: unknown, method = 'POST') => ({
  ...portal,
  body: body === undefined ? undefined : JSON.stringify(body),
  method,
});

export const useRedeemLink = () =>
  useMutation({
    mutationFn: (token: string) =>
      fetchJson<void>('/api/portal/sign-in', post({ token })),
  });

export const useRequestLink = () =>
  useMutation({
    mutationFn: (email: string) =>
      fetchJson<void>('/api/portal/link', post({ email })),
  });

export const useSignOut = () =>
  useMutation({
    mutationFn: () => fetchJson<void>('/api/portal/sign-out', post()),
  });

/** Booking or answering moves the step, so the checklist reloads. */
const usePortalChange = <TVariables>(
  request: (variables: TVariables) => Promise<void>
) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: request,
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: portalKeys.me() }),
  });
};

export const useBookAppointment = () =>
  usePortalChange(
    (booking: { locationId: number; start: string; typeKey: string }) =>
      fetchJson<void>('/api/portal/appointments', post(booking))
  );

export const useCancelAppointment = () =>
  usePortalChange((id: number) =>
    fetchJson<void>(`/api/portal/appointments/${id}/cancel`, post())
  );

export const useChooseRemoteI9 = () =>
  usePortalChange<void>(() => fetchJson<void>('/api/portal/i9/remote', post()));

export const useSetRepresentative = () =>
  usePortalChange((representative: { email: string; name: string }) =>
    fetchJson<void>(
      '/api/portal/i9/representative',
      post(representative, 'PUT')
    )
  );

export const useSetAtiNumber = () =>
  usePortalChange((atiNumber: string) =>
    fetchJson<void>(
      '/api/portal/fingerprinting/ati',
      post({ atiNumber }, 'PUT')
    )
  );
