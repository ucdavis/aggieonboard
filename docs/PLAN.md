# AggieOnboard: candidate onboarding guide — prototype plan

> **STATUS 2026-10-22 — Phases 0–3 done. Public at github.com/ucdavis/aggieonboard (template history kept; `upstream` remote). Next: Phase 4 (candidate portal).**
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

## The journey: steps and messages

Built in Phase 1. Source of truth: `server.core/Seed/JourneyCatalog.cs` (steps, settings,
holidays) and `server.core/Seed/Messages/*.md` (messages).

**Steps and messages are separate**, because they don't line up one to one. A *step* is
something tracked: staff advance it and the portal lists it. A *message* is something sent. Message
12 covers four steps; the qualification step has a request (4) and an acknowledgment (5); and
SkillSurvey, Universal and Quest send their own email for a step. Each step names its messages by
key: `RequestMessageKey` (sending it activates the step), `AcknowledgmentMessageKey`, and
`VendorMessageKey` (communications' proposed wording for the vendor, kept but never sent). Audit #3
("reference check completed") is not a message; it is step 1 turning Complete in the portal.

| # | Step key | Audit | Owner | Messages | Watch for | Condition | Deadline (✱ = unconfirmed) |
|---|---|---|---|---|---|---|---|
| 1 | `references` | 1–3 | RA | 1 · vendor 2 | SkillSurvey | — | 2 business days after activation ✱ |
| 2 | `qualifications` | 4–5 | RA | 4 · ack 5 | — | has qualifications | staff-entered ✱ |
| 3 | `offer` | 6 | Recruiter | — | jobs application portal | — | none |
| 4 | `employee-data` | 7 | Recruiter | 7 | DocuSign | — | staff-entered ✱ |
| 5 | `background-check` | 8 | OS | vendor 8 | Universal | — | 48 clock hours ✱ |
| 6 | `employee-health` | 9 | OS | 9 | — | — | clearance ✱ |
| 7 | `drug-screen` | 10 | OS | vendor 10 | Quest (noreply@questdiagnostics.com) | drug screen | 48 business hours ✱ |
| 8 | `i9` | 11A | OS | 11A | I-9 Tracker Complete | — | clearance ✱ |
| 9 | `i9-remote` | 11B | OS | 11B | I-9 Tracker Complete | chose remote | clearance ✱ |
| 10 | `dmv` | 12 | OS | 12 (section) | DocuSign | DMV | staff-entered |
| 11 | `fingerprinting` | 12 | OS | 12 (section) | — | Live Scan | clearance ✱ |
| 12 | `position-description` | 12 | OS | 12 (section) | DocuSign | PD | staff-entered |
| 13 | `parking` | 12 | OS | 12 (section) | — | parking | none |
| 14 | `cleared` | 13 | OS | 13 | DocuSign | — | day before start ✱ |
| 15 | `new-employee-welcome` | 14 | L&D | — | Learning and Development | — | 30 days after start |

*Clearance* = start date − `ClearanceBusinessDaysBeforeStart` (default 6) business days.

Two portal wins come straight from the audit (Phase 5). **11A, 11B and 12 ask the candidate to "reply to this
email"** with a choice, a representative's contact, an appointment time or an ATI number; the
portal turns those into small forms. **#7's need, "can staff see whether the form was completed
before sending another request?"**, is answered by step status. The copy still says "reply", so
updating it is a communications decision (see *Copy to review*).

### Message files

Each message is Markdown with front matter (`key`, `audit`, `sender`, `subject`). Merge fields are
`{{Field}}` from the closed list in `server.core/Messaging/MergeFields.cs`. A conditional run is
wrapped in `<!-- if: Condition -->` … `<!-- end -->` (message 4's license/degree lists, message 12's
four requirements). The files were generated from the TA/SSO document by cutting each message
out and swapping only its `[placeholders]` for merge fields; tests fail if an app message
still contains a bracketed placeholder or names an unknown field. Vendor messages (2, 8, 10) keep
their original brackets, since the app never fills them. Placeholders that stood for links became
Markdown links using the placeholder's own words (`[OEHC appointment/request form](…)`,
`[Schedule fingerprinting appointment](…)`). Message 7's bare `[Link]` became `[Link](…)`.

Not carried over, because they are notes rather than copy: #3 ("Remove?"), #6's internal SharePoint/Box
links, #13's "What changes" commentary, and #14's Box link.

### Copy to review

Seeded verbatim. These look like drafting artifacts, but changing them is your call:

- **4**: "Please let me know if you have any questions.Questions? Contact {{RecruitingAssistantContact}}." The sentence is doubled and has no space after the period.
- **7**: the form link's text is just "Link". Accessible link text would name the form.
- **11B**: the paragraph "The supplied remote template requires a representative over 18…" reads like a
  note about the source template and repeats the previous paragraph. A lone "." follows the last link.
- **11A / 11B / 12**: "reply to this email" instructions will need updating once the portal collects those answers (Phase 5).

