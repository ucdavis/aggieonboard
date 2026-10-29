# Where we are — 2026-10-28

A status note to pick up from. The plan with full detail is [`PLAN.md`](PLAN.md); the agent guide is
[`../AGENTS.md`](../AGENTS.md). Everything below is committed and pushed to `main` on
github.com/ucdavis/aggieonboard (last feature commit `dc74eb3`).

## In one paragraph

AggieOnboard is a working prototype of one place for a UC Davis Health new employee's onboarding,
from reference check to New Employee Welcome (15 steps). The new employee gets a link and is pulled
along one task at a time; staff in three groups (Recruiter, Service Channel, Unit HR) see their
work by person and each candidate as a timeline. It runs locally on fictional data. Hosting
(Phase 7) is deliberately deferred: we tweak locally until it's ready.

## What's built

**For the new employee (portal)**
- Emailed one-time link sign-in; a session sees only that one person.
- Home page: welcome, progress ("4 of 11 done"), **Your next step** with a "Click here to …"
  button, anything else open now, what staff are working on, and what's done.
- Only open and finished tasks show. When a stage finishes, the next opens on its own.
- Each task page shows the text of our email for it, filled in, then the way to do it: book an
  appointment, choose in-person or remote I-9, name a remote representative, enter the ATI number,
  or (for tasks done in another system today) a stand-in with "I've done this" / "Not yet".
- Finishing a task returns them home: "Done: X. Next up: Y."
- Emails for newly opened tasks wait until they've been away from the portal for an hour
  (a setting), then send one email listing what's new.

**For staff**
- **My work**: a group's open steps across all candidates, clustered by the responsible person
  (you first), split into "Needs [group]" and "With new employees" (overdue first). Each item opens
  the candidate's timeline at that step.
- **Candidate page**: one timeline per group with who's responsible and "n of m done"; the current
  group is open, steps waiting on staff open themselves, skipped steps are tucked away.
- Preview and send each step's approved message; nothing sends with a blank field; every sent
  message is kept exactly as it went out.
- **Schedule**: teams, availability, booking for candidates, day rosters, calendar invites and
  day-before reminders.

**Admin**
- Templates (every edit is a new version, with checks), settings, step deadlines, holidays,
  staff and roles, history of shared changes.
- **Emulate**: see the app as any staff member or any new employee, read-only.

**Groups** (decided 2026-10-26)
- Recruiter: steps 1–4 (the recruiting assistant folded in).
- Service Channel: steps 5–14, and makes sure all is complete before handing back.
- Unit HR: step 15, New Employee Welcome (L&D folded in).

## Try it

```sh
export SANDBOX_PROJECT=aggieonboard-sandbox SANDBOX_PORT=5280 SANDBOX_MAIL_PORT=8025
docker compose -p aggieonboard-sandbox -f .devcontainer/docker-compose.sandbox.yml up --build --wait
```

- App: http://localhost:5280/login. Sign in as Chris Alvarez (Recruiter), Pat Kim (Service
  Channel), Robin Hale (Unit HR), Sample User (admin), or **New employee view** for the portal.
- Sent email: http://localhost:8025 (Mailpit).
- Stop: same command with `down --volumes` in place of `up --build --wait`.

Tests at last run: 252 server, 54 client, all passing; type check and lint clean.

## Not done yet / open

- **Not yet viewed in a real browser**: the portal redesign, My work and the timeline were checked
  through the API and tests only. First thing to do: click through as each group and as a new employee.
- **Booked appointments show as "Needs Service Channel"** on My work even though there's nothing to
  do until the appointment. Possible fix: a separate "Booked" group.
- **"I've done this"** marks the task done for the new employee; whether staff should confirm it is open.
- **Stage grouping** (which steps open together) was my assumption; worth checking with Service Channel.
- **Message copy still names vendors** (e.g. "I-9 Tracker Complete"). Copy belongs to communications;
  new wording I drafted is listed under "Copy to review" in PLAN.md.
- Should staff be emailed when a new employee answers something in the portal?
- May new employees choose their own New Employee Welcome session?
- The ~30 unanswered process questions from the audit are still settings with the document's values.

## Next

1. Walk through each role in the browser and improve: Recruiter, Service Channel, Unit HR, new employee.
2. Later: real connections to the outside systems (SkillSurvey, Universal, Quest, I-9 Tracker,
   DocuSign), or modules we build ourselves, so tasks finish without "I've done this".
3. Phase 7, hosting: Entra app registration, SMTP relay, turn on deploy-on-push, security review
   (incl. anti-forgery), accessibility, invite testing in Outlook/Gmail, background jobs on one instance.
