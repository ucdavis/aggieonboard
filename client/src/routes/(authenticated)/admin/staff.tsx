import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useId, useState } from 'react';
import { HttpError } from '@/lib/api.ts';
import { errorMessage, roleLabel } from '@/lib/journey.ts';
import {
  type StaffAdminView,
  staffListQueryOptions,
  type TeamView,
  useSaveStaff,
} from '@/queries/admin.ts';
import type { StaffRole } from '@/queries/staff.ts';
import { useUser } from '@/shared/auth/UserContext.tsx';
import { Dialog } from '@/shared/staff/Dialog.tsx';

export const Route = createFileRoute('/(authenticated)/admin/staff')({
  component: StaffPage,
});

const roles = Object.keys(roleLabel) as StaffRole[];

/**
 * The sign-in whitelist. A change applies on the person's next request: their role, admin access,
 * schedule teams, or whether they can sign in at all.
 */
function StaffPage() {
  const { data, error } = useQuery(staffListQueryOptions());
  const [editing, setEditing] = useState<StaffAdminView | 'new' | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <h1 className="text-3xl font-bold">Staff</h1>
          <p className="text-base-content/70">
            Who can sign in, and what they see. Team members get only their
            teams&apos; schedules.
          </p>
        </div>
        <button
          className="btn btn-primary"
          onClick={() => setEditing('new')}
          type="button"
        >
          Add staff member
        </button>
      </div>
      {notice && (
        <div className="alert alert-info" role="status">
          <span>{notice}</span>
          <button
            className="btn btn-ghost btn-sm"
            onClick={() => setNotice(null)}
            type="button"
          >
            Dismiss
          </button>
        </div>
      )}
      {error ? (
        <div className="alert alert-error" role="alert">
          {errorMessage(error)}
        </div>
      ) : !data ? (
        <span className="loading loading-spinner" />
      ) : (
        <>
          <div className="overflow-x-auto rounded-box bg-base-100 shadow-sm">
            <table className="table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Role</th>
                  <th>Schedule teams</th>
                  <th>Candidates</th>
                  <th>
                    <span className="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.staff.map((s) => (
                  <tr className={s.isActive ? '' : 'opacity-60'} key={s.id}>
                    <td>
                      <div className="font-medium">{s.name}</div>
                      <div className="text-xs text-base-content/60">
                        {s.email}
                        {s.phone && <> · {s.phone}</>}
                      </div>
                    </td>
                    <td className="text-sm">
                      {roleLabel[s.role]}
                      {s.isAdmin && (
                        <span className="badge badge-primary badge-sm ml-2">
                          Admin
                        </span>
                      )}
                      {!s.isActive && (
                        <span className="badge badge-ghost badge-sm ml-2">
                          Inactive
                        </span>
                      )}
                    </td>
                    <td className="text-sm">
                      {teamNames(data.teams, s.teamIds) || '—'}
                    </td>
                    <td className="text-sm">{s.assignedCandidates || '—'}</td>
                    <td>
                      <button
                        aria-label={`Edit ${s.name}`}
                        className="btn btn-ghost btn-sm"
                        onClick={() => setEditing(s)}
                        type="button"
                      >
                        Edit
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {editing && (
            <StaffDialog
              member={editing === 'new' ? null : editing}
              onClose={() => setEditing(null)}
              onSaved={(message) => {
                setEditing(null);
                setNotice(message);
              }}
              teams={data.teams}
            />
          )}
        </>
      )}
    </div>
  );
}

const teamNames = (teams: TeamView[], ids: number[]) =>
  teams
    .filter((t) => ids.includes(t.id))
    .map((t) => t.name)
    .join(', ');

