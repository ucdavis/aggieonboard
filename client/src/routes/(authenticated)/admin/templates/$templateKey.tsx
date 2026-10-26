import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router';
import { useId, useState } from 'react';
import { HttpError } from '@/lib/api.ts';
import {
  conditionLabel,
  errorMessage,
  formatTimestamp,
  messageLabel,
} from '@/lib/journey.ts';
import {
  type CandidateCondition,
  type TemplateDetail,
  type TemplateSection,
  templateQueryOptions,
  usePreviewTemplate,
  useSaveTemplate,
} from '@/queries/admin.ts';
import { EmailFrame } from '@/shared/staff/Dialog.tsx';

type TemplateSearch = { saved?: number; version?: number };

export const Route = createFileRoute(
  '/(authenticated)/admin/templates/$templateKey'
)({
  component: TemplatePage,
  validateSearch: (search: Record<string, unknown>): TemplateSearch => ({
    saved: Number(search.saved) > 0 ? Number(search.saved) : undefined,
    version: Number(search.version) > 0 ? Number(search.version) : undefined,
  }),
});

const conditions = Object.keys(conditionLabel) as CandidateCondition[];

function TemplatePage() {
  const { templateKey } = Route.useParams();
  const { saved, version } = Route.useSearch();
  const { data, error } = useQuery(templateQueryOptions(templateKey, version));

  return (
    <div className="space-y-6">
      <Link className="link link-hover text-sm" to="/admin/templates">
        ← Message templates
      </Link>
      {error ? (
        <div className="alert alert-error" role="alert">
          {error instanceof HttpError && error.status === 404
            ? 'There is no such message or version.'
            : errorMessage(error)}
        </div>
      ) : !data ? (
        <span className="loading loading-spinner" />
      ) : (
        // Keyed by version so loading another one starts the editor afresh.
        <Editor
          key={`${data.key}-${data.version}`}
          saved={saved}
          template={data}
        />
      )}
    </div>
  );
}

type Draft = { id: number } & TemplateSection;

/**
 * Edits the message's wording. Saving makes the next version, which sends from then on; the
 * server checks every merge field and refuses an edit started from an older version.
 */
