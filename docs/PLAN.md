# AggieOnboard: candidate onboarding guide — prototype plan

> **STATUS 2026-10-22 — Phase 0 done. Public at github.com/ucdavis/aggieonboard (template history kept; `upstream` remote). Next: Phase 1.**
> Built on `ucdavis/web-app-template` (same stack as jdwriter-app), greenfield — there is no POC.
> Sources (in `Reference/`, gitignored): `TA_SSO Onboarding Communications.docx` (the 14 rewritten
> messages) and `Communications Needs_gdoc.docx` (the audit: sender, timing, changes, open "Needs" per message).

## Context

A UC Davis Health candidate today gets 14+ emails from at least 8 senders (recruiting assistant,
SkillSurvey, recruiter, Universal Background Screening, onboarding specialist, Quest, I-9 Tracker,
DocuSign, IT, L&D) between "you're a top candidate" and their first day. The communications audit
rewrote each message to say *what to do, by when, and what happens next* — but each message still
stands alone, and the candidate has to assemble the whole picture from their inbox.

AggieOnboard puts that journey in one place:

- **Candidates** get a portal: one checklist showing where they are, what to do now, what email to
  watch for (sender + subject, so they can find it in spam), the deadline, and who to ask.
- **Staff** (RA, recruiter, onboarding specialist) get a pipeline: every candidate's current step,
  what is overdue, and a one-click send of the step's approved message with fields filled in.

### Decisions made (2026-10-22)

| Decision | Choice |
|---|---|
| Users | **Both** — staff pipeline + candidate portal |
| Candidate sign-in | **Emailed magic link** (candidates have no UC Davis account); staff use Entra SSO as the template does |
| Emails | **The app sends them** — the rewritten templates live in the app; staff preview and send |
| Scope | **Full journey, steps 1–14** — reference check through New Employee Welcome |
| Integrations | **None in the prototype.** Staff record outside-system status (SkillSurvey, Universal, Quest, I-9 Tracker, DocuSign, UCPath, TAM) by hand |

### How this differs from JDWriter

JDWriter was a port: the POC was the spec and the plan went bottom-up against golden fixtures.
Here the *process* is the spec and it is still moving (the audit lists ~30 unanswered "Needs").
So the sequencing is **vertical slices, demo-able early**: staff can move a fictional candidate
through the pipeline by the end of Phase 2, and stakeholders can see the candidate portal at Phase 4.
Unconfirmed process facts become settings, not code, so SSO/TA answers do not need a release.

---

## The journey (step catalog)

Seeded from the two documents. **Sender** says who sends the email: *App* means we send it; *External*
means a vendor sends it and the portal shows a "watch for this email" card instead.

| # | Step | Owner | Email | Candidate does | Condition |
|---|---|---|---|---|---|
| 1 | Reference check introduced | RA | App | Enter 3+ references in SkillSurvey within 2 working days | — |
| 2 | SkillSurvey request | SkillSurvey | External | Use personalized link | — |
| 3 | Reference check completed | RA | **None** (removed in audit) | — | Portal status update fills the gap the audit worried about |
| 4 | Qualification verification | RA | App | Send listed license/cert/degree docs by deadline via approved method | Per-position requirement list |
| 5 | Qualification documents received | RA | App, **auto** on staff marking #4 received | Nothing | Audit asked for exactly this |
| 6 | Offer | Recruiter / TAM | External (TAM portal) | Accept/decline in jobs portal | Milestone only |
| 7 | Employee Data Gathering Form | Recruiter (TBC) | App | Complete DocuSign form by deadline | — |
| 8 | Universal background check | Universal | External | Complete within 48 hours | — |
| 9 | SSO welcome & clearance | Onboarding specialist | App | OEHC form now; overview of drug screen + background | Drug-screen section if required |
| 10 | Quest drug screen | Quest | External | Test within 48 business hours | Drug screen required |
| 11A | I-9 choice | Onboarding specialist | App | Section 1 in I-9 Tracker; **choose in-person (Wednesday slot) or remote in portal** | — |
| 11B | Remote I-9 | Onboarding specialist | App | **Enter representative name + email in portal** | Chose remote |
| 12 | Conditional requirements | Onboarding specialist | App, sections per flag | DMV pull notice / fingerprinting (enter appointment + ATI number in portal) / sign PD / parking | DMV, Live Scan, PD, parking flags |
| 13 | Final clearance | Onboarding specialist | App | DocuSign paperwork, claim accounts, privacy training | — |
| 14 | New Employee Welcome | L&D | External | Attend within 30 days | — |

