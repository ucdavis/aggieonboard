import { useId, useState } from 'react';
import { useRequestLink } from '@/queries/portal.ts';

/**
 * "Email me a new link." The answer is the same whether or not the address is on file, so the
 * page never tells anyone who is a candidate.
 */
export function NewLinkForm() {
  const request = useRequestLink();
  const [email, setEmail] = useState('');
  const id = useId();

  if (request.isSuccess) {
    return (
      <div className="alert alert-success" role="status">
        If that address is on file, we&apos;ve emailed it a new link. It can
        take a few minutes to arrive; check your spam or junk folder too.
      </div>
    );
  }

  return (
    <form
      className="space-y-3"
      onSubmit={(e) => {
        e.preventDefault();
        if (email.trim()) {
          request.mutate(email.trim());
        }
      }}
    >
      <label className="label block p-0" htmlFor={id}>
        <span className="label-text font-medium">
          The email address we&apos;ve been writing to
        </span>
      </label>
      <input
        autoComplete="email"
        className="input input-bordered w-full"
        id={id}
        inputMode="email"
        onChange={(e) => setEmail(e.target.value)}
        required
        type="email"
        value={email}
      />
      {request.isError && (
        <div className="alert alert-error" role="alert">
          That didn&apos;t go through. Wait a minute and try again.
        </div>
      )}
      <button
        className="btn btn-primary w-full sm:w-auto"
        disabled={request.isPending}
        type="submit"
      >
        {request.isPending ? 'Sending…' : 'Email me a new link'}
      </button>
    </form>
  );
}
