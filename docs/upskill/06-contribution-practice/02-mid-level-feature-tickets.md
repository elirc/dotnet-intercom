# Mid-Level Feature Tickets

Cross-layer features: schema + API + (sometimes) UI + tests. Each carries a design note, risk, and rollback — because mid-level means owning the change, not just the code. Simulated workflow (see [module README](README.md)).

---

## Ticket M1: Conversation tags

**Difficulty:** Medium · **Time:** 1–2 days · **Skills:** EF relationship, new slice, UI, migration
**Story:** As an agent, I want to tag conversations (`vip`, `bug`, `refund`) so I can filter the inbox by tag.
**Why it's a good contribution:** a self-contained vertical feature touching every layer once, following established patterns; moderate blast radius (additive).
**Acceptance criteria:**
- [ ] `Tag` + `ConversationTag` entities; many-to-many; mapped with indexes ([AppDbContext](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L30-L121) style).
- [ ] `POST/DELETE /api/v1/conversations/{id}/tags` slices ([Pattern 1](../03-architecture-and-patterns/05-pattern-catalog.md)).
- [ ] `ListConversationsQuery` gains a `tag` filter ([ListConversations#L35-62](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L35-L62) is the place).
- [ ] Inbox filter chip; `ConversationDto` exposes tags.
- [ ] Unit test for the filter; integration test for add/remove/filter.
**Design note:** decide tag scope (global vs per-team) up front — it changes the schema. Tags on the `ConversationDto` mean adding to the [include graph](../03-architecture-and-patterns/05-pattern-catalog.md) (do G3 first so there's one place to change).
**Risk & rollback:** additive schema (new tables) → safe; feature is dark until UI ships. Rollback = drop the new tables + revert. **Migration:** needs real EF migrations (the repo's `EnsureCreated` won't evolve a live DB — flag this).

## Ticket M2: Snooze-wake job (fixes R9)

**Difficulty:** Medium · **Time:** 1 day · **Skills:** background jobs, domain, the job-shim pattern
**Story:** As an agent, when I snooze a conversation until 2pm, it should reopen at 2pm even if the visitor doesn't write.
**Why it's good:** closes a real correctness gap (R9 — snoozes never auto-wake); clean application of [Pattern 10](../03-architecture-and-patterns/05-pattern-catalog.md).
**Acceptance criteria:**
- [ ] `WakeSnoozedConversationsCommand` handler reopens conversations where `State == Snoozed && SnoozedUntilUtc <= now`.
- [ ] A recurring job (minutely) like [SlaBreachJob](../../../src/NetIntercom.Infrastructure/BackgroundJobs/SlaBreachJob.cs); notifies agents via `ConversationUpdatedAsync`.
- [ ] **Decide and implement** the snooze/SLA-timer policy (R9): do timers pause while snoozed? (Recommend: yes — add a `Pause`/`Resume` or cancel-and-restart.) Document the decision.
- [ ] Unit test with a fake clock; idempotent re-run.
**Design note:** this forces the snooze/SLA question the codebase dodged. Write the policy decision in the PR description as if answering a maintainer.
**Risk & rollback:** new job, additive; rollback = remove the `RecurringJob.AddOrUpdate`. Watch: reopening fires notifications — don't storm agents if many wake at once.

## Ticket M3: Agent-side typing & presence in the widget header

**Difficulty:** Medium · **Time:** 1 day · **Skills:** SignalR cross-hub, React state
**Story:** As a visitor, I want to see "Sara is typing…" and whether my specific agent is online.
**Why it's good:** exercises the cross-hub fan-out already half-built ([AgentHub.Typing → visitor](../../../src/NetIntercom.Api/Realtime/AgentHub.cs#L69-L75)); mostly client work with a small server addition.
**Acceptance criteria:**
- [ ] Widget shows agent typing (already delivered — verify [Widget.tsx#L42-44](../../../frontend/widget/src/Widget.tsx#L42-L44)) and assignee online status (new — needs a per-agent presence event scoped to the visitor's conversation).
- [ ] Server: scope a presence event to `vconv:{id}` for the assignee only (don't leak all-agent presence to visitors).
- [ ] Manual test script (no UI harness — see [testing](../05-quality-engineering/02-writing-tests-here.md)).
**Design note:** privacy — visitors must learn about *their* agent only, never the roster. This is a DTO-leak-to-visitor decision ([security checklist](../05-quality-engineering/05-security-checklist.md)).
**Risk & rollback:** client-dominated; rollback removes the new event handler. Watch the stale-closure trap ([Widget.tsx#L72](../../../frontend/widget/src/Widget.tsx#L72)).

## Ticket M4: Idempotent message send (fixes R7)

**Difficulty:** Medium-Hard · **Time:** 1–2 days · **Skills:** concurrency, EF constraints, client+server
**Story:** As a visitor on a flaky connection, retrying a send shouldn't double-post.
**Why it's good:** fixes a real reliability defect that compounds with R6; teaches idempotency keys end to end.
**Acceptance criteria:**
- [ ] Client generates a `Guid` per logical message ([widget api.ts sendMessage](../../../frontend/widget/src/api.ts#L61-L67)).
- [ ] `Message` gains a nullable unique `ClientMessageId`; duplicate insert returns the existing message instead of creating one.
- [ ] Same for the inbox composer.
- [ ] Integration test: same key twice → one row, same id returned.
**Design note:** scope the uniqueness (per conversation vs global); decide the response for a duplicate (200 with existing). This pairs with R6 — note in the PR that the false-500 still needs fixing separately.
**Risk & rollback:** nullable column = additive; old clients (no key) keep working (key null → no dedupe). **Migration** required. Rollback = ignore the column.

## Ticket M5: Keyset (cursor) pagination for the inbox

**Difficulty:** Medium-Hard · **Time:** 1–2 days · **Skills:** query design, API contract
**Story:** As an agent with thousands of conversations, deep pages should be fast and stable under new inserts.
**Why it's good:** replaces offset pagination's drift ([Pattern 13](../03-architecture-and-patterns/05-pattern-catalog.md)) where it matters; teaches cursor design.
**Acceptance criteria:**
- [ ] A `before`/`after` cursor (encoding `LastMessageAtUtc` + `Id` for tie-break) alongside or replacing `page`.
- [ ] `PaginatedList` or a new `CursorPage<T>` returns the next cursor.
- [ ] Works with the existing `(State, LastMessageAtUtc)` index.
- [ ] Test: inserting a new conversation mid-scroll doesn't duplicate/skip items.
**Design note:** keep offset for the admin/reports lists; cursors only where live data moves. Cursor must be opaque and stable — don't leak raw offsets.
**Risk & rollback:** API contract change → version it or keep `page` working during transition. Rollback = revert to offset; cursor is additive if you keep both.

---

**General mid-level rubric:** Basic = feature works, tests pass. Solid = design note answers the open question (scope/policy), migration handled, rollback stated. Strong = you flagged a *related* risk you deliberately scoped out (e.g., M4 noting R6 remains), and your PR reads like you'd own the on-call page for it.
