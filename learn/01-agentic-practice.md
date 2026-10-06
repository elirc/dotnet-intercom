# Agentic practice: you direct, the agent types, you verify

Each drill turns an existing `training/` incident or ticket into practice at directing a coding agent. The loop is always the same:
1. **Diagnose by hand** first, using the incident brief. No agent, no answer file.
2. **Write the spec yourself**, using the template below.
3. **The agent implements** in a worktree (`git worktree add ../ni-drill-X <branch>`).
4. **You review adversarially**, using the checklist at the bottom, then compare with the sealed rubric in [`_answers/01-agentic-practice.md`](_answers/01-agentic-practice.md).

> **Status 2026-10-06:** drills A–C use `training/incidents/*` branches as their **Base**, and those branches aren't on GitHub (`git ls-remote origin` lists only `main`). From a clone, finish your hand diagnosis first, then re-create the incident change on a branch of your own (the answer file's "Root cause" section describes the diff), and run the drill against that branch. Drill D (base `main`) works as written. The concept-page links into `opusorganize/` point outside this repo.

This extends `training/agentic/WORKFLOW.md` (the full M2 loop). Do that one in Session 8; these drills are shorter reps. Background reading: [05 verify before trust](../../opusorganize/apprenticeship/curriculum/05-verify-before-trust.md).

## Spec template (keep it under one page)
```
Goal (one sentence, user-visible):
Root cause (your diagnosis, with file:line):
Behavior contract (inputs → outputs, status codes, what must NOT change):
Files the agent may touch:            Files it must NOT touch:
Tests required (name + the assertion that fails before the fix):
Out of scope:
Report back: files changed + why, real test output pasted, anything UNVERIFIED.
```

---

## Drill A: INC-004 widget transcript exposure (security, 60–90 min)
**Base:** `training/incidents/004-widget-transcript-exposure`. **Prereq:** you've diagnosed it by hand (Session 5).
- **Your spec must:**
  - Restore the ownership check.
  - Demand a **table-driven negative-test matrix**: every widget HTTP endpoint (`WidgetController.cs`) and every `VisitorHub` method, each called with visitor A's token on visitor B's conversation.
  - Say which status each one should return.
- **Trap to watch for:** agents like to "fix" IDORs by adding a filter, which returns `[]` instead of a refusal. Or they write one negative test for the endpoint in the incident and call it done.
- **Adversarial questions:**
  - Does the matrix include `Typing` (`VisitorHub.cs:48-58`)?
  - Did the agent notice that it has *no* ownership check today? Did it fix that, or flag it as out of scope?
  - Is every test asserting a status code, not just "not 200"?
- **Exit:** the matrix fails on the incident branch and passes after the fix. If Docker isn't available for the integration tests, write "integration UNVERIFIED".

