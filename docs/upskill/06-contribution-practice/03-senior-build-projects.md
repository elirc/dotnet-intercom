# Senior Build Projects

Subsystem-scale work. These are not rewrites — they're the projects a senior would scope, sequence, and de-risk. Each has the full plan: problem, value, design checklist, affected modules, and migration/test/security/performance/rollout/rollback plans. Simulated workflow.

---

## Project S1: Horizontal scale-out (SignalR backplane + shared presence)

**Problem:** the app is correct on one instance and silently wrong on two — presence, conversation-viewers, round-robin, and SignalR groups are all per-process memory ([PresenceTracker](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L25-L28), [Program.cs#L37-L41](../../../src/NetIntercom.Api/Program.cs#L37-L41)).
**Value:** unblocks availability (rolling deploys without dropping all connections) and capacity.
**Design checklist:**
- SignalR backplane: Redis (`AddStackExchangeRedis`) so a message published on instance A reaches a client on B.
- Shared presence: move `PresenceTracker` state to Redis (per-agent connection sets + timestamps with TTL) — the `Compute` logic stays, the storage changes.
- Round-robin: replace the in-memory online set with a Redis read; fix the assignment race (S overlaps Project S3).
- Sticky vs non-sticky: SignalR needs sticky sessions *or* the backplane; decide.
**Affected modules:** `Realtime/*`, `ConversationRouter`, `Program.cs` DI.
**Migration plan:** introduce the backplane behind a config flag (`Realtime:Backplane=InMemory|Redis`); presence store likewise. No schema change.
**Test plan:** a 2-instance integration test (two `WebApplicationFactory` hosts sharing one Redis + Postgres) asserting a message sent via instance A's API reaches a hub client on B. This test *cannot pass today* — it's the project's acceptance gate.
**Security:** Redis auth + network isolation; presence data is low-sensitivity but connection metadata shouldn't leak across tenants.
**Performance:** Redis round-trip per presence read on the assignment path — benchmark; cache the online-set briefly if hot.
**Rollout:** flag off (in-memory) → deploy → flip flag on one canary → verify cross-instance delivery → ramp.
**Rollback:** flip the flag back to in-memory (single instance) — instant.
**Open questions:** is presence-on-restart (R11) solved here by Redis TTL, making `PresenceSnapshot` finally meaningful or finally deletable?
**Stretch:** move conversation-viewers to Redis too (collision detection across instances).

---

## Project S2: Transactional outbox for realtime + future webhooks (fixes R6)

**Problem:** notify-after-commit can fail a committed write (R6) and loses background escalations on partial failure; there's no at-least-once delivery for events.
**Value:** writes never fail because of delivery; events become reliable; future outbound webhooks get a home.
**Design checklist:**
- `OutboxMessage` table written **in the same transaction** as the domain change ([SendVisitorMessage's SaveChanges](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L107)).
- A dispatcher (Hangfire job, [Pattern 10](../03-architecture-and-patterns/05-pattern-catalog.md)) reads unsent rows, fans out via `IRealtimeNotifier`, marks sent.
- Idempotent consumers (clients already dedupe by message id — leverage it).
**Affected modules:** all command handlers that currently call `_notifier` directly; `RealtimeNotifier`; new outbox infra; `Program.cs`.
**Migration:** new table (real EF migration). Transition: write to outbox *and* keep direct-notify behind a flag, then remove direct-notify once the dispatcher is proven.
**Test plan:** fault-inject a throwing notifier → assert the HTTP write still 200s and the event is delivered on the next dispatch; integration test that a committed message always produces exactly one delivered event.
**Security:** outbox rows may contain message bodies — same access controls as `Messages`.
**Performance:** one extra insert per write (cheap); dispatcher polling interval bounds delivery latency — tune against the realtime feel.
**Rollout:** dual-write (outbox + direct) → enable dispatcher → disable direct → remove direct.
**Rollback:** disable dispatcher, re-enable direct-notify (the flag).
**Open questions:** ordering guarantees (per-conversation ordering matters for messages) — does the dispatcher preserve it?
**Stretch:** outbound webhooks to host sites as a second consumer of the same outbox.

---

## Project S3: Reliable, race-free assignment (fixes R5, hardens routing)

**Problem:** round-robin reads-modifies-writes `LastAssignedAtUtc` with no lock (R5); concurrent first-messages collide; assignment depends on in-memory presence.
**Value:** correct load distribution under burst; a foundation for richer routing (skills, capacity).
**Design checklist:**
- Atomic candidate selection: `SELECT ... FOR UPDATE SKIP LOCKED` over eligible online members, or an advisory lock per team.
- Define "online" from the shared presence store (depends on S1).
- Capacity limits (max concurrent per agent) as a natural extension.
**Affected modules:** `ConversationRouter`, possibly a new `IAssignmentService`.
**Migration:** none (behavioral); the raw-SQL/locking path needs Npgsql specifics.
**Test plan:** the concurrent-send spread test from [debugging scenario 3](../05-quality-engineering/03-systematic-debugging.md) (currently would be red) becomes the acceptance test; plus unit tests for the selection algorithm.
**Security:** none new.
**Performance:** locking adds contention per assignment — measure under burst; `SKIP LOCKED` avoids blocking.
**Rollout:** behind a flag; canary on one team.
**Rollback:** flag back to the current best-effort selection.
**Open questions:** what happens to a conversation whose assigned agent goes offline mid-flight (currently nothing reassigns)?
**Stretch:** skills-based routing using `Visitor.AttributesJson`.

---

## Project S4: Frontend test harness + inbox E2E

**Problem:** both frontends are **untested**; SignalR/React behaviors (typing, reconnect catch-up, optimistic append) have zero automated coverage.
**Value:** the highest-risk untested surface gets a safety net; enables confident UI changes.
**Design checklist:**
- Vitest + React Testing Library for the inbox (unit/component): test the message-append dedupe, the `/shortcut` suggestion logic ([Inbox.tsx#L238-245](../../../frontend/inbox/src/pages/Inbox.tsx#L238-L245)), the SLA countdown.
- Playwright for one end-to-end: login → see seeded conversation → reply → SLA badge clears.
- Mock the hub or run against the real API (Testcontainers-backed).
**Affected modules:** new test config in `frontend/inbox`; CI matrix gains a test step ([ci.yml](../../../.github/workflows/ci.yml)).
**Migration:** none (additive tooling).
**Test plan:** the project *is* the test plan; acceptance = CI runs them and they're deterministic.
**Security:** none.
**Performance:** CI time — keep Playwright to one smoke path.
**Rollout:** add as non-blocking CI first, then required once stable (flake budget).
**Rollback:** remove the CI step.
**Open questions:** mock vs real-API for component tests — which gives better signal per maintenance cost?
**Stretch:** widget E2E in an embedded-iframe harness using `demo.html`.

---

**Senior rubric (all projects):** Basic = the design checklist is complete and plausible. Solid = migration + rollout + rollback are concrete and safe under a rolling deploy; the acceptance test is named and currently failing/absent. Strong = you sequenced dependencies (S1 before S3), named the open question you'd resolve first, and your plan reduces blast radius at every step (flags, dual-write, canary).
