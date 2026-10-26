# AggieOnboard: candidate onboarding guide — prototype plan

> **STATUS 2026-10-26 — Phases 0–6 done. Public at github.com/ucdavis/aggieonboard (template history kept; `upstream` remote). In progress: the one-place redesign (see "Redesign: groups, stages, one place"); steps 1–5 done, next the staff timeline by group. Then Phase 7 (hosting).**
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
`[Schedule fingerprinting appointment](…)`). Message 7's bare `[Link]` became `[Link](…)`. A file with `version: 2` in its front matter (`*.v2.md`) is later wording
for the same message, written by us and listed under *Copy to review*; the highest version is the one sent.

Not carried over, because they are notes rather than copy: #3 ("Remove?"), #6's internal SharePoint/Box
links, #13's "What changes" commentary, and #14's Box link.

### Copy to review

Seeded verbatim. These look like drafting artifacts, but changing them is your call:

- **4**: "Please let me know if you have any questions.Questions? Contact {{RecruitingAssistantContact}}." The sentence is doubled and has no space after the period.
- **7**: the form link's text is just "Link". Accessible link text would name the form.
- **11B**: the paragraph "The supplied remote template requires a representative over 18…" reads like a
  note about the source template and repeats the previous paragraph. A lone "." follows the last link.
- **11A / 11B / 12**: version 2 of each (`*.v2.md`, drafted by us in 5c and sent from then on; version 1 stays as the audit
  wrote it) changes only the "reply to this email" instructions to point to the checklist:
  - 11A: "In your onboarding checklist, choose whether you'd like to complete Section 2:", "Book your Wednesday date and
    time in your checklist." and "choose **Remote** in your checklist".
  - 11B: "In your onboarding checklist, enter the **full name** and **email address** of the adult who will serve as your
    authorized representative." in place of the reply-with block.
  - 12 (fingerprinting): step 1 books "In your onboarding checklist" (the external scheduling link is gone), step 2 "Send
    your appointment confirmation" is removed (the app knows), and the ATI step says "Enter the ATI number in your
    onboarding checklist". The steps are renumbered 1–3.
  - Left alone in version 2 too: 11B's template-note paragraph and lone "."; 12's "Print the **attached** …
    Authorization … form" (the app attaches nothing, in either version); 11A's "Wednesdays from 8 a.m.–2 p.m." is copy, so
    it won't follow the I-9 desk's hours if they change in the schedule.

New wording the app needed (not in the audit), written by us and open to rewrite:

- **Sign-in link email** (`server.core/Seed/Messages/p1-portal-link.md`): "Your UC Davis Health onboarding checklist" / "Use the button below to open your onboarding checklist…".
- **Button on every email**: "View your onboarding checklist".
- **Appointment emails** (`server.core/Seed/Messages/p2…p5-appointment-*.md`): "Booked: / Changed: / Cancelled: /
  Tomorrow: {appointment}", each with When / Where (and Was, for a change), "Until {cutoff}, you can change or cancel this
  appointment from your onboarding checklist. After that, reply to this email." No "what to bring": the audit doesn't say.
- **Booking in the portal**: "Book {appointment}", "Change time", "Cancel appointment" / "Cancel this appointment? Yes,
  cancel it / Keep it", "Until {cutoff}, you can change or cancel it here.", "It's too close to change here. To change it,
  reply to your confirmation email.", "You can book {appointment} once we ask you to. We'll email you when it's time.",
  and "Your {appointment} appointment is booked. We're emailing you the details."
- **Answers in the portal** (5c): "Choose how you'll verify your documents", "Or verify remotely, with an authorized
  representative.", "Verify remotely" / "Verify remotely? We'll email you what to do next. Yes, verify remotely / Back",
  "You chose to verify your documents remotely, with an authorized representative.", "You chose remote verification.
  We're emailing you the next steps."; "Your authorized representative" with "Full name" / "Email address"; "Your ATI
  number" with "The Automated Transaction Identifier the police department gave you at your fingerprinting appointment.";
  "Send", "Change", "Thanks. We have your representative's details." / "Thanks. We have your ATI number."; and the
  refusals "You can choose how to verify your documents once we ask you to…", "Cancel your in-person appointment first,
  then choose remote verification.", "You chose remote verification. To verify in person instead, contact your onboarding
  specialist." and "You can enter your ATI number after your fingerprinting appointment."