## Drill B: INC-003 SLA escalation storm (multi-replica, 60–90 min)
**Base:** `training/incidents/003-sla-escalation-storm`. **Prereq:** your written diagnosis.
- **Your spec must:**
  - Restore a constant recurring-job id (compare `main`'s `Program.cs:190`).
  - Make the sweep **idempotent across overlapping runs**, so a run notifies only for timers *it* transitioned.
  - Include a cleanup plan for the stale per-replica job ids.
- **Trap to watch for:** the agent adds `[DisableConcurrentExecution]` or a `lock`, and claims the race is gone. A `lock` is per process. The attribute lowers the odds but still leaves the reply-vs-sweep lost update.
- **Adversarial questions:**
  - Where is the database-level guard? It should be a conditional `UPDATE … WHERE Status = Running` or a concurrency token on `SlaTimer` (`Sla.cs`).
  - Does the test run two sweeps against the same DB, or just one sweep twice in sequence?
- **Exit:** a test with two overlapping sweeps shows exactly one `slaBreached` notification per timer.

## Drill C: INC-005 inbox list latency (performance, 60 min)
**Base:** `training/incidents/005-inbox-list-latency`. Read [10 query plans](../../opusorganize/apprenticeship/curriculum/10-query-plans-and-indexes.md) first.
- **Your spec must:**
  - Keep the `unreadCount` badge.
  - Forbid any collection `Include` of `Messages` in `ListConversations.cs`.
  - Require a **SQL-capture test** (a `DbCommandInterceptor` or `LogTo`) that asserts no `JOIN "Messages"` and a bounded command count.
  - Name every other handler that builds a `ConversationDto` and say what `unreadCount` should be there.
- **Trap to watch for:** "made it faster" by adding an index, while the cartesian `Include` stays. Or a correct list query, while `ConversationDto.From` still computes the count from a navigation and silently reports 0 elsewhere.
- **Adversarial questions:**
  - Which handlers call `ConversationDto.From`? Grep for it yourself.
  - Does the agent's before/after claim come from a real plan or a real test, or from its own assertion?
- **Exit:** the SQL-capture test fails on the incident branch and passes on the fix.

## Drill D: characterization-first before touching office hours (legacy discipline, 45–60 min)
**Base:** `main`. **Ticket context:** `training/ladder/J3-office-hours-endpoint.md`. Read [04 characterization tests](../../opusorganize/apprenticeship/curriculum/04-characterization-tests.md).
- **Your spec (phase 1 only):** "Write characterization tests for `OfficeHours.IsOpenAt` and `ConversationRouter.RouteAsync` office-hours fallback. **Change no production code.** Tests must assert what the code does today, even when that looks wrong. List every behavior you think is a bug separately, as a question for me."
- **Trap to watch for:** the agent "helpfully" makes overnight windows work (`OfficeHours.cs:33`), or asserts the *desired* behavior and edits the code to match. That smuggles a behavior change into a test PR. It may also swallow the unknown-time-zone fallback (`:26-29`) without flagging it.
- **Adversarial questions:**
  - Does `git diff main --stat` show only files under `tests/`?
  - Is there a test for the unknown time zone, the empty windows and the overnight window?
  - Did the agent list bugs as *questions* rather than fixing them?
- **Exit:** a test-only diff that is green on `main`, plus a written list of at least 2 behavior questions for J3.

---

## Verification checklist: before trusting agent output in NetIntercom
Use this together with `training/agentic/VERIFY_AI_OUTPUT.md` (the full list). These are the NetIntercom-specific spots:
- [ ] **Build and tests are real:** you ran `dotnet build NetIntercom.sln` and `DOTNET_ROLL_FORWARD=Major dotnet test tests/NetIntercom.UnitTests` yourself. The test count went up by exactly the number of tests claimed. Integration tests: either you ran them with Docker, or they're marked UNVERIFIED.
- [ ] **Visitor surfaces:** any new or changed field in `ConversationDto`/`MessageDto` (`Application/Dtos.cs`). Check all three visitor paths: `RealtimeNotifier.cs:20-38`, `POST /widget/session` (`StartConversation.cs`) and `POST /widget/messages` (`WidgetController.cs:39-46`). Internal notes must never reach them (INC-001).
- [ ] **Ownership:** every widget handler or hub method re-checks `VisitorId` with `AnyAsync` (`WidgetQueries.cs:25-30`). It must not be folded into a filter (INC-004).
- [ ] **Concurrency:** any "safe under concurrency" claim points at a DB constraint, a conditional update or the `xmin` token (`AppDbContext.cs:79`). An in-memory guard like `SlaTimer.MarkBreached` or `Conversation.Close` protects nothing across `DbContext`s.
- [ ] **Multi-replica:** nothing new relies on `PresenceTracker` (per process), a `static`/singleton cache or a machine-specific recurring-job id (`Program.cs:190`).
- [ ] **Schema:** `EnsureCreated` never alters an existing DB (`Program.cs:150-157`). Any new column or index comes with a written deployment path.
- [ ] **Config:** no new always-on setting in the base `appsettings.json`. `Seed:Enabled=true` there is already a defect.
- [ ] **Fake behavior:** no `catch {}`, no `.catch(() => {})` added in `frontend/`, no `Task.Delay` race "fixes", no tests that mock `IAppDbContext` to "prove" a DB constraint.
- [ ] **Claims map to lines:** for each sentence in the agent's summary, you found the line that makes it true.