Two portal wins come directly from the audit. **11A/11B and 12 currently ask the candidate to "reply to this email"**
with a choice, a representative's contact, an appointment time, or an ATI number. The portal turns
those into small forms whose answers land on the staff record. **#7's need, "can staff see whether the form was completed
before sending another request?"**, is answered by step status.

---

## Domain model (normalized SQL, EF Core — same approach as JDWriter)

- **Candidate**: name, preferred first name, email, position title, department, anticipated start
  date, employee ID (set at #13), assigned RA / recruiter / onboarding specialist, archived flag.
- **CandidateRequirement**: the condition flags (drug screen, DMV, Live Scan, PD, parking) plus the
  list of required licenses/certifications/degrees for #4. Toggling a flag adds or skips steps.
- **StepDefinition** (seeded, admin-editable): key, order, phase (TA / SSO / L&D), owner role,
  sender (App/External), external sender name + from-address + subject (for "watch for" cards),
  condition, deadline rule, candidate-facing instructions, "what happens next" text, dependencies.
- **CandidateStep**: one per candidate per applicable step. Status
  `Upcoming → ActionNeeded → Waiting (submitted / with vendor) → Complete`, plus `Skipped` and `Blocked`.
  Due date, completed at/by, internal staff note.
- **CandidateResponse**: typed portal answers (I-9 choice + slot, representative name/email,
  fingerprint appointment, ATI number), timestamped, visible on the staff record.
- **MessageTemplate** (versioned): subject + body with merge fields (`{{FirstName}}`,
  `{{PositionTitle}}`, `{{ClearanceDeadline}}`…), converted from the doc's `[First name]` placeholders.
- **SentMessage**: recipient, template version, rendered subject/HTML/text, sender, timestamp.
- **Settings / Contact / Holiday**: deadline values, contacts per role, Wednesday I-9 hours and
  address, UC holiday calendar for business-day math.
- **StaffUser** (whitelist + role, as JDWriter did): `Admin`, `RecruitingAssistant`, `Recruiter`,
  `OnboardingSpecialist`.
- **CandidateAccessToken**: SHA-256 hash, candidate, expiry, consumed-at.
- **AuditEvent**: who changed which step/flag/template, when.

### Deadline rules (configurable, because the audit hasn't confirmed them)

- `TriggerPlus(n, BusinessDays | Hours | BusinessHours)` → e.g. #1 = 2 working days, #8 = 48 hours, #10 = 48 business hours.
- `StartDateMinus(n, BusinessDays)` → clearance deadline = start − 6 business days (#9, #12).
- `StartDateMinus(n, Weeks)` → OEHC appointment ≥ 2 weeks before start.
- `None` / staff-entered → #4, #7 until TA/SSO confirm.

One `BusinessCalendar` service does all date math against the holiday table and is unit-tested at
the edge cases: Friday triggers, holidays, start dates on Mondays, and "48 business hours" starting mid-day.

---

## Architecture on the template

- **Staff auth**: the template's Entra OIDC, unchanged. `UserService.GetRolesForUser` reads the
  `StaffUser` whitelist. Unknown UC Davis accounts get no role, so they see only "request access".
- **Candidate auth**: a second cookie scheme, `Candidate`, with its own cookie name.
  - `GET /c/{token}` hashes the token, checks it is unexpired and unconsumed, marks it consumed, issues
    the candidate cookie (7-day sliding), and redirects to `/portal`.
  - Every app-sent email carries a "View your onboarding checklist" button with a fresh token, so the
    emails and the portal reinforce each other.
  - `POST /api/portal/link` (request a new link) always returns the same response whether or not the
    email exists, and is rate-limited.
  - Authorization policies keep the two worlds apart: `/api/staff/*` requires a staff role, and
    `/api/portal/*` requires the candidate scheme. The candidate ID comes **only from the session**, never from the URL.
  - The sandbox gains fictional candidate personas next to the template's sample users, so the portal can
    be demoed without email.
- **Email**: the template's `INotificationRenderer` + MJML layout + `IEmailService`. Merge fields are
  rendered into the shared layout. The sandbox sends to Mailpit, so the full 14-step journey can be
  watched in the local inbox.
- **Client** (TanStack Router file routes):
  - `(staff)/` — `pipeline` (TanStack Table: candidate, current step, due, overdue, owner; "mine"
    filter), `candidates/$id` (timeline, flags, send-with-preview, mark status, responses, message
    history), `templates`, `settings`, `admin/users`.
  - `portal/` — mobile-first, plain language, WCAG 2.1 AA: progress by phase ("Reference check →
    Qualifications → Offer → Pre-employment → Cleared → First day"), *Do now* cards with deadlines,
    *Watch for* cards, *What happens next*, contacts. No internal statuses or staff notes.
  - Public: `/c/$token`, `/portal/link-expired` (request a new link).

---

## Invariants (go into AGENTS.md on day one)

1. **No sensitive identity data in AggieOnboard.** No SSN, birth date, driver's license, address
   history, or uploaded transcripts/licenses. Those stay in the vendor systems (DocuSign, Universal,
   I-9 Tracker). The app holds name, email, position, dates, and statuses. This keeps the prototype
   out of the high-risk data category. Revisit only with an explicit decision.
2. **A candidate session sees exactly one candidate.** Every portal query filters by the session's
   candidate ID, and tests prove cross-candidate reads and writes fail.
3. **Magic-link tokens are stored hashed, single-use, and expiring.** Requesting a new link never
   reveals whether an email address is on file.
4. **Unconfirmed process facts are settings, never constants.** "48 business hours", "six business
   days before start", Wednesday 8–2, and the representative's age rule are all on the open-questions list.
5. **No message is sent with an unfilled merge field.** Preview shows the gaps and Send is disabled.
6. **Sent messages are logged as rendered**, with template version, so "what did we tell them" has an answer.
7. **Copy is owned by communications.** Templates are seeded verbatim from the TA/SSO document and edited
   in the app, not rewritten in code.
8. **Fictional data only** until a pilot is approved. No real candidate records in dev, test, or the repo.

---

## Phases

**Phase 0: Repo from the template.** Create `ucdavis/aggieonboard` from the template, commit the
cleaned template first (README.customization §8: remove weather/showcase), rename the identity and
cookie, and confirm the Docker sandbox runs. On Apple Silicon, SQL Server needs Rosetta (see
jdwriter-app `docs/RUNNING-LOCALLY.md`). Write AGENTS.md with the invariants above.

**Phase 1: Domain and seed.** Schema and migration; seed the step catalog, 14 templates (from doc 2),
settings, and contacts; seed ~8 fictional candidates spread across the journey (one at each phase,
one overdue, one remote I-9, one with all conditional flags). Build `BusinessCalendar` and the
deadline rules with unit tests.

**Phase 2: Staff slice.** Whitelist roles, pipeline table, candidate detail, step status changes,
condition flags (add/skip steps), and audit events. *Demo: move a candidate from #1 to #14 by hand.*

**Phase 3: Communications.** Template rendering, preview with missing-field detection, send via
SMTP→Mailpit, the SentMessage log, and the auto-acknowledgment for #5. *Demo: the full journey in the Mailpit inbox.*

**Phase 4: Candidate portal (read).** Candidate auth scheme, magic links in every email, the portal
timeline with Do now / Watch for / What happens next / contacts, and the request-new-link flow. Isolation tests.
*Stakeholder demo milestone: walk TA and SSO through it.*

**Phase 5: Candidate responses.** I-9 choice and Wednesday slot (#11A), representative (#11B, which then
unlocks the step), fingerprint appointment and ATI number (#12). Staff see the answers, and the step advances.

**Phase 6: Admin.** Template editor (versioned, with merge-field validation), settings and holiday
calendar, staff user management.

**Phase 7: Pilot readiness.** Accessibility pass, `/security-review`, and Azure deploy via the template's
GitHub Actions + Bicep. These need **user-only prerequisites**: a new Entra app registration (not
JDWriter's), Azure resources, an approved SMTP relay and sender address able to email external
candidates, and data-classification sign-off. Then set the repository variable `DEPLOY_TEST_ON_PUSH=true` to
turn on the automatic test deploy that `ci-cd.yml` runs on pushes to main.

## Verification

- `dotnet test` per phase: calendar edge cases, deadline rules, step transitions, token lifecycle,
  candidate isolation, merge-field validation.
- Client tests (Vitest + MSW) for the pipeline, candidate detail, and portal cards.
- End-to-end in the Docker sandbox: send #1 → open the Mailpit email → follow its magic link →
  portal shows the correct Do-now card → staff advance → portal updates.

## Open questions

These are from the audit's "Needs" lists. Each has a placeholder setting so it doesn't block the build:

- TA: approved submission method for qualification documents (#4). Who sends the EDGF email in the future process (#7)?
- SSO: what "48 business hours" means and when the clock starts (#8, #10). Clearance deadline = 6 business days? (#9)
  OEHC timing vs I-9. Representative eligibility (age 18+, family excluded?). In-person I-9 days/hours. Is a separate
  in-person confirmation email needed? Must fingerprinting be scheduled, attended, or cleared before start? Which #13 forms are in DocuSign?
- Vendors: can SkillSurvey, Universal, and Quest wording be customized? This only affects the "watch for" card text.
- Ours: repo name/owner; whether a later version should accept document uploads (would break invariant #1, so it needs a data decision).
