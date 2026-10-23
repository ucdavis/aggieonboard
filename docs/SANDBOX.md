# Docker sandbox

The sandbox packages the current checkout into an image and runs it with SQL Server and Mailpit. ASP.NET Core serves the compiled React frontend, API, and local login on one origin. It uses the same EF migrations, controllers, role checks, and notification renderer as the regular app.

## Start and inspect

Follow the [README quick start](../README.md#run-the-docker-sandbox) to set this checkout's `SANDBOX_PROJECT`, app port, and inbox port before running any command below. Use the same values in each new terminal and whenever switching back to this worktree. Run commands from the repository root. This Compose file is standalone; do not combine it with the regular development Compose file.

```bash
docker compose -p "$SANDBOX_PROJECT" -f .devcontainer/docker-compose.sandbox.yml ps
docker compose -p "$SANDBOX_PROJECT" -f .devcontainer/docker-compose.sandbox.yml logs --tail=100 app
```

The app health check at `/health` verifies SQL connectivity after migrations and seeding finish. Compose waits for SQL and Mailpit health before starting the app.

Try these flows:

1. Sign in as Sample User, a whitelisted onboarding specialist and admin. The pipeline lists eight fictional candidates; Taylor Nguyen is overdue. **My candidates** shows Morgan Chen and Riley Johnson.
2. Open a candidate. Start, complete, block or reopen steps; change a due date; add a staff note. Each change appears under History.
3. Use **New candidate**, or **Edit details** on a candidate, and toggle a requirement such as Drug screen to watch its step appear or be skipped.
4. On a step, choose **Preview message** to see its email filled in for that candidate, with anything missing and how to fix it. **Send** delivers it to the local inbox linked in the quick start and starts the step. Mailpit captures all recipients locally and has no relay configured.
5. On Jordan Lee's Qualification verification, choose **Candidate done**: message 5 goes out automatically. Sent messages are listed under **Messages sent** and open exactly as they went out.
6. Visit `/login` and switch to Basic User. They are signed in but not on the staff whitelist, so the app shows **Access unavailable** and `/api/staff/*` returns `403`.
7. Visit `/login` and sign out. `/api/user/me` returns `401`.

Fixtures are seeded only into an empty database. After a schema or fixture change, reset with `down --volumes` (below).

Pipeline, email, and candidate-portal flows are added here as their phases land (see [the plan](PLAN.md)).

Sample User has ID `sandbox-sample`, email `sample@example.test`, IAM ID `sandbox-10001`, and roles `User` and `SampleRole`. Basic User has ID `sandbox-basic`, email `basic@example.test`, IAM ID `sandbox-10002`, and only `User`. They are fixed claims, not database user rows. Extend `LocalAuthentication.cs` when adding application roles, and `DbInitializer.cs` when adding fixtures.

## Stop, rebuild, and reset

To stop the sandbox and preserve the database and cookie keys:

```bash
docker compose -p "$SANDBOX_PROJECT" -f .devcontainer/docker-compose.sandbox.yml down
```

Use the [README rebuild and reset commands](../README.md#run-the-docker-sandbox) to restart from current source or restore the original fixtures. See [Database configuration](../README.md#database-configuration) for the seeding policy. Mailpit's inbox is temporary and clears when its container stops.

The sandbox copies source at build time. It does not mount the checkout or hot reload. Use the regular development workflow for hot reload. Docker excludes host `.env` files, keys, dependencies, and build outputs using `.dockerignore`.

## Ports and multiple sandboxes

Only the app and inbox publish ports, both bound to `127.0.0.1`. SQL is reachable on the Compose network as `sql:1433`, with the disposable credentials in the Compose file. `SANDBOX_PROJECT` is required and must be distinct for each checkout or worktree. Use lowercase letters, digits, dashes, or underscores, starting with a letter or digit. Do not reuse another sandbox's name or the regular development project's name: the project selects the containers, network, database volume, and cookie-key volume.

For a second checkout or worktree, use another project name and unused host ports. For example, in that worktree's terminal:

```bash
export SANDBOX_PROJECT=template-feature-b
export SANDBOX_PORT=5281
export SANDBOX_MAIL_PORT=8026
docker compose -p "$SANDBOX_PROJECT" -f .devcontainer/docker-compose.sandbox.yml up --build --wait
```

Use these same exports and `-p "$SANDBOX_PROJECT"` for all later commands, including inspection, shutdown, and volume resets. The second app is at `http://localhost:5281` and its inbox is at `http://localhost:8026`. Resetting one project leaves the other project's resources intact.

To manage a sandbox created with the previous default name, set `SANDBOX_PROJECT=web-app-template-sandbox` and use its original ports. Reserve that name for that existing sandbox.

Compose sets `Auth__LocalCookieSuffix` to the project name so local sign-in and antiforgery cookies stay separate across sandboxes on different localhost ports.

See [Auth Configuration](../README.md#auth-configuration) for enabling local authentication and its environment restriction. The local cookie uses its own authentication scheme and name. Login and logout are POST forms with antiforgery validation, and login accepts only local return URLs. Keep the sandbox bound to loopback; anyone who can reach it can choose a fictional user.

SQL Server uses its Linux AMD64 image. On Apple Silicon, Docker Desktop needs AMD64 emulation enabled. If SQL stays unhealthy, inspect `docker compose -p "$SANDBOX_PROJECT" -f .devcontainer/docker-compose.sandbox.yml logs sql` and Docker's available memory. SQL Server needs at least 2 GB of memory, with additional room for the build and app.

## Agent use

Agents can use the host URL in the [quick start](../README.md#run-the-docker-sandbox) or join the Compose network from a browser container and visit `http://sandbox.test:8080`. The `sandbox.test` network alias avoids Chromium's automatic HTTPS upgrade for the `app` hostname. Follow the sign-in form so the browser receives a real cookie. Do not replace `/api/user/me` or other API endpoints with mocks when verifying the sandbox.

Useful commands inside the app and database containers:

```bash
docker compose -p "$SANDBOX_PROJECT" -f .devcontainer/docker-compose.sandbox.yml exec app curl --fail http://localhost:8080/health
docker compose -p "$SANDBOX_PROJECT" -f .devcontainer/docker-compose.sandbox.yml exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'SandboxOnly123!' -C -d SandboxDb -Q 'SELECT name FROM sys.tables ORDER BY name'
```