function Editor({
  saved,
  template,
}: {
  saved?: number;
  template: TemplateDetail;
}) {
  const navigate = useNavigate();
  const [subject, setSubject] = useState(template.subject);
  const [sections, setSections] = useState<Draft[]>(() =>
    template.sections.map((s, i) => ({ ...s, id: i }))
  );
  const [nextId, setNextId] = useState(template.sections.length);
  const save = useSaveTemplate(template.key);
  const errors =
    save.error instanceof HttpError && save.error.status === 400
      ? ((save.error.body as { errors?: Record<string, string[]> }).errors ??
        {})
      : {};
  const vendor = template.sender === 'External';
  const older = template.version !== template.latestVersion;
  const subjectId = useId();

  const update = (id: number, change: Partial<TemplateSection>) =>
    setSections((all) =>
      all.map((s) => (s.id === id ? { ...s, ...change } : s))
    );
  const move = (index: number, by: number) =>
    setSections((all) => {
      const next = [...all];
      const [item] = next.splice(index, 1);
      next.splice(index + by, 0, item);
      return next;
    });

  const content = {
    sections: sections.map(({ body, condition }) => ({ body, condition })),
    subject,
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold">
          {messageLabel(template.auditReference)}: {template.subject}
        </h1>
        <p className="text-base-content/70">
          {template.key}
          {template.steps.length > 0 && <> · {template.steps.join(', ')}</>}
        </p>
      </div>

      {saved && !older && (
        <div className="alert alert-success" role="status">
          Version {saved} is saved. It sends from now on.
        </div>
      )}
      {vendor && (
        <div className="alert alert-info text-sm" role="status">
          A vendor sends this message, not AggieOnboard. This is the wording
          proposed to them, so it can&apos;t use merge fields.
        </div>
      )}
      {older && (
        <div className="alert alert-warning text-sm" role="status">
          You&apos;re looking at version {template.version}. Version{' '}
          {template.latestVersion} is the one that sends. Saving this makes it
          version {template.latestVersion + 1}.
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-[1fr_18rem]">
        <form
          className="space-y-4"
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate(
              { ...content, basedOnVersion: template.latestVersion },
              {
                onSuccess: (detail) =>
                  navigate({
                    params: { templateKey: template.key },
                    search: { saved: detail.version },
                    to: '/admin/templates/$templateKey',
                  }),
              }
            );
          }}
        >
          <div className="form-control">
            <label className="label font-medium" htmlFor={subjectId}>
              Subject
            </label>
            <input
              aria-invalid={errors.subject ? true : undefined}
              className={`input input-bordered w-full ${errors.subject ? 'input-error' : ''}`}
              id={subjectId}
              onChange={(e) => setSubject(e.target.value)}
              value={subject}
            />
            <FieldErrors messages={errors.subject} />
          </div>

          {sections.map((section, i) => (
            <SectionEditor
              canMoveDown={i < sections.length - 1}
              canMoveUp={i > 0}
              errors={[
                ...(errors[`sections[${i}].body`] ?? []),
                ...(errors[`sections[${i}].condition`] ?? []),
              ]}
              index={i}
              key={section.id}
              onChange={(change) => update(section.id, change)}
              onMove={(by) => move(i, by)}
              onRemove={() =>
                setSections((all) => all.filter((s) => s.id !== section.id))
              }
              section={section}
            />
          ))}
          <FieldErrors messages={errors.sections} />
          <button
            className="btn btn-outline btn-sm"
            onClick={() => {
              setSections((all) => [
                ...all,
                { body: '', condition: 'Always', id: nextId },
              ]);
              setNextId((n) => n + 1);
            }}
            type="button"
          >
            Add section
          </button>

          {save.error &&
            !(save.error instanceof HttpError && save.error.status === 400) && (
              <div className="alert alert-error" role="alert">
                {errorMessage(save.error)}
              </div>
            )}
          {save.error instanceof HttpError && save.error.status === 400 && (
            <div className="alert alert-error" role="alert">
              Some of the wording needs fixing. See the highlighted fields.
            </div>
          )}
          <button
            className="btn btn-primary"
            disabled={save.isPending}
            type="submit"
          >
            {save.isPending
              ? 'Saving…'
              : `Save as version ${template.latestVersion + 1}`}
          </button>
        </form>

        <aside className="space-y-4">
          <Versions template={template} />
          {!vendor && <Fields template={template} />}
        </aside>
      </div>

      {!vendor && <Preview content={content} template={template} />}
    </div>
  );
}

function SectionEditor({
  canMoveDown,
  canMoveUp,
  errors,
  index,
  onChange,
  onMove,
  onRemove,
  section,
}: {
  canMoveDown: boolean;
  canMoveUp: boolean;
  errors: string[];
  index: number;
  onChange: (change: Partial<TemplateSection>) => void;
  onMove: (by: number) => void;
  onRemove: () => void;
  section: Draft;
}) {
  const bodyId = useId();
  const audienceId = useId();
  return (
    <fieldset className="space-y-2 rounded-box border border-base-300 bg-base-100 p-4">
      <legend className="px-1 font-medium">Section {index + 1}</legend>
      <div className="flex flex-wrap items-end gap-2">
        <div className="form-control">
          <label className="label py-1 text-sm" htmlFor={audienceId}>
            Who sees it
          </label>
          <select
            className="select select-bordered select-sm"
            id={audienceId}
            onChange={(e) =>
              onChange({ condition: e.target.value as CandidateCondition })
            }
            value={section.condition}
          >
            {conditions.map((c) => (
              <option key={c} value={c}>
                {conditionLabel[c]}
              </option>
            ))}
          </select>
        </div>
        <div className="ml-auto flex gap-1">
          <button
            aria-label={`Move section ${index + 1} up`}
            className="btn btn-ghost btn-sm"
            disabled={!canMoveUp}
            onClick={() => onMove(-1)}
            type="button"
          >
            ↑
          </button>
          <button
            aria-label={`Move section ${index + 1} down`}
            className="btn btn-ghost btn-sm"
            disabled={!canMoveDown}
            onClick={() => onMove(1)}
            type="button"
          >
            ↓
          </button>
          <button
            className="btn btn-ghost btn-sm text-error"
            onClick={onRemove}
            type="button"
          >
            Remove
          </button>
        </div>
      </div>
      <label className="sr-only" htmlFor={bodyId}>
        Section {index + 1} wording
      </label>
      <textarea
        aria-invalid={errors.length ? true : undefined}
        className={`textarea textarea-bordered w-full font-mono text-sm ${errors.length ? 'textarea-error' : ''}`}
        id={bodyId}
        onChange={(e) => onChange({ body: e.target.value })}
        rows={Math.min(24, Math.max(4, section.body.split('\n').length + 1))}
        value={section.body}
      />
      <FieldErrors messages={errors} />
    </fieldset>
  );
}