- **The pull-along portal** (redesign step 5): each step's button label and, where a step has no email of its own,
  its summary (`JourneyCatalog.Tasks`); "Welcome, {name}", "{n} of {m} steps done", "Your next step", "Click here to
  {label}", "Also to do now", "We're working on it", "You've done your part. We'll let you know if we need anything
  else.", "Done (n)", "More steps appear here as you finish these.", "Done: {task}. Next up: {task}."; the stand-in for
  tasks in other systems ("This is where you'll complete it. In the finished checklist, this opens right here…",
  "I've done this" / "Not yet"); and the new-steps email (`p6-new-tasks.md`: "Next steps in your UC Davis Health
  onboarding", "You have new steps in your onboarding checklist: …"). The audit's messages, shown on task pages,
  still name vendors in places ("the link sent to you by I-9 Tracker Complete").
- **Portal labels**: "Your onboarding checklist", "To do / In progress / Coming up / Done", "Nothing to do right now",
  "You're all caught up. We'll email you when there's something to do.", "Watch for an email from … Check your spam or
  junk folder if you don't see it.", "Read our email: …", "Questions? Contact … at …", the Continue page ("For your
  security, this link works once…") and the new-link form ("If that address is on file, we've emailed it a new link…").

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
  DMV, fingerprinting, PD, parking), I-9 method, the remote I-9 representative (name, email) and fingerprinting ATI number
  (5c), and **CandidateQualification**s (license /
  certification / degree + document to submit). `Meets(condition)` decides steps and sections.
- **CandidateStep**: status (`Upcoming → ActionNeeded → Waiting → Complete`, plus `Skipped`,
  `Blocked`), activated at, due date + optional due time (Pacific), `DueOverridden`, completed
  at/by, staff note. Moves go through `server.core/Journey/CandidateJourney.cs`, which the seeds
  use too, so fixtures are always reachable states.
- **StaffMember**: name, email, phone, role, active. Phase 2 makes it the sign-in whitelist.
- **AppSetting**: key, value (null = unanswered, which blocks messages that need it), kind,
  description, confirmed flag, open question.
- **Holiday**: UC Davis observed holidays, Nov 2026 – Sep 2027 (dates.ucdavis.edu). Extend yearly.

Later phases add: **CandidateResponse** (5; not needed after all: the two answers are candidate fields and their history
is the audit log), **SentMessage** (3, built: subject, HTML and text as sent, template version, sender, automatic flag), **CandidateAccessToken** (4, built: SHA-256 hash, expiry, consumed-at),
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

**Phase 4: Candidate portal (read).** ✅ 2026-10-23. Decisions beyond the plan:
- **Links open a "Continue" page; only its POST redeems the link.** Email security scanners (UC Davis uses Outlook
  Safe Links) open every link; a one-time link redeemed on GET would be spent before the candidate clicked.
- Tokens: 32 random bytes, SHA-256 stored, single use, 14 days (`Portal:LinkLifetimeDays`); 7-day sliding session
  (`Portal:SessionDays`); at most 3 links per candidate per hour; anonymous endpoints rate-limited (10/min/IP);
  "email me a new link" always answers 202 after at least one second, so timing says nothing.
- Every app email carries "View your onboarding checklist" with a fresh one-time link. The **logged copy points at
  `/portal` instead**, so the message log never holds a credential. A token is recorded only after the email server
  accepts the message.
- The portal adds no step copy: per step it shows the title, due date, "watch for" (sender, address, subject), who to
  ask (the assigned owner), and the emails actually sent for it. Waiting and blocked both read "In progress";
  skipped steps and staff notes never leave the server.
- Staff: a Portal card with last sign-in and "Email a sign-in link" (previewed like any message).
- Verified on the sandbox: scanner GET doesn't spend a link; reuse refused; candidate cookie gets 401 from staff
  endpoints and staff cookie 401 from the portal; another candidate's email 404s; tampered cookie 401; rate limit 429.

