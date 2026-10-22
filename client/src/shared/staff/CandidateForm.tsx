import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { z } from 'zod';
import { HttpError } from '@/lib/api.ts';
import { errorMessage, i9Label } from '@/lib/journey.ts';
import {
  type CandidateDetail,
  type CandidateInput,
  type I9Method,
  type QualificationKind,
  type StaffRole,
  staffMembersQueryOptions,
} from '@/queries/staff.ts';
import { useAppForm } from '@/shared/forms/formContext.tsx';

type FormValues = {
  anticipatedStartDate: string;
  department: string;
  email: string;
  employeeId: string;
  firstName: string;
  hiringManagerContact: string;
  i9Method: I9Method;
  lastName: string;
  onboardingSpecialistId: string;
  positionTitle: string;
  qualifications: {
    documentToSubmit: string;
    kind: QualificationKind;
    name: string;
  }[];
  recruiterId: string;
  recruitingAssistantId: string;
  requirements: CandidateDetail['requirements'];
};

const required = (label: string) =>
  z.string().trim().min(1, `${label} is required.`);

const schema = z.object({
  anticipatedStartDate: z.string(),
  department: required('Department'),
  email: z.email('Enter an email address like name@example.com.'),
  employeeId: z.string().max(20, 'Employee ID must be 20 characters or fewer.'),
  firstName: required('First name'),
  hiringManagerContact: z.string().max(500),
  i9Method: z.enum(['NotChosen', 'InPerson', 'Remote']),
  lastName: required('Last name'),
  onboardingSpecialistId: z.string(),
  positionTitle: required('Position title'),
  qualifications: z.array(
    z.object({
      documentToSubmit: z.string(),
      kind: z.enum(['License', 'Certification', 'Degree']),
      name: required('Name'),
    })
  ),
  recruiterId: z.string(),
  recruitingAssistantId: z.string(),
  requirements: z.object({
    dmvPullNotice: z.boolean(),
    drugScreen: z.boolean(),
    fingerprinting: z.boolean(),
    parking: z.boolean(),
    positionDescription: z.boolean(),
  }),
}) satisfies z.ZodType<FormValues>;

const requirementOptions: {
  key: keyof FormValues['requirements'];
  label: string;
}[] = [
  { key: 'drugScreen', label: 'Drug screen' },
  { key: 'dmvPullNotice', label: 'DMV Employer Pull Notice' },
  { key: 'fingerprinting', label: 'Fingerprinting (Live Scan)' },
  { key: 'positionDescription', label: 'Position description' },
  { key: 'parking', label: 'Eligible for complimentary parking' },
];

const toValues = (c?: CandidateDetail): FormValues => ({
  anticipatedStartDate: c?.anticipatedStartDate ?? '',
  department: c?.department ?? '',
  email: c?.email ?? '',
  employeeId: c?.employeeId ?? '',
  firstName: c?.firstName ?? '',
  hiringManagerContact: c?.hiringManagerContact ?? '',
  i9Method: c?.i9Method ?? 'NotChosen',
  lastName: c?.lastName ?? '',
  onboardingSpecialistId: c?.onboardingSpecialist
    ? String(c.onboardingSpecialist.id)
    : '',
  positionTitle: c?.positionTitle ?? '',
  qualifications:
    c?.qualifications.map((q) => ({
      documentToSubmit: q.documentToSubmit ?? '',
      kind: q.kind,
      name: q.name,
    })) ?? [],
  recruiterId: c?.recruiter ? String(c.recruiter.id) : '',
  recruitingAssistantId: c?.recruitingAssistant
    ? String(c.recruitingAssistant.id)
    : '',
  requirements: c?.requirements ?? {
    dmvPullNotice: false,
    drugScreen: false,
    fingerprinting: false,
    parking: false,
    positionDescription: false,
  },
});

const blank = (value: string) => (value.trim() ? value.trim() : null);
const idOrNull = (value: string) => (value ? Number(value) : null);