function Versions({ template }: { template: TemplateDetail }) {
  return (
    <section className="rounded-box bg-base-100 p-4 text-sm shadow-sm">
      <h2 className="mb-2 font-semibold">Versions</h2>
      <ul className="space-y-1">
        {template.versions.map((v) => (
          <li key={v.version}>
            {v.version === template.version ? (
              <span className="font-medium">Version {v.version} (open)</span>
            ) : (
              <Link
                className="link link-primary"
                params={{ templateKey: template.key }}
                search={
                  v.version === template.latestVersion
                    ? {}
                    : { version: v.version }
                }
                to="/admin/templates/$templateKey"
              >
                Version {v.version}
              </Link>
            )}
            {v.version === template.latestVersion && ' · sends now'}
            <div className="text-xs text-base-content/60">
              {formatTimestamp(v.createdAt)} · {v.createdBy}
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

function Fields({ template }: { template: TemplateDetail }) {
  return (
    <details className="rounded-box bg-base-100 p-4 text-sm shadow-sm">
      <summary className="cursor-pointer font-semibold">Merge fields</summary>
      <p className="mt-2 text-base-content/70">
        Write a field as {'{{FieldName}}'}. Sending is blocked while one has no
        value for the candidate.
      </p>
      <dl className="mt-2 space-y-2">
        {template.fields.map((f) => (
          <div key={f.name}>
            <dt className="font-mono text-xs">{`{{${f.name}}}`}</dt>
            <dd className="text-base-content/70">{f.description}</dd>
          </div>
        ))}
      </dl>
    </details>
  );
}

/** The wording in the editor, saved or not, filled in for a candidate. */
function Preview({
  content,
  template,
}: {
  content: { sections: TemplateSection[]; subject: string };
  template: TemplateDetail;
}) {
  const preview = usePreviewTemplate(template.key);
  const [candidateId, setCandidateId] = useState(
    template.previewCandidates[0]?.id ?? 0
  );
  const selectId = useId();
  const p = preview.data;

  return (
    <section
      aria-labelledby={`${selectId}-heading`}
      className="space-y-3 rounded-box bg-base-100 p-4 shadow-sm"
    >
      <h2 className="text-lg font-semibold" id={`${selectId}-heading`}>
        Preview
      </h2>
      <div className="flex flex-wrap items-end gap-2">
        <div className="form-control">
          <label className="label py-1 text-sm" htmlFor={selectId}>
            As sent to
          </label>
          <select
            className="select select-bordered select-sm"
            id={selectId}
            onChange={(e) => setCandidateId(Number(e.target.value))}
            value={candidateId}
          >
            {template.previewCandidates.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>
        <button
          className="btn btn-sm"
          disabled={!candidateId || preview.isPending}
          onClick={() => preview.mutate({ ...content, candidateId })}
          type="button"
        >
          Preview the wording above
        </button>
      </div>
      {template.key.startsWith('appointment-') && (
        <p className="text-sm text-base-content/70">
          Appointment emails are previewed with an example appointment.
        </p>
      )}
      {preview.error && (
        <div className="alert alert-error text-sm" role="alert">
          {errorMessage(preview.error)}
        </div>
      )}
      {p && (
        <div className="space-y-3">
          <p className="text-sm">
            <span className="text-base-content/60">Subject:</span> {p.subject}
          </p>
          {p.missing.length > 0 && (
            <div className="alert alert-warning block text-sm" role="status">
              <p className="font-semibold">
                For this candidate, these have no value yet:
              </p>
              <ul className="mt-2 list-inside list-disc space-y-1">
                {p.missing.map((m) => (
                  <li key={m.name}>
                    <span className="font-medium">{m.description}</span> {m.fix}
                  </li>
                ))}
              </ul>
            </div>
          )}
          <EmailFrame html={p.html} title={`Preview of ${p.subject}`} />
        </div>
      )}
    </section>
  );
}

function FieldErrors({ messages }: { messages?: string[] }) {
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
