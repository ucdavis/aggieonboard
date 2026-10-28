# Agent instructions

## AggieOnboard: read this before the template guidance below

AggieOnboard guides UC Davis Health candidates through onboarding, from "you're a top candidate"
(reference check) to New Employee Welcome: 14 steps across Talent Acquisition, SSO, and L&D.
Staff work a candidate pipeline and send each step's approved message from the app; candidates
follow one checklist in a portal they reach by emailed magic link.

This is a **greenfield prototype** on the template, and currently the owner's own POC.
**The plan is [`docs/PLAN.md`](docs/PLAN.md)**: step catalog, domain model, phases, open questions.
Update its STATUS header as phases land. The source documents (the communications audit and the
rewritten TA/SSO messages) are in `Reference/`, which is gitignored. Do not commit them.

### The process is still moving

The audit lists ~30 unanswered questions for TA, SSO, and vendors ("48 business hours": from when?
clearance six business days before start? representative age rule? Wednesday I-9 hours?). Build so
their answers are data changes, not releases: anything the audit marks unconfirmed is a setting
with the document's value as the default. When a step's behavior depends on an unanswered question,
say so in a code comment that names the step number.

Sequencing is vertical slices, demo-able early, not JDWriter's bottom-up port. There is no POC to
reach parity with.

### Invariants

1. **No sensitive identity data.** No SSN, birth date, driver's license, address history, or uploaded
   transcripts/licenses. Those stay in the vendor systems (DocuSign, Universal, I-9 Tracker). The app
   holds name, email, position, dates, and statuses. Changing this needs an explicit decision recorded
   in `docs/PLAN.md`, not a feature request.
2. **A candidate session sees exactly one candidate.** Every portal query filters by the session's
   candidate ID, never one from the URL or body. Tests prove cross-candidate reads and writes fail.
3. **Magic-link tokens are stored hashed, single-use, and expiring.** "Send me a new link" responds
   identically whether or not the email is on file, and is rate-limited.