Planned: candidate auth scheme, magic links in every email, the portal
timeline with Do now / Watch for / What happens next / contacts, and the request-new-link flow. Isolation tests.
*Stakeholder demo milestone: walk TA and SSO through it.*

**Phase 5: Scheduling and candidate responses.** Re-scoped 2026-10-23: AggieOnboard is the one stop shop, so
anything that needs an appointment is **booked in the app**, not replied to by email, and whoever runs each
appointment sees who is coming, when and where.

Decisions (2026-10-23): all four appointment types are booked in the app; rosters belong to **owner teams**;
candidates **book, change and cancel** up to a cutoff, with confirmation/change/cancel emails carrying a calendar
invite and a reminder the day before; reply-by-email wording in 11A/11B/12 gets new template versions that point
to the checklist, drafted by us and listed for review.

| Appointment | Owner team | Locations | Availability | Window (✱ unconfirmed) | Attendance completes the step? |
|---|---|---|---|---|---|
| In-person I-9 (Section 2) | SSO I-9 desk | 10850 White Rock Road, Rancho Cordova | Weekly hours: Wednesdays 8–2 ✱ | Before the clearance deadline ✱ | Yes |
| Fingerprinting (Live Scan) | UC Davis Health PD; UC Davis Campus PD | Sacramento; Davis (addresses ✱) | Weekly hours ✱ (owners set them) | Before the clearance deadline (message 12) | No: the ATI number and clearance follow |
| Employee Health (OEHC) | Employee Health | Sacramento (address ✱) | Weekly hours ✱ | At least two weeks before start (message 9) | No: clearance follows |
| New Employee Welcome | Learning and Development | Education Building, Sacramento campus | Dated sessions with seats (twice a month) | Within 30 days after start (message 13) | Yes |

Hours, capacities and addresses nobody has confirmed are **not seeded in production**; the sandbox gets clearly
labelled stand-ins. Every window and the change cutoff (default 24 hours) are data.

Model: **Team** (+ staff membership); **AppointmentType** (step it fulfils, owner team, length, booking window as
two `DeadlineRule`s, change cutoff, completes-on-attendance); **Location**; availability as **weekly hours** (day,
start, end, slot length, capacity), **sessions** (a dated block with seats, for NEW or extra hours) and
**closures** (plus the holiday table); **Appointment** (candidate, type, location, start/end, status
Booked → Attended / No-show / Cancelled, who booked or changed it, reminder sent). Booking checks capacity in a
serializable transaction so two people can't take the last seat.

Access: a staff member with journey role "Team member" signs in to the **schedule only** (no pipeline, no candidate
records beyond their roster). Team members manage their team's hours, sessions and closures and see its day
roster; onboarding specialists and other journey staff see their candidates' appointments and can book or change
on a candidate's behalf; admins manage every team.

Step linkage: booking moves the step to Waiting (the candidate has done their part); cancelling moves it back to
Action needed; marking attended completes it where the table says so. Booking in-person I-9 sets the I-9 method.

- **5a, the schedule:** ✅ 2026-10-23. Built as described, plus: a third sandbox sign-in, Schedule User (police-
  department team member, schedule only); staff booking from the candidate page; removing a session or closing a
  day with bookings on it is refused until they're moved. Schedule times are stored as Pacific wall-clock times. A
  booking race on SQL Server is resolved by the serializable transaction: the loser is SQL Server's deadlock victim
  (1205) and gets "That time was just taken" (verified: six simultaneous races, one winner each). Assumptions flagged
  for SSO: I-9 slots are 15 minutes, one candidate at a time. Planned: teams and team access; types, locations, weekly hours, sessions, closures; the slot
  engine; staff booking on a candidate's behalf; the **day roster** per team and location (attended / no-show /
  cancel / reschedule). *Demo: an I-9 desk member sets Wednesday hours and sees tomorrow's roster.*
