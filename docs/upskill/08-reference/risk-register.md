# Risk Register

Synthetic repos look clean and hide real defects; this hunts for them deliberately. Each row: evidence (anchored), impact, likelihood, a test that would prove it, a fix sketch, and **confidence** (Confirmed by reading = the code demonstrably does this; Hypothesis = needs a running system to be sure). Nothing is called a "bug" without proof — unproven items say *investigate*.

Confidence legend: **C** = confirmed by reading the code; **H** = hypothesis (plausible, not executed).

---

## R1 — Visitor identity can be spoofed via `externalId` (security, high)

**Evidence:** [`StartWidgetSessionHandler.Handle`](../../../src/NetIntercom.Application/Widget/StartConversation.cs#L51-L65) looks up a visitor by `ExternalId` from the **request body** and then issues a visitor token for whatever it finds/creates; the session endpoint is `[AllowAnonymous]` ([WidgetController#L33-L35](../../../src/NetIntercom.Api/Controllers/WidgetController.cs#L33-L35)). The host site is supposed to set `externalId` to "their own user id" ([Visitor.ExternalId comment](../../../src/NetIntercom.Domain/Entities/Visitor.cs#L10-L11)), but nothing authenticates that claim.
**Impact:** anyone who learns/guesses another user's `externalId` resumes that visitor's identity and full conversation history. Account-history takeover.
**Likelihood:** High where `externalId` is a sequential/email-derived value.
**Suggested test:** integration — start a session with `externalId: "user-1"`, write a message; from a fresh client start a session with the same `externalId`; assert it should *not* return the first visitor's conversation without a verifying signature.
**Suggested fix:** require a host-signed token (HMAC of `externalId` with a per-tenant secret) for identified sessions; treat unsigned sessions as anonymous-only.
**Confidence: C** (the data flow is unambiguous; "exploitable" depends on deployment, hence the test).

## R2 — Any agent can act on any conversation (authorization, medium)

**Evidence:** [`ConversationsController`](../../../src/NetIntercom.Api/Controllers/ConversationsController.cs#L12-L16) requires only the `Agent` role; no handler scopes by team/assignment ([SendAgentMessageHandler](../../../src/NetIntercom.Application/Conversations/SendAgentMessage.cs#L36-L73) loads by id alone).
**Impact:** a Billing agent can read/reply/close any Support conversation; no audit of cross-team access.
**Likelihood:** Medium — likely *intended* (whole-inbox model) but undocumented and untested.
**Suggested test:** decide the policy first; if scoped, an integration test that an agent not on the team gets 403.
**Suggested fix:** if scoping is wanted, a resource-based authorization handler keyed on team membership; otherwise document "all agents see all conversations" explicitly.
**Confidence: C** (behavior), **investigate** (intent).

## R3 — Canned replies: no team-membership check (authorization, medium)

**Evidence:** [`SaveCannedReplyHandler`](../../../src/NetIntercom.Application/CannedReplies/CannedReplies.cs#L71-L97) lets any agent create/edit a *team* reply for any `TeamId`; only personal-reply ownership is checked (L78-81). [`DeleteCannedReplyHandler`](../../../src/NetIntercom.Application/CannedReplies/CannedReplies.cs#L108-L120) lets any admin (or owner) delete, but team-membership is never consulted.
**Impact:** agents inject/alter canned content for teams they don't belong to (placeholders render server-side, so this is also a minor content-injection vector into other teams' workflows).
**Likelihood:** Medium.
**Suggested test:** agent on Team A edits a Team B reply → expect 403.
**Suggested fix:** verify `request.AgentUserId` is a member of `request.TeamId` for non-personal replies.
**Confidence: C.**

## R4 — `VisitorHub.Typing` skips the ownership check (authorization, medium)

**Evidence:** [`VisitorHub.Typing`](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L48-L58) broadcasts a typing event to `conv:{conversationId}` for *any* conversation id the caller supplies — unlike `JoinConversation` ([L37-L46](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L37-L46)) which verifies `VisitorId` ownership.
**Impact:** a visitor can spoof "visitor is typing" into any conversation's agent view by guessing a conversation id; low-severity but a clear instance of the [Pattern 7](../03-architecture-and-patterns/05-pattern-catalog.md) gap.
**Likelihood:** Low (needs a valid visitor token + target id) but trivial once both held.
**Suggested test:** hub test — visitor A invokes `Typing(convB)`; assert no event reaches B's agents (after fix).
**Suggested fix:** same `AnyAsync` ownership check as `JoinConversation` before fan-out.
**Confidence: C.**

## R5 — Round-robin assignment race (correctness/concurrency, medium)

**Evidence:** [`PickRoundRobinAsync`](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L75-L86) reads `LastAssignedAtUtc`, picks the min, and the caller stamps + saves later; no lock, no unique constraint, no conflict handling.
**Impact:** concurrent first-messages assign the same "least recently assigned" agent, defeating load balancing.
**Likelihood:** Medium under bursty traffic; Low at trickle.
**Suggested test:** fire N concurrent first-messages; assert spread (red until fixed).
**Suggested fix:** `SELECT ... FOR UPDATE SKIP LOCKED` on candidate members, or an advisory lock per team, or accept-and-rebalance.
**Confidence: C** (race exists by construction), **H** (frequency in practice).

## R6 — Notify-after-save with no outbox (reliability, medium)

**Evidence:** every write does `SaveChangesAsync` then `await _notifier...` ([SendVisitorMessage#L107-L112](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L107-L112)); a SignalR failure throws *after* commit.
**Impact:** the client receives a 500 for a write that actually persisted → user retries → duplicate (no idempotency, R7). Also background breach notifications are lost on failure (Hangfire retry finds nothing Running).
**Likelihood:** Medium (SignalR backplane hiccups, client disconnects mid-send).
**Suggested test:** fault-inject a throwing notifier; assert the HTTP write still 200s (after fix) and the event is retried.
**Suggested fix:** transactional outbox, or at minimum swallow+log notifier errors so delivery failure never fails a committed write.
**Confidence: C.**

## R7 — No idempotency on message send (reliability, medium)

**Evidence:** [`SendVisitorMessageHandler`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L91-L100) inserts a new `Message` per call with no client key; the widget can double-send (R6 retries, double-click, reconnect).
**Impact:** duplicate visitor messages; duplicate conversation creation if `ConversationId` was null on both.
**Likelihood:** Medium.
**Suggested test:** POST the same logical message twice with a client id; assert one row.
**Suggested fix:** client-generated `Guid` message id + unique index + return-existing on conflict.
**Confidence: C.**

## R8 — JWT/refresh token handling exposures (security, medium)

**Evidence:** refresh token in `localStorage` ([api.ts#L13-L16](../../../frontend/inbox/src/api.ts#L8-L18)); access tokens and 30-day visitor tokens are irrevocable (no deny-list — [TokenService](../../../src/NetIntercom.Infrastructure/Auth/TokenService.cs#L66-L75)); hub tokens travel in the query string ([Program.cs#L60-L69](../../../src/NetIntercom.Api/Program.cs#L60-L69)) where proxies may log them; refresh-rotation race (R-adjacent, see key flow 2).
**Impact:** XSS → 14-day credential theft; a leaked/abused visitor token works for 30 days with no kill switch.
**Likelihood:** Medium (conditional on an XSS or logging proxy).
**Suggested test:** N/A automated; security review checklist item.
**Suggested fix:** refresh token in memory + silent re-login, or move inbox to cookie auth ([comparison card 3](../03-architecture-and-patterns/07-comparative-implementations.md)); add a token version/deny-list for revocation.
**Confidence: C** (the exposures exist; severity is deployment-dependent).

## R9 — Snoozed conversations can breach SLA invisibly; nothing wakes them (correctness/product, medium)

**Evidence:** `Snooze` doesn't cancel timers ([Conversation.Snooze](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L63-L77) — only `Close` does); the breach job ignores state ([CheckSlaBreaches#L39](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L39)); and **no job re-opens conversations when `SnoozedUntilUtc` passes** (grep: `SnoozedUntilUtc` is written but only read in DTOs).
**Impact:** snoozed conversations breach while filtered out of the Open inbox; snoozes never auto-wake — a snoozed conversation with no further visitor message is forgotten.
**Likelihood:** Medium (any use of snooze).
**Suggested test:** snooze a conversation with a running timer, advance the clock past breach → assert intended behavior (breach? pause?); separately, assert a wake job reopens at `SnoozedUntilUtc`.
**Suggested fix:** decide policy (likely: timers pause while snoozed); add a snooze-wake recurring job (mid-level ticket M2).
**Confidence: C** (both behaviors confirmed absent/present by reading).

## R10 — `PresenceTracker` bypasses `IClock` (testability/quality, low)

**Evidence:** uses `DateTime.UtcNow` directly ([PresenceTracker#L34,L55,L63](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L30-L65)) while the rest of the codebase injects [`IClock`](../../../src/NetIntercom.Application/Common/Interfaces.cs#L26-L29); the subsystem has zero tests.
**Impact:** grace-period/away logic can't be unit-tested without real sleeps → it stays untested → regressions ship silently.
**Likelihood:** High that it *stays* untested; the code itself is currently correct.
**Suggested fix:** inject `IClock` (good-first ticket G7), then add the state-machine tests.
**Confidence: C.**

## R11 — `PresenceSnapshot` is written but never read (dead path / investigate, low)

**Evidence:** [`PresenceSweeper.PersistAsync`](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L89-L110) upserts snapshots every transition; no query anywhere reads `PresenceSnapshots` back (the live status always comes from the in-memory tracker — [GetAgentsHandler](../../../src/NetIntercom.Application/Teams/TeamsAndAdmin.cs#L47-L51)).
**Impact:** wasted writes; or an unfinished feature (status-on-restart) that silently does nothing. The README claims presence "survives API restarts" — it does *in the table*, but no code restores it.
**Likelihood:** N/A — it's a design gap, not a runtime fault.
**Suggested fix:** either read snapshots to seed presence after restart, or delete the write path. Decide which; don't leave Schrödinger's feature.
**Confidence: C** (no reader exists), **investigate** (intended future use?).

## R12 — Hangfire dashboard authorization may be ineffective (security, investigate)

**Evidence:** dashboard mapped with `.RequireAuthorization("Admin")` ([Program.cs#L189](../../../src/NetIntercom.Api/Program.cs#L189-L190)). Browsers navigating to `/hangfire` don't send `Authorization: Bearer` headers, and Hangfire's own `DashboardOptions` default authorization (local-requests-only) isn't configured here.
**Impact:** either the dashboard is unreachable in dev (admin JWT can't be presented by a browser navigation) or, behind a proxy that strips the local-only assumption, reachable without the intended check. Needs a running system to determine which.
**Likelihood:** Investigate.
**Suggested test:** run the API, browse to `/hangfire` with and without an admin session; observe.
**Suggested fix:** a Hangfire `IDashboardAuthorizationFilter` that checks the cookie/role explicitly, not ASP.NET endpoint auth meant for APIs.
**Confidence: H** — flagged as *investigate*, not asserted.

## R13 — Optimistic concurrency token set but never reconciled (correctness, investigate)

**Evidence:** [`Conversation.RowVersion`](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L30-L31) maps to `xmin` ([AppDbContext#L79](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L78-L79)); no code catches `DbUpdateConcurrencyException`.
**Impact:** concurrent edits (two agents assigning/closing the same conversation) will throw an unmapped exception → 500, rather than a friendly 409. The token *prevents* lost updates (good) but the UX on conflict is a server error.
**Likelihood:** Low–Medium (the inbox lets two agents act on one conversation — R2).
**Suggested test:** two concurrent state changes on one conversation → observe the exception path.
**Suggested fix:** catch `DbUpdateConcurrencyException`, map to a `ConflictException` → 409 (extends [ApiExceptionHandler](../../../src/NetIntercom.Api/Infrastructure/ApiExceptionHandler.cs#L18-L33)).
**Confidence: C** (no handler exists), **H** (exact runtime behavior unverified — needs DB).

---

### Priority order (if you owned this for one sprint)

1. **R1** (identity spoofing) — security, highest impact.
2. **R6 + R7** (notify/idempotency) — user-visible duplicates and false errors.
3. **R3, R4** (authorization gaps) — small fixes, clear wins.
4. **R9** (snooze/SLA) — product correctness + missing job.
5. **R5, R13** (concurrency) — needs design, not just a patch.
6. **R10, R11** (quality/dead path) — cheap hygiene.
7. **R12** (dashboard) — investigate before acting.
