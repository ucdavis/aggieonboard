import { NewLinkForm } from './NewLinkForm.tsx';

/** Shown when there is no portal session: a new link is the only way in. */
export function SignedOut() {
  return (
    <div className="card bg-base-100 shadow-sm">
      <div className="card-body gap-4">
        <h1 className="text-2xl font-bold">Your onboarding checklist</h1>
        <p>
          We&apos;ll email you a link to open your checklist. Use the address UC
          Davis Health has been writing to about your position.
        </p>
        <NewLinkForm />
      </div>
    </div>
  );
}