---

## Domain model (normalized SQL, EF Core — same approach as JDWriter)

Built in Phase 1 (`server.core/Domain/`). Enums are stored as their names.

- **StepDefinition**: key, order, audit reference, title, phase, owner role, condition, message
  keys, deadline rule (owned: anchor, amount, unit), `DeadlineConfirmed` + `DeadlineQuestion`,
  and "watch for" sender / from address / subject.
- **MessageTemplate** (`Key` + `Version` unique; edits add versions) with ordered
  **MessageTemplateSection**s (condition + Markdown body). Sender is App or External.
- **Candidate**: name, email, position, department, hiring-manager contact, anticipated start date,
  employee ID, assigned RA / recruiter / onboarding specialist, requirement flags (drug screen,
  DMV, fingerprinting, PD, parking), I-9 method, and **CandidateQualification**s (license /
  certification / degree + document to submit). `Meets(condition)` decides steps and sections.
- **CandidateStep**: status (`Upcoming → ActionNeeded → Waiting → Complete`, plus `Skipped`,
  `Blocked`), activated at, due date + optional due time (Pacific), `DueOverridden`, completed
  at/by, staff note. Moves go through `server.core/Journey/CandidateJourney.cs`, which the seeds
  use too, so fixtures are always reachable states.
- **StaffMember**: name, email, phone, role, active. Phase 2 makes it the sign-in whitelist.
- **AppSetting**: key, value (null = unanswered, which blocks messages that need it), kind,
  description, confirmed flag, open question.
- **Holiday**: UC Davis observed holidays, Nov 2026 – Sep 2027 (dates.ucdavis.edu). Extend yearly.

Later phases add: **CandidateResponse** (5), **SentMessage** (3, built: subject, HTML and text as sent, template version, sender, automatic flag), **CandidateAccessToken** (4),
**AuditEvent** (2, built: every staff change, shown as the candidate's history).

### Deadline rules

`DeadlineRule(Anchor, Amount, Unit)`: anchors `None` (staff enter the date), `Activation`,
`Clearance`, `BeforeStart`, `AfterStart`; units `Hours`, `BusinessHours`, `CalendarDays`,
`BusinessDays`, `Weeks`. One `BusinessCalendar` (weekends + holidays, Pacific time) does all date
maths. Choices made where the audit is silent, all tested:

- Business days never count the activation day itself; the due date is the end of the nth business day.
- "Business hours" = a clock that runs only on business days (24 h each). Whether SSO means office
  hours instead is open (audit #8, #10). A start on a weekend or holiday waits for the next business day.
- Clock hours are real elapsed time, so a daylight-saving change is counted correctly.
- A deadline landing exactly on midnight reads as the end of the day before; due times are kept to the minute.
- Staff can override any due date, and recomputation leaves an override alone.

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

**Phase 0: Repo from the template.** ✅ 2026-10-22. Create `ucdavis/aggieonboard` from the template, commit the
cleaned template first (README.customization §8: remove weather/showcase), rename the identity and
cookie, and confirm the Docker sandbox runs. On Apple Silicon, SQL Server needs Rosetta (see
jdwriter-app `docs/RUNNING-LOCALLY.md`). Write AGENTS.md with the invariants above.

**Phase 1: Domain and seed.** ✅ 2026-10-22: 15 steps, 12 messages, 7 settings, 14 holidays, 5 staff and 8
candidates; `InitialCreate` verified on SQL Server in the sandbox. Planned: schema and migration; seed the step catalog, the templates (from doc 2),
settings, and contacts; seed ~8 fictional candidates spread across the journey (one at each phase,
one overdue, one remote I-9, one with all conditional flags). Build `BusinessCalendar` and the
deadline rules with unit tests.

**Phase 2: Staff slice.** ✅ 2026-10-22: done as planned, plus create/edit candidate with qualifications. The
sandbox's Sample User is a whitelisted onboarding specialist and admin; Basic User is not staff. Demo verified on the
sandbox: a candidate with every requirement walked from step 1 to 15. Planned: whitelist roles, pipeline table, candidate detail, step status changes,
condition flags (add/skip steps), and audit events. Return 404 for unknown `/api/*` paths: the template's SPA
fallback currently answers them with `index.html` and a 200. *Demo: move a candidate from step 1 to 15 by hand.*

**Phase 3: Communications.** ✅ 2026-10-22. Messages render from Markdown (Markdig, raw HTML off, every value
escaped) into their own MJML layout: "UC Davis Health", no "do not reply" footer, Reply-To the sending staff member,
From name "UC Davis Health". Sending a request message starts the steps it asks for; delivery happens first, so a
refused send records nothing. "Candidate done" on qualifications sends message 5, or says why it could not.
Contact fields are the email ("or" phone) because the copy reads "Contact me at [contact]". Verified on the sandbox:
messages 9 and 5 delivered to Mailpit. Planned: template rendering, preview with missing-field detection, send via
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