- **5b, candidate booking:** ✅ 2026-10-24. Built as described. Each step with an appointment shows it in the
  checklist with **Book**, **Change time** and **Cancel appointment** exactly as the server allows. A candidate books only a
  step they've been asked to do (action needed) or one already booked (waiting); staff can still book ahead. So New
  Employee Welcome, which no message starts, is bookable by the candidate once L&D or the specialist starts the step
  (open question: may candidates choose a session?). A remote-I-9 candidate isn't offered the in-person desk until 5c
  adds the choice. Every booking, change and cancellation emails the candidate (signed and replied to by their
  specialist, whoever made the change) after it is saved; a failed email leaves the booking and says so in the history
  and to staff. Booked/changed carry an `.ics` REQUEST and cancelled a CANCEL, with one event per candidate and
  appointment type, so a change moves the event already in their calendar. Reminders go from 9 a.m. Pacific the day
  before, once, skipping anything booked that day; the job runs on one instance (Phase 7: claim rows first if that
  changes). Verified on the sandbox: Casey moved fingerprinting to Campus PD in the portal, it appeared on Schedule
  User's roster and left Health PD's, and Mailpit got the change email and invitation; cancel sent a CANCEL with a
  higher sequence; a reminder went out from the job; another candidate's appointment 404s; a candidate cookie gets 401
  from staff booking. Not yet checked: how Outlook and Gmail treat the invitation (accept/decline buttons, updates).
  Planned: pick a location and time in the portal within the window; change or cancel until the
  cutoff; confirmation, change and cancellation emails with an .ics invite; day-before reminders from a background
  job. *Demo: a candidate books fingerprinting and it appears on the police department's roster.*
- **5c, the other answers:** ✅ 2026-10-26. Built as described. On the I-9 step (after 11A) the candidate books the
  in-person desk or chooses **Verify remotely**. Remote sets the I-9 method, moves step 8 to waiting, starts step 9 and
  sends 11B automatically, signed by their specialist; the step starts even if the email can't go, and the history says
  why. Once remote is chosen, or an in-person appointment is booked, the choice closes; switching back is for staff (the
  candidate's edit form). Step 9 asks for the representative's full name and email address; step 11 asks for the ATI
  number once the fingerprinting appointment has started or been marked attended. Each answer moves its step to waiting,
  can be corrected while the step is open, and is recorded in the history; staff see it on the candidate page and can
  enter it themselves from an emailed reply (the edit form, same checks). ATI numbers are stored upper case without
  spaces; only letters, digits and hyphens are required, since the format isn't confirmed. Version 2 of 11A, 11B and 12
  points to the checklist (see *Copy to review*); seeding adds a version only when the database has nothing at or above
  it, so wording edited in the app is never shadowed. Verified on the sandbox: Morgan got 11A v2, chose remote in the
  portal, and 11B v2 arrived with Reply-To the specialist while the representative step opened with its due date; Casey
  entered a representative (bad input → field errors) and, after the appointment, an ATI number (refused before it,
  `sbx 12345` stored as `SBX12345`); staff saw both and the history. Not decided: whether staff should be emailed when a
  candidate answers (today the pipeline shows the step waiting).
  Planned: the I-9 choice (in person → book; remote → representative's name and email, which
  starts 11B), the ATI number after fingerprinting, and new template versions of 11A, 11B and 12 that point to the
  checklist.

**Phase 6: Admin.** ✅ 2026-10-26. `/api/admin` (role Admin, from `StaffMember.IsAdmin`) and an **Admin** tab:
- **Templates (6a):** every message at its sending version; the editor changes subject and sections (each with its
  audience), previews unsaved wording for any candidate (appointment emails with an example appointment), and saves
  the next version. Saving refuses unknown merge fields, appointment fields outside appointment emails, any field in a
  vendor's wording, leftover `[placeholders]`, malformed `{{ fields }}`, `<!-- if: -->` markers, and an edit started
  from an older version. Earlier versions stay readable and can be reopened as the start of a new one.
- **Settings (6b):** the process settings (checked by kind; a confirmed setting needs a value), each step's deadline
  rule (anchor, amount, unit; start-date rules count days or weeks), and holidays. A change that moves deadlines
  recomputes every open due date staff didn't set by hand and writes each move into that candidate's history.
- **Staff (6c):** the sign-in whitelist: name, email, phone, role (including schedule-only team member), admin, active,
  and schedule teams. An admin can't remove their own access; a role change waits until no candidate names the person
  in the old role; deactivating says how many candidates still need reassigning. Teams and their locations are seeded,
  not created here.
- **History:** every change to shared data, with who made it.
Verified on the sandbox: Schedule User and Basic User get 403 from `/api/admin`; message 9 v2 saved and became what
staff preview; a stale save 409s; clearance days 6 → 8 moved four of Casey's due dates (each in Casey's history); drug
screen → 72 hours and a holiday add/remove recomputed Morgan's; adding Schedule User to Employee Health showed that
location on their next request, and deactivating them gave 403 until restored. Not built: appointment types (window,
cutoff, length) and locations are still edited only in seed data; the Schedule's own pages handle hours and sessions.
Planned: template editor (versioned, with merge-field validation), settings and holiday
calendar, staff user management.