function StaffDialog({
  member,
  onClose,
  onSaved,
  teams,
}: {
  member: StaffAdminView | null;
  onClose: () => void;
  onSaved: (notice: string) => void;
  teams: TeamView[];
}) {
  const user = useUser();
  const self = member?.email.toLowerCase() === user.email?.toLowerCase();
  const [name, setName] = useState(member?.name ?? '');
  const [email, setEmail] = useState(member?.email ?? '');
  const [phone, setPhone] = useState(member?.phone ?? '');
  const [role, setRole] = useState<StaffRole>(member?.role ?? 'ServiceChannel');
  const [isAdmin, setIsAdmin] = useState(member?.isAdmin ?? false);
  const [isActive, setIsActive] = useState(member?.isActive ?? true);
  const [teamIds, setTeamIds] = useState<number[]>(member?.teamIds ?? []);
  const save = useSaveStaff();
  const errors =
    save.error instanceof HttpError && save.error.status === 400
      ? ((save.error.body as { errors?: Record<string, string[]> }).errors ??
        {})
      : null;
  const ids = { email: useId(), name: useId(), phone: useId(), role: useId() };

  return (
    <Dialog
      onClose={onClose}
      title={member ? `Edit ${member.name}` : 'Add staff member'}
    >
      <form
        className="space-y-4"
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate(
            {
              email,
              id: member?.id ?? null,
              isActive,
              isAdmin,
              name,
              phone,
              role,
              teamIds,
            },
            {
              onSuccess: (result) =>
                onSaved(result.notice ?? `${result.member.name} is saved.`),
            }
          );
        }}
      >
        <div className="grid gap-4 sm:grid-cols-2">
          <Field error={errors?.name} id={ids.name} label="Name">
            <input
              className="input input-bordered w-full"
              id={ids.name}
              onChange={(e) => setName(e.target.value)}
              value={name}
            />
          </Field>
          <Field
            error={errors?.email}
            hint="Their UC Davis sign-in address."
            id={ids.email}
            label="Email"
          >
            <input
              className="input input-bordered w-full"
              id={ids.email}
              onChange={(e) => setEmail(e.target.value)}
              type="email"
              value={email}
            />
          </Field>
          <Field
            error={errors?.phone}
            hint="Optional. Messages offer it with their email."
            id={ids.phone}
            label="Phone"
          >
            <input
              className="input input-bordered w-full"
              id={ids.phone}
              onChange={(e) => setPhone(e.target.value)}
              value={phone}
            />
          </Field>
          <Field error={errors?.role} id={ids.role} label="Role">
            <select
              className="select select-bordered w-full"
              id={ids.role}
              onChange={(e) => setRole(e.target.value as StaffRole)}
              value={role}
            >
              {roles.map((r) => (
                <option key={r} value={r}>
                  {roleLabel[r]}
                </option>
              ))}
            </select>
          </Field>
        </div>

        <fieldset className="space-y-1">
          <legend className="mb-1 font-medium">Access</legend>
          <label className="label cursor-pointer justify-start gap-2">
            <input
              checked={isActive}
              className="checkbox checkbox-sm checkbox-primary"
              disabled={self}
              onChange={(e) => setIsActive(e.target.checked)}
              type="checkbox"
            />
            <span className="label-text">Can sign in</span>
          </label>
          <label className="label cursor-pointer justify-start gap-2">
            <input
              checked={isAdmin}
              className="checkbox checkbox-sm checkbox-primary"
              disabled={self}
              onChange={(e) => setIsAdmin(e.target.checked)}
              type="checkbox"
            />
            <span className="label-text">
              Admin: templates, settings, staff and every team&apos;s schedule
            </span>
          </label>
          {self && (
            <p className="text-sm text-base-content/70">
              You can&apos;t remove your own access. Ask another admin.
            </p>
          )}
        </fieldset>

        <fieldset className="space-y-1">
          <legend className="mb-1 font-medium">Schedule teams</legend>
          {teams.map((t) => (
            <label
              className="label cursor-pointer justify-start gap-2"
              key={t.id}
            >
              <input
                checked={teamIds.includes(t.id)}
                className="checkbox checkbox-sm checkbox-primary"
                onChange={(e) =>
                  setTeamIds((all) =>
                    e.target.checked
                      ? [...all, t.id]
                      : all.filter((id) => id !== t.id)
                  )
                }
                type="checkbox"
              />
              <span className="label-text">
                {t.name}
                {t.locations.length > 0 && (
                  <span className="text-base-content/60">
                    {' '}
                    · {t.locations.join('; ')}
                  </span>
                )}
              </span>
            </label>
          ))}
          {errors?.teamIds && <Errors messages={errors.teamIds} />}
        </fieldset>

        {save.error && !errors && (
          <div className="alert alert-error text-sm" role="alert">
            {errorMessage(save.error)}
          </div>
        )}
        <div className="flex justify-end gap-2">
          <button className="btn btn-ghost" onClick={onClose} type="button">
            Cancel
          </button>
          <button
            className="btn btn-primary"
            disabled={save.isPending}
            type="submit"
          >
            {member ? 'Save' : 'Add'}
          </button>
        </div>
      </form>
    </Dialog>
  );
}

function Field({
  children,
  error,
  hint,
  id,
  label,
}: {
  children: React.ReactNode;
  error?: string[];
  hint?: string;
  id: string;
  label: string;
}) {
  return (
    <div className="form-control">
      <label className="label py-1 text-sm font-medium" htmlFor={id}>
        {label}
      </label>
      {children}
      {hint && <p className="mt-1 text-xs text-base-content/60">{hint}</p>}
      <Errors messages={error} />
    </div>
  );
}

function Errors({ messages }: { messages?: string[] }) {
  if (!messages?.length) {
    return null;
  }
  return (
    <ul className="mt-1 space-y-1 text-sm text-error">
      {messages.map((m) => (
        <li key={m}>{m}</li>
      ))}
    </ul>
  );
}
