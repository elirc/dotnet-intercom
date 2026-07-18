# Good First Tickets

> **Simulated OSS workflow.** This repo is synthetic (no real issues/maintainers — see [verification log](../08-reference/verification-log.md)). You play the contributor; these tickets encode what a maintainer would accept. Treat acceptance criteria as a real reviewer's bar. Branch per ticket, write the test, open a PR against your own fork's `main`.

Each ticket: small blast radius, follows an existing pattern, has a clear done. Difficulty Easy→Medium. Spread across UI, API, domain, persistence, tests.

---

## Ticket G1: SLA countdown turns amber configurably

**Difficulty:** Easy · **Time:** 30 min · **Skills:** React props, component reading
**Story:** As an agent, I want the "warning" threshold on the SLA badge to be tunable so teams with tight SLAs get earlier warning.
**Why it's a good contribution:** pure presentation, one component, zero backend; teaches prop threading from a real data source.
**Acceptance criteria:**
- [ ] [`SlaCountdown`](../../../frontend/inbox/src/pages/Inbox.tsx#L148-L165) takes a `warnMinutes` prop (default 5).
- [ ] Both call sites ([row L136](../../../frontend/inbox/src/pages/Inbox.tsx#L136), [header L261](../../../frontend/inbox/src/pages/Inbox.tsx#L261)) compile; no behavior change at default.
**Read first:** the two call sites and the `minutes < 5` branch.
**Files likely touched:** `frontend/inbox/src/pages/Inbox.tsx`.
**Review questions:** did you keep the default? Is the prop typed `number`?

## Ticket G2: Return 404 (not 500) when saving a routing rule for a missing team

**Difficulty:** Easy · **Time:** 30 min · **Skills:** reading validators vs handlers, exception mapping
**Story:** As an admin, a bad `teamId` should give a clear 404, not a server error.
**Why it's good:** [`SaveRoutingRuleHandler`](../../../src/NetIntercom.Application/Teams/TeamsAndAdmin.cs#L139-L168) *already* checks team existence (L141-145) — so verify it works and add the missing test. (Contrast [`SaveCannedReplyHandler`](../../../src/NetIntercom.Application/CannedReplies/CannedReplies.cs#L71-L97), which does *not* — see G8.)
**Acceptance criteria:**
- [ ] Integration test: POST `/api/v1/routing-rules` with a random `teamId` → 404 with ProblemDetails.
**Read first:** [ApiExceptionHandler](../../../src/NetIntercom.Api/Infrastructure/ApiExceptionHandler.cs#L16-L46) (how NotFound becomes 404).
**Review questions:** does the test assert the status *and* the problem `title`?

## Ticket G3: Extract a single conversation loader (finish Pattern 14)

**Difficulty:** Medium · **Time:** 1–2 h · **Skills:** refactoring, include-contracts
**Story:** As a maintainer, I want one place that defines the conversation include-graph so DTO mapping can't silently break.
**Why it's good:** consolidates four hand-copied chains ([SendAgentMessage](../../../src/NetIntercom.Application/Conversations/SendAgentMessage.cs#L38-L44), [GetConversation](../../../src/NetIntercom.Application/Conversations/GetConversation.cs#L20-L26), [ListConversations](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L28-L33), [AssignConversationHandler.LoadAsync](../../../src/NetIntercom.Application/Conversations/ManageConversation.cs#L58-L65)) into one helper; small, high-value, no behavior change.
**Acceptance criteria:**
- [ ] A `ConversationQueries.WithDetails(this IQueryable<Conversation>)` (or equivalent) extension.
- [ ] All four sites use it; **note** `SendVisitorMessage` additionally needs `SlaPolicy` — decide whether the helper includes it or that site keeps an extra `.Include`.
- [ ] All 27 unit tests + integration suite still pass; no DTO field goes null.
**Implementation plan:** 1) write the extension; 2) replace one site, run tests; 3) repeat; 4) document the include-contract in a comment.
**What could go wrong:** `ListConversations` applies `.Where`/`.OrderBy` after include — make sure the extension returns `IQueryable`, not a materialized list.
**Review questions:** did you change any query *shape* (e.g., add an Include a site didn't have, risking row multiplication)?

## Ticket G4: Add `[ProducesResponseType]` annotations to the widget controller

**Difficulty:** Easy · **Time:** 45 min · **Skills:** ASP.NET attributes, Swagger
**Story:** As an API consumer, I want Swagger to show 200/400/401/429 shapes for widget endpoints.
**Why it's good:** documentation-only, teaches the ProblemDetails status surface ([WidgetController](../../../src/NetIntercom.Api/Controllers/WidgetController.cs#L33-L60)).
**Acceptance criteria:**
- [ ] Each action annotated with realistic response types; Swagger UI reflects them.
**Review questions:** did you include 429 (the controller is rate-limited)?

## Ticket G5: Validate `OfficeHours.TimeZoneId` at save time

**Difficulty:** Medium · **Time:** 1–2 h · **Skills:** validation, domain, debugging scenario 2
**Story:** As an admin, saving a team with a bad timezone id should fail loudly, not silently route everyone to fallback.
**Why it's good:** fixes the silent fallback in [`IsOpenAt`](../../../src/NetIntercom.Domain/Entities/OfficeHours.cs#L21-L29) at the right layer (write, not read).
**Acceptance criteria:**
- [ ] Saving a team with an unknown `TimeZoneId` is rejected (where teams are persisted — note: there is currently **no SaveTeam command**, so part of this ticket is recognizing that office hours are only set via seed; scope it to a validator + unit test on a helper).
- [ ] Unit test: known zone passes, garbage fails.
**Review questions:** did you fail at write time rather than changing the read-time default?

## Ticket G6: Purge expired/revoked refresh tokens (new Hangfire job)

**Difficulty:** Medium · **Time:** 2–3 h · **Skills:** background jobs, MediatR, the job-shim pattern
**Story:** As an operator, I don't want `RefreshTokens` to grow forever.
**Why it's good:** clean application of [Pattern 10](../03-architecture-and-patterns/05-pattern-catalog.md) (thin shim → command); mirrors [SlaBreachJob](../../../src/NetIntercom.Infrastructure/BackgroundJobs/SlaBreachJob.cs#L10-L17) exactly.
**Acceptance criteria:**
- [ ] `PurgeExpiredTokensCommand` handler deletes tokens where `ExpiresAtUtc < now` OR (`RevokedAtUtc != null` AND older than N days).
- [ ] A `PurgeTokensJob` shim + `RecurringJob.AddOrUpdate` daily ([Program.cs#L187-L191](../../../src/NetIntercom.Api/Program.cs#L187-L191)).
- [ ] Unit test of the predicate with a fake clock.
**What could go wrong:** don't delete the *active* token of a logged-in user; only expired/revoked.
**Review questions:** is the job idempotent (safe to run twice)? Does it use `IClock`?

## Ticket G7: Make `PresenceTracker` use `IClock`

**Difficulty:** Medium · **Time:** 1–2 h · **Skills:** dependency injection, testability
**Story:** As a maintainer, I want presence transitions unit-testable without real `Thread.Sleep`.
**Why it's good:** removes the one `DateTime.UtcNow` holdout ([PresenceTracker](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L34) and elsewhere); enables the missing presence tests.
**Acceptance criteria:**
- [ ] Tracker takes `IClock`; singleton registration still works (IClock is singleton — [Infrastructure DI](../../../src/NetIntercom.Infrastructure/DependencyInjection.cs#L24)).
- [ ] New unit tests: connect→Online, disconnect→Online within grace→Offline after, idle→Away — all with a controllable clock.
**What could go wrong:** the sweeper and hubs also call `DateTime.UtcNow`; decide your blast radius (tracker-only is fine for this ticket).
**Review questions:** did any production timing behavior change? (It must not.)

## Ticket G8: 404 instead of 500 for canned reply with bad team

**Difficulty:** Easy–Medium · **Time:** 1 h · **Skills:** validators, parity with G2
**Story:** As an agent, saving a team canned reply with a non-existent `teamId` should 404, not 500 (FK violation).
**Why it's good:** [`SaveCannedReplyHandler`](../../../src/NetIntercom.Application/CannedReplies/CannedReplies.cs#L71-L97) never checks the team exists, unlike its routing-rule sibling — a real inconsistency to close.
**Acceptance criteria:**
- [ ] Existence check throwing `NotFoundException` when `!Personal`.
- [ ] Integration test proving 404.
**Review questions:** did you guard only the team path (personal replies have no team)?

## Ticket G9: Dedupe the message-append logic across inbox handlers

**Difficulty:** Easy · **Time:** 45 min · **Skills:** React, DRY
**Story:** The "append message if not already present by id" logic appears twice ([Inbox.tsx#L42-L44](../../../frontend/inbox/src/pages/Inbox.tsx#L42-L44) and [L214-L216](../../../frontend/inbox/src/pages/Inbox.tsx#L214-L216)).
**Why it's good:** tiny, safe, teaches extracting a pure helper.
**Acceptance criteria:**
- [ ] One `appendUnique(list, message)` helper used by both.
- [ ] No behavior change.
**Review questions:** is the helper pure (no React imports)?

## Ticket G10: Add a `GET /api/v1/widget/conversations/{id}` for the widget

**Difficulty:** Medium · **Time:** 2 h · **Skills:** full vertical slice, authorization-in-query
**Story:** As the widget, I want to refetch my conversation's current state (assignee/state) after a reconnect without a full message reload.
**Why it's good:** complete slice following [Pattern 1](../03-architecture-and-patterns/05-pattern-catalog.md) + [Pattern 7](../03-architecture-and-patterns/05-pattern-catalog.md); small surface.
**Acceptance criteria:**
- [ ] Query + handler scoping to `VisitorId` (copy the ownership check from [WidgetQueries](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L25-L30)).
- [ ] Controller action under `[Authorize(Policy="Visitor")]`.
- [ ] Integration test: visitor A cannot read visitor B's conversation (403/404).
**What could go wrong:** returning the full `ConversationDto` leaks `AssigneeName` etc. — decide the widget's minimal shape (mirror [RealtimeNotifier's trimmed visitor payload](../../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs#L32-L37)).
**Review questions:** does the cross-tenant test actually fail before your ownership check exists?

---

**Self-grade for any ticket:** Basic = it works and compiles. Solid = test added, existing tests pass, follows the matching pattern. Strong = you noted a related issue you *didn't* fix (scope discipline) and said why in the PR.
