/** For someone signed in who is not on the staff whitelist. */
export function AccessUnavailable() {
  return (
    <main className="min-h-screen flex items-center justify-center px-4 py-12">
      <section className="max-w-lg text-center">
        <h1 className="text-3xl font-bold text-gray-900">Access unavailable</h1>
        <p className="mt-4 text-gray-600">
          You are signed in, but your account is not authorized to use this
          application.
        </p>
        <a className="btn btn-primary mt-6" href="/login">
          Sign in with a different account
        </a>
      </section>
    </main>
  );
}
