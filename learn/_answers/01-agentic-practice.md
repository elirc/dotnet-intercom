SEALED: attempt first. Finish the drill, including your own review, before reading its rubric.

# Agentic practice: rubrics

The root causes live in `training/_answers/incident-00N.md`. This file grades **how you directed and verified the agent**, not only whether the bug got fixed. Each drill is scored Basic / Solid / Strong.

## Drill A: INC-004
**A strong spec contains:**
- The root cause in one sentence: "authorization was replaced by a message filter; agent replies pass the `OR` for any conversation".
- The explicit contract: `GET …/messages` → 403, `GET …?after=` → 403, `POST …/read` → 403, `POST /widget/messages {B}` → 404 (current, `SendVisitorMessage.cs:61-62`), `JoinConversation(B)` → `HubException`.
- `Typing(B)` named, either as "must reject" or as "flag as out of scope". Today it has no ownership check (`VisitorHub.cs:48-58`), so a visitor can fire typing events into any conversation's agent group.
- A "must not touch" list: `RealtimeNotifier`, DTOs.

**Findings a good adversarial review makes:**
- The fix is a filter (`m.Conversation.VisitorId == visitorId`) that returns `[]`. It's correct, but it hides probing. Did you decide 403 on purpose?
- There's a single negative test for the incident route only. The matrix must be table-driven so a new endpoint gets added to it.
- A test that asserts `NotEqual(200)` would pass on a 500.
- The agent says "integration tests pass" without Docker. Reject that claim.

**Scoring:**
- **Basic:** ownership restored, and one negative test.
- **Solid:** the full matrix, plus `Typing` handled or flagged.
- **Strong:** you also wrote the blast-radius action item (log `visitor_id` per request, `training/_answers/incident-004.md`), and you rejected at least one agent claim with evidence.

## Drill B: INC-003
**A strong spec contains:**
- A constant id `"sla-breach-check"`.
- A one-off cleanup of `sla-breach-check:*` ids. The `/hangfire` dashboard isn't reachable from a browser, so this means a script or an admin action.
- A set-based conditional transition (`UPDATE … SET Status=Breached WHERE Status=Running AND BreachAtUtc<=@now RETURNING Id`), notifying only the returned rows. A concurrency token on `SlaTimer` with per-timer conflict handling is an acceptable alternative.
- The reply path's `MarkMet` made conditional too.

**Findings a good review makes:**
- `[DisableConcurrentExecution]` on its own is belt-and-braces, not a fix.
- A C# `lock` or a `SemaphoreSlim` is per process, so it does nothing across 3 replicas.
- A test that calls the handler twice *sequentially* proves nothing. It needs two contexts loading the timer before either saves, or a real parallel run.
- `MarkMet` still ignores `BreachAtUtc` (`Sla.cs:31-40`). Even if the agent didn't fix it, it should have been raised.

**Scoring:**
- **Basic:** the constant id.
- **Solid:** + the DB-level idempotent sweep, with a two-context test.
- **Strong:** + the cleanup plan, the reply-vs-sweep lost update, and an alert (notifications per breached timer > 1, or the size of the recurring-job set).

## Drill C: INC-005
**A strong spec contains:**
- A projection instead of `Include`: `UnreadCount` as a correlated `COUNT` in `Select`.
- A partial index `ON "Messages"("ConversationId") WHERE "SenderType"=0 AND "ReadByAgentAtUtc" IS NULL`, created `CONCURRENTLY` with a written deployment path (there are no migrations: `EnsureCreated`).
- A SQL-capture test.
- A list of the other `ConversationDto.From` callers (`GetConversation`, the assign/state-change handlers, `SendAgentMessage`, `CheckSlaBreaches`, `SendVisitorMessage`), each marked "count must come from the query, or the field is omitted".

**Findings a good review makes:**
- The agent added an index while the cartesian `Include` remained.
- The count is still computed in `From()` from a navigation, so it reports 0 wherever `Messages` isn't loaded.
- A "p95 improved" claim with no plan and no test.
- The client multiplier (`Inbox.tsx:40-48` invalidating on every event) was not mentioned. It's fine to leave it out of scope, but it must be named.

**Scoring:**
- **Basic:** `Include` removed.
- **Solid:** + the projection, and a SQL-capture test that fails on the incident branch.
- **Strong:** + the other callers fixed or ticketed, the partial index with a deployment path, and the multiplier ticketed.

## Drill D: characterization first
**A strong result:**
- The diff touches `tests/` only.
- The tests pin these behaviors:
  - empty windows → open (`OfficeHours.cs:16-19`)
  - unknown time zone → silent UTC (`:26-29`)
  - overnight window → never open (`:33`)
  - window end is exclusive (`time < End`)
  - the router falls back to the fallback team outside hours, then to the oldest team if there's no fallback team (`ConversationRouter.cs:43-51`)
- The agent returns a question list: "overnight windows never match — intended?", "unknown TZ silently UTC — should the J3 endpoint reject it?", "no fallback team → oldest team — intended?".

**Findings a good review makes:**
- Any change under `src/` gets rejected, even a "tiny fix".
- A test named `OvernightWindow_IsOpen` that asserts `true` is a spec for J3, not a characterization. Move it to J3's PR.
- The router tests must use a fake `IClock` and a fake `IPresenceService`. Otherwise they depend on the time of day, like `HappyPathTests` (review finding).

**Scoring:**
- **Basic:** green tests.
- **Solid:** test-only diff, with the three `IsOpenAt` edge cases pinned.
- **Strong:** + router fallback pinned with fakes, and a question list you then turned into J3 acceptance criteria.

## Cross-drill red flags (automatic "Basic" at best)
- You pasted the incident brief to the agent and asked it to "find and fix the bug". That skips the diagnosis, which is the part you're here to learn.
- You accepted "all tests pass" without seeing the output.
- You didn't open `git diff main --stat` before approving.