**Redesign: groups, stages, one place** (decided 2026-10-26, after Phase 6). The new employee gets one place to do
every task, in order, even when a task sends them out to another app; staff work by group.
- **Groups own the steps.** Recruiter (Talent Acquisition, with the recruiting assistants): 1–4. Service Channel: 5–14,
  through "cleared to start", then hands the new employee back to Unit HR. Unit HR (with L&D): 15, the first day and
  New Employee Welcome. Each candidate has one person per group. Schedule teams (I-9 desk, police, OEHC) stay as they are.
- **Stages open by themselves.** Steps that can run together form a stage; when the last step of a stage completes,
  the next stage opens on its own, so the new employee comes back to their checklist and does the next thing.
- **Emails wait for inactivity.** A newly opened task appears in the checklist at once. Its email goes only if the
  new employee hasn't used the checklist for an hour (a setting), and not at all if they've already done it.
- **The new employee sees only what's needed now,** in order: the current stage's tasks, then what's done. Later
  stages stay hidden until they open.
- **Staff see a timeline by group:** each candidate's steps clustered by the responsible group, each one expanding.

Steps: (1) ✅ sandbox sign-in names the groups and adds **New employee view**, and Admin → **Emulate** shows the app as
any staff member or any new employee's checklist (read-only: every change under `/api` is refused while it lasts, and
each view is in the history); (2) ✅ roles become Recruiter, Service
Channel and Unit HR (migration `StaffGroups` moves recruiting assistants into the recruiter slot and renames stored
roles; message copy still says "onboarding specialist" and its fields keep their names); (3) ✅ stages and automatic
opening (`JourneyFlow`: on candidate creation, edits, step moves and attendance; stages are 1 references, 2
qualifications, 3 offer, 4 employee data form, 5 all of pre-employment, 6 cleared, 7 New Employee Welcome); (4) ✅
emails held until the new employee has been away for `NewTaskEmailDelayMinutes` (60): one step that opened alone
sends its own message, several send one "new steps" email listing them, and a task done first is never emailed
(`NewTaskEmails`, run every 5 minutes by `NewTaskEmailJob`); (5) ✅ the portal pulls them along: a home page led by
**Your next step** with a "Click here to …" button, then what else is open, what we're working on and what's done
(later stages hidden, no vendor names), and a page per task showing that step's email text (or a summary) with the way
to do it: booking, the I-9 choice, the representative and the ATI number happen right there; a task done in another
system opens a stand-in and **I've done this** (it moves to waiting; staff confirm). Finishing returns home with
"Done: … Next up: …". Verified on the sandbox: completing Taylor's employee data form opened stage 5, and with the
delay at 0 one "Next steps" email listed its three tasks; (6) the staff timeline by group.

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
- Ours: should staff be emailed when a candidate answers in the portal (representative, ATI number), or is the
  pipeline's "waiting" enough? Repo name/owner; whether a later version should accept document uploads (would break invariant #1, so it needs a data decision).