4. **Staff and candidate auth never mix.** Staff use Entra (the template's scheme) with roles from
   the staff whitelist; candidates use a separate cookie scheme. `/api/staff/*` and `/api/portal/*`
   are guarded by separate policies.
5. **Unconfirmed process facts are settings, never constants** (see above).
6. **No message is sent with an unfilled merge field.** Preview shows the gaps and Send is disabled.
7. **Sent messages are logged as rendered** (subject, HTML, text, template version) so "what did we
   tell them" always has an answer.
8. **Copy belongs to communications.** Message templates are seeded verbatim from the TA/SSO document
   (`server.core/Seed/Messages/*.md`) and edited in the app. Do not reword, tighten, or "improve"
   them in code — not even the drafting artifacts listed under *Copy to review* in the plan.
9. **Fictional data only.** No real candidate records in dev, test, fixtures, or the repo.

### Where things are

- Domain: `server.core/Domain/`. Steps and messages are separate (the plan explains why); a step
  names its messages by key.
- Step moves: `server.core/Journey/CandidateJourney.cs`. Seeds and the app use the same moves.
- Date maths: `server.core/Scheduling/`. Every business-day or business-hour calculation goes through
  `BusinessCalendar`, in Pacific time.
- Merge fields: the closed list in `server.core/Messaging/MergeFields.cs`.
- Staff operations: `server.core/Journey/JourneyService.cs`, one method per change, each writing an
  `AuditEvent`. The HTTP layer is `server/Controllers/Staff/` (`/api/staff/*`, role `Staff`). A rule
  violation is `JourneyRuleException` (→ 409 with a message written for staff); bad input is
  `JourneyValidationException` (→ 400 with field errors the client puts on the matching fields).
- Allowed step moves live in one table, `CandidateJourney.AllowedFrom`. The API returns each step's
  `availableActions` from it, and the UI shows exactly those buttons. Don't duplicate the rules client-side.
- Messages: `server.core/Messaging/`. `MessageService` previews and sends; `MergeFieldResolver`
  fills fields (a missing one carries a fix written for staff); `MessageRenderer` keeps the
  sections that apply and marks gaps as "[missing: …]"; `MessageMarkdown` is the dialect (raw HTML
  disabled, single line breaks kept, values escaped). The email frame is
  `server.core/Views/Messages/CandidateMessage_mjml.cshtml`, not the template's notification layout.
- Portal: `server.core/Portal/` (`PortalTokens`, `PortalService`) and `server/Controllers/Portal/`.
  Candidate sign-in is `server/Helpers/CandidateAuthentication.cs`: its own cookie scheme, never the
  default, and the `Candidate` policy accepts only it. A portal request's candidate comes only from
  the `candidate_id` claim. Links are redeemed by POST from the Continue page, never on GET (email
  scanners open links). Never log, store or display a raw token; logged email copies point at `/portal`.
- Scheduling: `server.core/Scheduling/` (`SlotEngine` is pure; `SchedulingService` books, cancels,
  records attendance and edits availability) and `server/Controllers/Schedule/` (`/api/schedule/*`,
  role `Scheduling`). Schedule times are Pacific wall-clock `DateTime`s, never converted. Booking runs
  in a serializable transaction; keep any new capacity check inside it. A team member
  (`StaffRole.TeamMember`) gets `Scheduling` but never `Staff`: no pipeline, no candidate records.
  Team-scoped endpoints answer 404, not 403, for another team's location or appointment.
- Appointment emails: every booking, change and cancellation, whoever makes it, goes through
  `MessageService.SendAppointmentAsync` after the change is saved (never inside the booking
  transaction; a failed email never undoes a booking). Booked/changed carry a REQUEST `invite.ics`,
  cancelled a CANCEL, with one UID per candidate and type (`CalendarInvite`). The day-before reminder is
  `AppointmentReminders`, run by `server/Services/AppointmentReminderJob.cs` on one instance.
  Candidates book in the portal (`/api/portal/appointments`) only for a step that is action needed or
  waiting; `SchedulingService.GetPortalBookingsAsync` says what each step offers, and the portal shows exactly that.
- The pull-along flow: `server.core/Journey/JourneyFlow.cs` opens the current stage (`StepDefinition.Stage`) after any
  change that can finish one; call `JourneyFlow.Advance` from new code that completes or adds steps. Each opened step
  gets a `TaskNotification`, emailed by `NewTaskEmails` only once the candidate has been away from the portal
  (`Candidate.LastPortalActivityAt`, set by `PortalActivityAttribute`). The portal lists only open and finished steps;
  `PortalStep.Kind` says how a task is done, and the task page shows the step's message via
  `MessageService.RenderForStepAsync` (a shared message keeps only that step's section).
- Staff by group: `JourneyService.GetWorkAsync` is a group's work list (My work); the candidate page's
  `JourneyTimeline` groups steps by `StepDefinition.Owner`. A step's owner group is who acts on it.
- Candidate answers (the replies 11A, 11B and 12 used to ask for): `server.core/Portal/PortalAnswers.cs` decides what each
  step asks for and records the I-9 choice, the remote representative and the ATI number (`/api/portal/i9/*`,
  `/api/portal/fingerprinting/ati`). Staff enter the same answers on the candidate form; both use the checks in
  `server.core/Journey/CandidateAnswers.cs`. A message's later wording is a `version: N` file next to version 1
  (`*.v2.md`); the latest version is what sends.
- Admin: `server.core/Admin/` (`TemplateAdmin`, `SettingsAdmin`, `StaffAdmin`) and `server/Controllers/Admin/`
  (`/api/admin/*`, role `Admin`). A template edit is always a new version, and the save checks are the gate for
  invariant 6, so keep new merge fields in `MergeFields` (and `AppointmentOnly` when only appointment emails have them).
  A change that moves deadlines must recompute open due dates and note each move in the candidate's history. Changes
  to shared data are `AuditEvent`s with no candidate, shown on the History tab.
- Emulation (Admin → Emulate): `server/Helpers/Emulation.cs`. An admin's staff cookie carries the emulated staff ID and
  `UserService.GetStaffMemberAsync` returns that person (`GetSignedInStaffMemberAsync` is the real one); an emulated
  checklist is a candidate cookie with a viewed-by claim. `UseReadOnlyEmulation` refuses every change under `/api` while
  either lasts. Keep it that way: nothing may be recorded, booked or emailed in someone else's name.
- Roles: `UserService` derives them from the `StaffMember` whitelist on every request, for Entra and
  sandbox sign-ins alike. Not on the list = signed in with no roles.
- Seeds: `server.core/Seed/`. `ReferenceDataSeeder` runs in every environment and only inserts what
  is missing. `DevelopmentSeeder` (fictional fixtures) runs only with `DevelopmentData__SeedOnStartup=true`.

EF design-time commands need the app to start without Entra:

    Auth__UseLocal=true DB_CONNECTION="Server=localhost,14333;Database=Design;User ID=sa;Password=x;Encrypt=False;TrustServerCertificate=True;" \
      dotnet ef migrations add <Name> -p server.core -s server

### Local environment

SQL Server 2022 is amd64-only. On Apple Silicon it runs only under **Rosetta** (Docker Desktop
setting, or Colima `--vm-type vz --vz-rosetta`); under QEMU it aborts at startup. jdwriter-app's
`docs/RUNNING-LOCALLY.md` has the setup and a check.

Several template files have CRLF or mixed line endings. Avoid end-of-line anchors (`$`) in
`sed` edits; they silently fail to match.

## Template guidance

This is a full-stack web application template using modern React and .NET technologies. Please follow these guidelines when generating code suggestions.

## Pull requests

Follow `.github/pull_request_template.md` when creating or updating pull requests, including through the CLI. Use a concise, descriptive title and describe the final change. Remove optional sections that do not apply, and never claim validation that was not performed.

## Docker sandbox for investigation

Use the [Docker sandbox quick start](README.md#run-the-docker-sandbox) to investigate the current checkout with local sign-in and sample data. See [the sandbox guide](docs/SANDBOX.md) for role checks, logs, alternate ports, and browser investigation.

## Architecture Overview

- **Frontend**: React 19 with TypeScript, built with Vite
- **Backend**: ASP.NET Core 10 Web API
- **Development Flow**: Vite serves the frontend on port `5173` and proxies API/auth/health requests to ASP.NET Core on port `5165`
- **Visual Studio Integration**: ASP.NET Core `SpaProxy` can launch Vite for Visual Studio users
- **Production Flow**: ASP.NET Core serves the built frontend from `server/wwwroot`

## Frontend Technology Stack

### Build Tools & Development

- **Vite** (`^7.1.5`) - Primary build tool and dev server on port `5173`
- **TypeScript** (`^5.9.2`) - Primary language for all React components
- **Node.js** - See [development setup](README.md#set-up-for-development) for runtime requirements and setup.

### React & Routing

- **React** `^19.1.1` with **React DOM** `^19.1.1`
- **TanStack Router** (`^1.132.33`) - File-based routing system
  - Routes live in `src/routes/`
  - The generated route tree is in `routeTree.gen.ts`
  - Router context includes the shared `QueryClient`
  - Default preload strategy is `'intent'`
  - Router devtools are enabled in development

### State Management & Data Fetching

- **TanStack Query** (`^5.90.2`) - Server state management
  - `QueryClient` is created in `src/main.tsx`
  - React Query devtools are enabled in development
  - Router preloading uses `defaultPreloadStaleTime: 0`

### Forms & Tables

- **TanStack React Form** (`^1.23.5`) - Form state management
- **TanStack React Table** (`^8.21.3`) - Table/data grid functionality

### Styling & UI

- **Tailwind CSS** (`^4.1.14`) - Utility-first CSS framework
- **DaisyUI** (`^5.1.27`) - Tailwind CSS component library
- **UC Davis Gunrock Tailwind** - Custom design system; dependency version is in [client/package.json](client/package.json).
- CSS imports are structured like:
  ```css
  @import "tailwindcss";
  @plugin "daisyui";
  @import "@ucdavis/gunrock-tailwind/imports.css";
  ```

### Code Quality & Linting

- **ESLint** (`^9.35.0`) with custom config (`@nkzw/eslint-config`)
- **Prettier** (`^3.6.2`) - Code formatting
- **TanStack ESLint plugins** for Query and Router
- **Vitest** (`^4.0.5`) - Testing framework
- **Testing Library** + **jsdom** - Component and route tests

### Path Aliases

- `@/` resolves to `./src/`

## Backend Technology Stack

### Framework & Runtime

- **ASP.NET Core 10.0** - Web API framework
- **.NET 10.0** - Target framework
- **C#** with nullable reference types enabled

### Authentication & Authorization

- **Microsoft Identity Web** (`3.14.1`) - Authentication integration
- Cookie-based auth flow is exposed through backend endpoints like `/login` and `/signin-oidc`

### Monitoring & Observability

- **OpenTelemetry** - Distributed tracing and metrics
  - OTLP exporter
  - ASP.NET Core instrumentation
  - HTTP instrumentation

### Development Tools

- **Swashbuckle** (`6.9.0`) - Swagger/OpenAPI documentation
- **Dotenv.Extensions.Microsoft.Configuration** (`3.1.0`) - `.env` configuration loading
- **Microsoft.AspNetCore.SpaProxy** (`10.0.1`) - Visual Studio dev-time frontend launch support

## Development Patterns

### Development Request Flow

See [Development Architecture](docs/ARCHITECTURE.md#development-request-flow) for request-flow diagrams and [Vite's responsibilities](docs/ARCHITECTURE.md#clientviteconfigts) for the backend proxy routes.

### Project Structure

```text
/
├── client/              # Vite React app
│   ├── src/
│   │   ├── routes/      # TanStack Router file-based routes
│   │   ├── queries/     # TanStack Query hooks
│   │   ├── lib/         # API helpers and utilities
│   │   ├── shared/      # Reusable UI/auth components
│   │   └── test/        # Client tests
├── server/              # ASP.NET Core host app
│   ├── Examples/Notifications/ # Optional notification samples
│   ├── Controllers/
│   ├── Helpers/
│   ├── Properties/
│   ├── Program.cs       # Backend startup and middleware pipeline
│   └── server.csproj    # SpaProxy and publish integration
├── server.core/         # Shared server domain/data code
└── tests/server.tests/  # .NET server tests
```

### Routing Conventions

- File-based routing lives in `src/routes/`
- Protected routes are grouped under `(authenticated)/`
- The authenticated layout preloads the current user via `ensureQueryData(meQueryOptions())`
- Route components should use TanStack Router hooks and integrate cleanly with React Query

### Component Guidelines

- Use TypeScript for all components
- Prefer function components with hooks
- Use Tailwind CSS classes for styling
- Leverage DaisyUI components when appropriate
- Follow UC Davis Gunrock design system patterns

### Data Fetching

- Use TanStack Query for server state
- Create custom hooks and query options in `queries/`
- Prefer the shared `fetchJson` helper in `src/lib/api.ts`
- Keep API calls relative, for example `/api/example`
- Integrate query usage with router loading when route data is required up front

### Authentication Conventions

- In development, the browser talks to Vite on `:5173`, and Vite proxies auth and API requests to ASP.NET Core on `:5165`
- The authenticated route tree fetches `/api/user/me` before rendering child routes
- `fetchJson` redirects `401` responses to `/login?returnUrl=...` unless explicitly disabled
- Avoid hardcoding backend origins; use relative URLs so the same code works in development and production

### Form Handling

- Use TanStack React Form for complex forms
- Combine form state with TanStack Query mutations for server interactions
- Follow existing validation and submission patterns where present

### API Integration

- **Development mode**: Follow the [Vite proxy configuration](docs/ARCHITECTURE.md#clientviteconfigts) for backend routes
- **Production mode**: ASP.NET Core serves static files from `wwwroot/` and handles `/api` routes directly
- Authentication modes are documented under [Auth Configuration](README.md#auth-configuration)
- Use type-safe API client patterns for request and response shapes
- Prefer relative paths like `/api/example`, never hardcoded `http://localhost:5165/api/example`

### Development Commands

- `npm start` - Start backend and frontend together from the repo root
- `npm run start:server` - Start only the ASP.NET Core backend with `dotnet watch`
- `npm run start:client` - Start only the Vite dev server
- `npm run db:up` - Start the local SQL Server container
- `npm run db:down` - Stop the local SQL Server container
- `npm run db:logs` - Tail SQL Server logs
- `cd client && npm run build` - Build the frontend for production
- `cd client && npm run lint` - Run ESLint
- Client test commands: see [Client tests](README.md#client-tests).
- `dotnet test` - Execute the .NET test project(s)

### Testing

- Client tests use Vitest, jsdom, and Testing Library
- Server tests live under `tests/server.tests/`
- Frontend route work often needs auth-aware mocking because authenticated routes preload `/api/user/me`

## Code Generation Preferences

1. **Always use TypeScript** - No plain JavaScript files for app code
2. **Prefer functional components** - Use hooks over class components
3. **Use Tailwind CSS classes** - Avoid custom CSS unless necessary
4. **Leverage the TanStack ecosystem** - Router, Query, Form, and Table are the default tools
5. **Follow file-based routing** - Add route files in the correct `src/routes/` location
6. **Use type-safe API calls** - Maintain TypeScript interfaces for API responses
7. **Use modern React patterns** - Hooks, context, and current React APIs
8. **Prefer existing shared helpers** - Reuse `fetchJson`, query options, auth context, and shared components before adding new abstractions
9. **Environment-aware code** - Keep development and production behavior aligned through relative URLs and existing proxy/static-file patterns
10. **Responsive design** - Use Tailwind responsive utilities

### Optional Notifications

See [optional email notifications](server.core/Notification/README.md) for reusable services, sample boundaries, configuration, and removal steps.

### C# Readability Preferences

Prefer simple, explicit C# conditionals that balance compactness with readability. Use familiar null-conditional operators, direct boolean checks, and short local variables when they make intent easier to scan.

Avoid advanced pattern-matching forms for routine null checks, collection checks, property extraction, or aliasing. Use pattern matching only when it clearly models the domain logic better than ordinary conditionals.

### Async Timing and Synchronization

Do not use arbitrary delays, sleeps, timers, or timeout-based retries to hide race conditions or make async code appear to work. Avoid patterns like `setTimeout`, `Task.Delay`, polling loops, or fixed wait times unless the delay itself is a real product requirement.

When coordinating async work, prefer explicit completion signals and proper async primitives: `Promise` chains, `async`/`await`, awaited task results, cancellation tokens, event callbacks, query/mutation lifecycle hooks, router loaders, or framework-provided readiness states.

If timing code seems necessary, first identify the real dependency being waited on and model that dependency directly. Use timeouts only for bounded failure handling, cancellation, user feedback, or external-system resilience, not as a substitute for correct async flow.

## Common Patterns

### Route Component Example

```tsx
import { createFileRoute } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';

export const Route = createFileRoute('/(authenticated)/example')({
  component: ExampleComponent,
});

function ExampleComponent() {
  const { data } = useQuery({
    queryKey: ['example'],
    queryFn: () => fetch('/api/example').then((res) => res.json()),
  });

  return (
    <div className="container mx-auto p-4">
      {/* DaisyUI and Tailwind styling */}
    </div>
  );
}
```

### API Controller Pattern

```csharp
[ApiController]
[Route("api/[controller]")]
public class ExampleController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetExample()
    {
        // Implementation
    }
}
```

## Important Development Notes

1. **Port Usage**:
   - Backend runs on `http://localhost:5165`
   - Vite runs on `http://localhost:5173`
   - For command-line development, use `http://localhost:5173`
   - Visual Studio may launch through the backend profile and redirect to `:5173` via `SpaProxy`

2. **API Calls**:
   - Always use relative paths like `/api/example`
   - Do not hardcode localhost ports into frontend fetch calls
   - Relative paths keep auth and proxy behavior correct in both development and production

3. **Proxy Configuration**:
   - Development proxying lives in `client/vite.config.ts`
   - `SpaProxy` settings live in `server/server.csproj` and launch settings
   - `server/Program.cs` does not proxy Vite in development

4. **Hot Reload**:
   - Vite HMR runs directly on `:5173`
   - Backend changes trigger `dotnet watch` restarts on `:5165`
   - Visual Studio users can rely on `SpaProxy` to launch the frontend automatically

5. **Production Build**:
   - `cd client && npm run build` outputs frontend assets to `client/dist/`
   - `dotnet publish` also builds the client and copies `client/dist/` into `wwwroot`
   - ASP.NET Core serves static files directly in production
   - See [the middleware responsibilities](docs/ARCHITECTURE.md#serverprogramcs) for static files and SPA fallback behavior

When generating code, ensure it follows these patterns and integrates well with the existing technology stack.
