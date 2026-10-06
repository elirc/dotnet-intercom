# Learn NetIntercom: study guide (B08)

This is the entry point for a junior engineer. `training/` holds the lab: tickets, incidents, a blind review and an interview kit. This folder tells you **in what order to study it, what to do yourself and what to hand to a coding agent**. Answers are sealed in `learn/_answers/` and `training/_answers/`. Open one only after you've written your own answer.

## What this app is and why it's in the portfolio
NetIntercom is an Intercom-style support inbox. It's a .NET 8 API (`src/NetIntercom.Api`) with MediatR handlers (`src/NetIntercom.Application`), EF Core on Postgres (`src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs`), SignalR hubs for agents and visitors (`src/NetIntercom.Api/Realtime/`), Hangfire SLA sweeps (`src/NetIntercom.Application/Sla/CheckSlaBreaches.cs`), a React agent inbox (`frontend/inbox`) and an embeddable widget (`frontend/widget`).
It's the portfolio's only **realtime + multi-replica background job** app. It's where you learn that "it works on one server" and "it works on three" are different claims.

## Known state of main
These facts come from an earlier review report (B01) that is not part of this repo. Nothing was re-run in this pass:
- `main` builds (`dotnet build NetIntercom.sln`, 0 errors). Unit tests pass 27/27 with `DOTNET_ROLL_FORWARD=Major`, because the review machine had only the .NET 10 runtime (the projects target `net8.0`; without a .NET 8 runtime you need the same flag).
- The integration tests (`tests/NetIntercom.IntegrationTests`) need Docker/Testcontainers. **They were never run** in B01. Treat any "integration passes" claim as UNVERIFIED until you run them yourself.
- Real defects on `main` (don't fix them unless a ticket asks you to):
  - `Seed:Enabled=true` sits in the **base** `src/NetIntercom.Api/appsettings.json:16-18`, so every environment seeds a known admin.
  - The widget never rejoins its `vconv:` SignalR group after a reconnect (`frontend/widget/src/Widget.tsx:52-58`). Ticket M3 covers it.
  - `docs/architecture-review.md` §2.3 is **wrong** about concurrent SLA sweeps. That doc is a gated answer key: don't open it before `training/review/EXERCISE.md`.
- All 5 incident branches build. The existing test suite catches **none** of them, and that's the point.
- **Status 2026-10-06: the incident branches were never pushed.** `git ls-remote origin` shows only `main`, so a fresh clone can't check them out and the `git worktree add` commands below fail. See the status note in `training/README.md` for how to work the incidents from their briefs in the meantime.
- **Status 2026-10-06:** the concept-page links to `opusorganize/apprenticeship/curriculum/*.md` (here, in `00-onramp.md`, `01-agentic-practice.md` and ladder M5) point outside this repo and don't resolve from a clone. The page titles name the concept (idempotency, outbox, caching, query plans and so on), so look the topic up in any standard reference instead.

## Prerequisites
Do these first. Each one takes 30–60 minutes.
1. **The request pipeline:** controller → MediatR → handler. Read `src/NetIntercom.Api/Controllers/WidgetController.cs` and `src/NetIntercom.Application/Common/Behaviors.cs`, and be able to say where validation runs. Do netdrop's `learn/` (the `elirc/dotnet-drop` repo) first if you haven't: it teaches the same Api/Application/Domain/Infrastructure layering on a simpler app.
2. **EF Core queries, indexes, `Include`:** read [10 query plans](../../opusorganize/apprenticeship/curriculum/10-query-plans-and-indexes.md) and `AppDbContext.cs:72-97`.
3. **Check-then-act races and optimistic concurrency** (the `xmin` row version at `AppDbContext.cs:79`): read [03 concurrency races](../../opusorganize/apprenticeship/curriculum/03-concurrency-races.md).
4. **SignalR connections and groups:** read [08 realtime fan-out](../../opusorganize/apprenticeship/curriculum/08-realtime-fanout.md), then `src/NetIntercom.Api/Realtime/VisitorHub.cs`.
5. **Authorization is not a filter:** read the ownership check at `src/NetIntercom.Application/Widget/WidgetQueries.cs:25-30`, then [05 verify before trust](../../opusorganize/apprenticeship/curriculum/05-verify-before-trust.md).
6. **For the mid rungs:** [02 idempotency](../../opusorganize/apprenticeship/curriculum/02-idempotency.md), [01 outbox](../../opusorganize/apprenticeship/curriculum/01-transactional-outbox.md) and [06 caching](../../opusorganize/apprenticeship/curriculum/06-caching-correctness.md).

## Study route
Twelve sessions. A ticket that takes longer than one sitting is split, so stop at the exit criterion and resume next time. Check out incident branches **in a worktree**, never on your main tree (once they're pushed; see "Known state of main"): `git worktree add ../ni-inc-004 training/incidents/004-widget-transcript-exposure`.

### Session 1 — Read the system without running it (60 min)
- **Files/branches:** `README.md`, `training/navigation/ARCHITECTURE.md`, `training/navigation/ONBOARDING.md`, `learn/00-onramp.md` exercises 1–4.
- **Do by hand:** read everything with the code open next to it. Do on-ramp 1–4 on paper.
- **Delegate, then verify:** you may ask an agent to "list every file touched when a visitor sends a message". Then open each file it names and confirm the call actually happens there. Cross off any file it named that isn't really in the path.
- **Exit criterion:** without notes, you can draw the path visitor send → DB → agent inbox and widget, naming `WidgetController`, `SendVisitorMessageHandler`, `ConversationRouter`, `RealtimeNotifier` and the two SignalR groups.

### Session 2 — Finish the on-ramp and write your first test (75 min)
- **Files/branches:** `learn/00-onramp.md` exercises 5–9, `tests/NetIntercom.UnitTests/RoutingAndOfficeHoursTests.cs`.
- **Do by hand:** the predictions (exercises 5, 6, 8 and 9) and the characterization test (exercise 7). Type the test yourself: this is the one time you shouldn't delegate it.
- **Delegate, then verify:** nothing. Afterwards you can ask an agent to review your test for missing edge cases, then decide yourself which suggestions are real.
- **Exit criterion:** your new test passes on `main` (`DOTNET_ROLL_FORWARD=Major dotnet test tests/NetIntercom.UnitTests` shows 28 tests), and at least 7 of your 9 answers match `learn/_answers/00-onramp.md`.

### Session 3 — Blind code review (90 min)
- **Files/branches:** `training/review/EXERCISE.md`, then `training/review/COMPARE.md`.
- **Do by hand:** all of it. Reviewing is the skill that agents make *more* important, so never delegate it while you're learning.
- **Delegate, then verify:** after you've compared, ask an agent for its own review of the same scope. Score its findings against `training/_answers/review.md` the way you scored yours. Note what it missed and what it invented.
- **Exit criterion:** at least 8 findings with file:line evidence, and you can explain in two sentences why `docs/architecture-review.md` §2.3 is wrong (`training/_answers/review.md` R5).

### Session 4 — J1: two agents close the same conversation (90 min, may need 2 sittings)
- **Files/branches:** `training/ladder/J1-concurrent-close-409.md`, `src/NetIntercom.Domain/Entities/Conversation.cs:50-61`, `src/NetIntercom.Api/Infrastructure/ApiExceptionHandler.cs`.
- **Do by hand:** answer the ticket's "explain before touching" questions. Write the status-code contract: which exception maps to 409, and what the response body says.
- **Delegate, then verify:** the Testcontainers test scaffolding and the exception-handler mapping. Verify with `training/agentic/VERIFY_AI_OUTPUT.md` "Run" and "Check the claims".
- **Exit criterion:** a test that sends two concurrent close requests. It fails on `main` with a 500 and passes with a 409 after your fix. If Docker isn't available, write "integration UNVERIFIED" and show at least the unit-level mapping test passing.

### Session 5 — Security incidents 004 then 001 (90 min)
- **Files/branches:** `training/incidents/004-widget-transcript-exposure.md` and `001-widget-note-leak.md`, plus their branches (not on GitHub yet, see the status note under "Known state of main").
- **Do by hand:** the diagnosis. Find the line on the branch that causes the symptom, using `git diff main..<branch>`, before you open any answer. Without the branch: start from the code on `main` that the symptom points at (for 004, the ownership check in `src/NetIntercom.Application/Widget/WidgetQueries.cs:25-30`; for 001, the `if (!request.IsInternalNote)` guard around `LastMessagePreview` at `src/NetIntercom.Application/Conversations/SendAgentMessage.cs:60-64` and the visitor projection at `src/NetIntercom.Api/Realtime/RealtimeNotifier.cs:32-37`), and write down which change would produce the symptom.
- **Delegate, then verify:** after you've diagnosed 004, do drill A in `learn/01-agentic-practice.md`: the agent writes the negative-test matrix and the fix from *your* spec.
- **Exit criterion:** for each incident, a written one-paragraph root cause that matches `training/_answers/incident-00N.md`, plus the INC-004 negative-test matrix covering every widget endpoint and hub method.

### Session 6 — Performance incident 005 (75 min)
- **Files/branches:** `training/incidents/005-inbox-list-latency.md`, `src/NetIntercom.Application/Conversations/ListConversations.cs:28-33`, `frontend/inbox/src/pages/Inbox.tsx:40-48`.
- **Do by hand:** the row multiplication (task 1) and finding the client-side multiplier (task 2).
- **Delegate, then verify:** drill C in `learn/01-agentic-practice.md`.
- **Exit criterion:** you can explain, without notes, why the request rate stayed flat while DB CPU pinned, and your SQL-capture test fails on the incident branch.

### Session 7 — M3: widget stops receiving replies after a blip (90 min, may need 2 sittings)
- **Files/branches:** `training/ladder/M3-widget-reconnect-delivery.md`, `frontend/widget/src/Widget.tsx:34-63`, `src/NetIntercom.Api/Realtime/VisitorHub.cs:29-46`.
- **Do by hand:** the written reproduction, and the answers to the ticket's 5 questions, especially join-then-fetch ordering.
- **Delegate, then verify:** the Vitest fake-`HubConnection` test or the SignalR .NET client test. Check that the test really goes through a reconnect: new connection id, `StopAsync`/`StartAsync`.
- **Exit criterion:** a test that fails on `main` and passes after the fix. You can name both root causes (`training/_answers/ladder-M3.md`).

### Session 8 — The agentic workflow on M2: idempotent visitor send (90 min, then half a day)
- **Files/branches:** `training/agentic/WORKFLOW.md`, `training/ladder/M2-idempotent-visitor-send.md`, `src/NetIntercom.Application/Widget/SendVisitorMessage.cs:47-115`.
- **Do by hand:** Step 1 (the spec), Step 4 (which reviewer findings you accept or reject), Step 6 (the explanation).
- **Delegate, then verify:** Steps 2–3 (implementer and adversarial reviewer), then Step 5 against `VERIFY_AI_OUTPUT.md`.
- **Exit criterion:** your explanation scores "solid" or better against `training/_answers/agentic-grading.md`, and a concurrent-duplicate test shows exactly one message row.

### Session 9 — Multi-replica incidents 002 and 003 (90 min)
- **Files/branches:** `training/incidents/002-unassigned-queue-spike.md`, `003-sla-escalation-storm.md`, `src/NetIntercom.Api/Realtime/PresenceTracker.cs:76-90`, `src/NetIntercom.Api/Program.cs:190`.
- **Do by hand:** both diagnoses. For 003, explain why `SlaTimer.MarkBreached`'s guard doesn't stop duplicates across `DbContext`s.
- **Delegate, then verify:** drill B in `learn/01-agentic-practice.md`.
- **Exit criterion:** you can explain without notes why the duplicate count grows with deploys, and your fix notifies only for rows that *this* sweep transitioned.

### Session 10 — Trade-offs, the change request and caching (90 min)
- **Files/branches:** `training/navigation/TRADEOFFS.md`, `training/navigation/CHANGE_REQUEST.md`, `training/ladder/M5-cache-routing-rules.md` (new in B08, fills the caching gap).
- **Do by hand:** the change-request placement plan, and the M5 "measure first" plus staleness-bound decision.
- **Delegate, then verify:** drill D in `learn/01-agentic-practice.md` (characterization-first before touching routing and office hours).
- **Exit criterion:** you've written the M5 staleness bound and invalidation owner in one sentence each, and they survive a comparison with `training/_answers/ladder-M5.md`.

### Session 11 — Senior design: S1 three replicas (90 min, design doc only)
- **Files/branches:** `training/ladder/S1-scale-out-three-replicas.md` (then S3, S2 later).
- **Do by hand:** the whole design doc. Agents are allowed only as a critic afterwards.
- **Delegate, then verify:** paste your doc to a fresh agent session and ask for "the three failure modes this design doesn't handle". Accept only the ones you can tie to a file in this repo.
- **Exit criterion:** your doc addresses presence, SignalR backplane, recurring jobs and rollback, and matches the headline decisions in `training/_answers/ladder-S1.md`.

### Session 12 — Interview kit (90 min)
- **Files/branches:** `training/interview/TECHNICAL_QUESTIONS.md` → `FLASHCARDS.md` → `PROJECT_STORIES.md` → `SYSTEM_DESIGN.md` → `MOCK_DEFENSE.md`.
- **Do by hand:** fill in `PROJECT_STORIES.md` from what *you* actually did in sessions 4–10. Answer the questions out loud.
- **Delegate, then verify:** an agent can play the interviewer in `MOCK_DEFENSE.md`. Grade yourself with its rubric, not the agent's praise.
- **Exit criterion:** at least 12/18 on the mock-defense rubric, and one STAR story per incident class: security, performance, multi-replica.

## How you know you're done here
- You can explain, without notes, the three "one server vs three" traps: in-memory presence, per-connection SignalR groups and recurring job ids.
- You've shipped at least one ticket through spec → agent → adversarial review → your own verification, and rejected at least one agent claim with evidence.
- You have one failing-then-passing test for a security bug (INC-004) and one for a realtime bug (M3).

## Next repo
**modular-monolith-with-ddd** (kamilgrzybek/modular-monolith-with-ddd on GitHub, a read-only study repo). Its outbox/inbox between modules is the grown-up version of what NetIntercom's S3 asks you to design. After that comes eShop, then gitvg/atlas (B02).


## Extended current-source course

Continue with the [extended course](extended-course/README.md), which adds source-grounded lessons, independent predictions, and separate solutions. Its README records the current installment and evidence boundaries.