const toInput = (v: FormValues): CandidateInput => ({
  anticipatedStartDate: v.anticipatedStartDate || null,
  department: v.department.trim(),
  email: v.email.trim(),
  employeeId: blank(v.employeeId),
  firstName: v.firstName.trim(),
  hiringManagerContact: blank(v.hiringManagerContact),
  i9Method: v.i9Method,
  lastName: v.lastName.trim(),
  onboardingSpecialistId: idOrNull(v.onboardingSpecialistId),
  positionTitle: v.positionTitle.trim(),
  qualifications: v.qualifications.map((q) => ({
    documentToSubmit: blank(q.documentToSubmit),
    kind: q.kind,
    name: q.name.trim(),
  })),
  recruiterId: idOrNull(v.recruiterId),
  recruitingAssistantId: idOrNull(v.recruitingAssistantId),
  requirements: v.requirements,
});

/**
 * Create or edit a candidate. The server validates too; its field errors land on the same fields,
 * qualification rows included ("qualifications[0].name").
 */
export function CandidateForm({
  candidate,
  onSave,
  submitLabel,
}: {
  candidate?: CandidateDetail;
  onSave: (input: CandidateInput) => Promise<unknown>;
  submitLabel: string;
}) {
  const staff = useQuery(staffMembersQueryOptions());
  const [failure, setFailure] = useState<string | null>(null);

  const form = useAppForm({
    defaultValues: toValues(candidate),
    onSubmit: async ({ formApi, value }) => {
      setFailure(null);
      try {
        await onSave(toInput(value));
      } catch (error) {
        const errors =
          error instanceof HttpError && error.status === 400
            ? (error.body as { errors?: Record<string, string[]> }).errors
            : undefined;
        if (!errors) {
          setFailure(errorMessage(error));
          return;
        }
        for (const [field, messages] of Object.entries(errors)) {
          formApi.setFieldMeta(field as keyof FormValues, (meta) => ({
            ...meta,
            errorMap: {
              ...meta.errorMap,
              onServer: messages.map((message) => ({ message })),
            },
            isTouched: true,
          }));
        }
        setFailure('Some details need fixing. See the highlighted fields.');
      }
    },
    validators: { onSubmit: schema },
  });

  const optionsFor = (role: StaffRole) =>
    (staff.data ?? [])
      .filter((s) => s.role === role)
      .map((s) => ({ label: s.name, value: String(s.id) }));

  return (
    <form
      className="space-y-8"
      noValidate
      onSubmit={(e) => {
        e.preventDefault();
        void form.handleSubmit();
      }}
    >
      <form.AppForm>
        <fieldset className="grid gap-4 sm:grid-cols-2">
          <legend className="mb-2 text-lg font-semibold">Candidate</legend>
          <form.AppField name="firstName">
            {(f) => <f.TextField label="First name" placeholder="" />}
          </form.AppField>
          <form.AppField name="lastName">
            {(f) => <f.TextField label="Last name" placeholder="" />}
          </form.AppField>
          <form.AppField name="email">
            {(f) => (
              <f.TextField
                label="Email"
                placeholder="name@example.com"
                type="email"
              />
            )}
          </form.AppField>
          <form.AppField name="positionTitle">
            {(f) => <f.TextField label="Position title" placeholder="" />}
          </form.AppField>
          <form.AppField name="department">
            {(f) => <f.TextField label="Department" placeholder="" />}
          </form.AppField>
          <form.AppField name="anticipatedStartDate">
            {(f) => <f.TextField label="Anticipated start date" type="date" />}
          </form.AppField>
          <form.AppField name="hiringManagerContact">
            {(f) => (
              <f.TextField
                label="Hiring manager contact"
                placeholder="Name and email or phone"
              />
            )}
          </form.AppField>
          <form.AppField name="employeeId">
            {(f) => (
              <f.TextField label="Employee ID" placeholder="Once in UCPath" />
            )}
          </form.AppField>
        </fieldset>

        <fieldset className="grid gap-4 sm:grid-cols-3">
          <legend className="mb-2 text-lg font-semibold">Team</legend>
          <form.AppField name="recruitingAssistantId">
            {(f) => (
              <f.SelectField
                emptyLabel="Unassigned"
                label="Recruiting assistant"
                options={optionsFor('RecruitingAssistant')}
              />
            )}
          </form.AppField>
          <form.AppField name="recruiterId">
            {(f) => (
              <f.SelectField
                emptyLabel="Unassigned"
                label="Recruiter"
                options={optionsFor('Recruiter')}
              />
            )}
          </form.AppField>
          <form.AppField name="onboardingSpecialistId">
            {(f) => (
              <f.SelectField
                emptyLabel="Unassigned"
                label="Onboarding specialist"
                options={optionsFor('OnboardingSpecialist')}
              />
            )}
          </form.AppField>
        </fieldset>

        <fieldset className="space-y-3">
          <legend className="mb-2 text-lg font-semibold">
            The position requires
          </legend>
          <p className="text-sm text-base-content/70">
            Each adds steps, or sections of message 12. Turning one off skips
            its step unless it is already complete.
          </p>
          <div className="grid gap-2 sm:grid-cols-2">
            {requirementOptions.map(({ key, label }) => (
              <form.Field key={key} name={`requirements.${key}`}>
                {(field) => (
                  <label className="label cursor-pointer justify-start gap-3">
                    <input
                      checked={field.state.value}
                      className="checkbox checkbox-primary checkbox-sm"
                      onChange={(e) => field.handleChange(e.target.checked)}
                      type="checkbox"
                    />
                    <span className="label-text">{label}</span>
                  </label>
                )}
              </form.Field>
            ))}
          </div>
          <form.AppField name="i9Method">
            {(f) => (
              <div className="max-w-xs">
                <f.SelectField
                  label="I-9 verification"
                  options={(Object.keys(i9Label) as I9Method[]).map((m) => ({
                    label: i9Label[m],
                    value: m,
                  }))}
                />
              </div>
            )}
          </form.AppField>
        </fieldset>

        <form.Field mode="array" name="qualifications">
          {(list) => (
            <fieldset className="space-y-3">
              <legend className="mb-2 text-lg font-semibold">
                Qualifications to verify
              </legend>
              <p className="text-sm text-base-content/70">
                Listed in message 4. With none, the qualification step is
                skipped.
              </p>
              {list.state.value.map((_, i) => (
                <div
                  className="grid items-end gap-3 rounded-box border border-base-300 p-3 sm:grid-cols-[10rem_1fr_1fr_auto]"
                  key={i}
                >
                  <form.AppField name={`qualifications[${i}].kind`}>
                    {(f) => (
                      <f.SelectField
                        label="Kind"
                        options={[
                          { label: 'License', value: 'License' },
                          { label: 'Certification', value: 'Certification' },
                          { label: 'Degree', value: 'Degree' },
                        ]}
                      />
                    )}
                  </form.AppField>
                  <form.AppField name={`qualifications[${i}].name`}>
                    {(f) => (
                      <f.TextField
                        label="Name"
                        placeholder="Registered Nurse (California)"
                      />
                    )}
                  </form.AppField>
                  <form.AppField name={`qualifications[${i}].documentToSubmit`}>
                    {(f) => (
                      <f.TextField
                        label="Document to send"
                        placeholder="Degrees default to an official transcript"
                      />
                    )}
                  </form.AppField>
                  <button
                    className="btn btn-ghost btn-sm"
                    onClick={() => list.removeValue(i)}
                    type="button"
                  >
                    Remove
                  </button>
                </div>
              ))}
              <button
                className="btn btn-outline btn-sm"
                onClick={() =>
                  list.pushValue({
                    documentToSubmit: '',
                    kind: 'License',
                    name: '',
                  })
                }
                type="button"
              >
                Add qualification
              </button>
            </fieldset>
          )}
        </form.Field>

        {failure && (
          <div className="alert alert-error" role="alert">
            {failure}
          </div>
        )}
        <div className="max-w-xs">
          <form.SubscribeButton label={submitLabel} />
        </div>
      </form.AppForm>
    </form>
  );
}
