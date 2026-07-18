# Architecture Critique

Written with extra teeth because the repo is synthetic ([provenance verdict](../08-reference/verification-log.md)): clean-looking generated code reliably hides missing ownership checks, races, and untested paths. Each claim is tagged **Confirmed** (read/ran it) or **Hypothesis** (needs a running system).

## Strongest choices (keep these; learn from them)

1. **Layering with inward dependencies + ports.** The Domain compiles with zero dependencies and is fully unit-tested without mocks. This is the cleanest thing in the repo and the reason the SLA/routing logic is trustworthy. *(Confirmed — [Domain csproj](../../../src/NetIntercom.Domain/NetIntercom.Domain.csproj), [unit tests](../../../tests/NetIntercom.UnitTests/) green 27/27.)*
2. **Persist-then-notify with DB-as-truth.** The single decision that makes a realtime app survivable; reconnect reconciliation is wired on both clients. *(Confirmed — [Pattern 9](05-pattern-catalog.md).)*
3. **Invariants on entities.** The conversation state machine and timer lifecycle can't be corrupted by handlers. *(Confirmed.)*
4. **Indexes match query shapes.** The breach scan, inbox sort, and transcript order all have supporting composite indexes. Someone read the queries before defining the schema. *(Confirmed — [data model](02-data-model-and-persistence.md).)*
5. **One error-handling chokepoint + correlation ids.** Consistent RFC 7807 everywhere, traceable. *(Confirmed.)*

## Risks and trade-offs (prioritized)

Full evidence and fixes are in the [risk register](../08-reference/risk-register.md); here's the architectural reading.

### Tier 1 — would block a production launch

- **Identity spoofing via `externalId` (R1, Confirmed).** The trust boundary is in the wrong place: unauthenticated request data establishes identity. This isn't a missing check, it's a missing *design* (host-signed identity tokens). The most important thing to fix.
- **No idempotency + notify-after-commit (R6+R7, Confirmed).** Together they produce user-visible duplicate messages and false errors under perfectly normal network conditions. Architectural, not cosmetic — the write path needs an idempotency key and the notify step must not be able to fail a committed write.

### Tier 2 — correctness under concurrency / scale

- **Single-node assumptions (Confirmed).** Presence, viewers, round-robin, and SignalR groups are all per-process. The app is *correct on one instance and wrong on two*. Nothing in the code says so. This caps horizontal scaling until a backplane + shared presence store exist.
- **Round-robin race (R5, Confirmed race / Hypothesis frequency).** Read-modify-write on `LastAssignedAtUtc` with no lock.
- **Unhandled optimistic-concurrency conflict (R13, Confirmed gap / Hypothesis runtime).** The token prevents lost updates but conflicts become 500s; and the whole-inbox model (R2) makes concurrent edits *likely*.

### Tier 3 — product correctness gaps wearing a clean coat

- **Snooze doesn't pause SLA, nothing wakes snoozes (R9, Confirmed).** A snoozed conversation can breach invisibly and is never auto-reopened — a whole feature (snooze-wake) is missing, not just buggy. Classic synthetic-repo tell: the happy path is covered, the time-passes path doesn't exist.
- **`PresenceSnapshot` written, never read (R11, Confirmed).** Schrödinger's feature; the README claims a durability the code doesn't deliver.

### Tier 4 — quality/testability debt

- **Presence bypasses `IClock` and is untested (R10, Confirmed).** The one subsystem with real concurrent logic has zero tests and can't be tested without real sleeps.
- **Include-contract duplication (Pattern 14, Confirmed).** Five hand-copied include chains; a half-built consolidation that's worse than either extreme.

## "If I owned this for 3 months"

**Month 1 — close the launch-blockers.** R1 (host-signed identity), then R6+R7 (outbox or guarded-notify + idempotency key). Add the missing tests as you go (the breach job and presence first — they're the biggest untested behaviors). Net: the app stops corrupting trust and stops duplicating messages.

**Month 2 — earn the right to scale.** Make the single-node assumptions explicit and then break them: Redis (or Postgres) backplane for SignalR, shared presence store, fix the round-robin race with `SKIP LOCKED`. Decide and document the whole-inbox authorization model (R2) — either scope it or write it down. Handle `DbUpdateConcurrencyException` → 409.

**Month 3 — product correctness + hygiene.** Snooze-wake job + decide the snooze/SLA policy (R9). Read or delete the presence snapshot (R11). Inject `IClock` into presence (R10) and add its tests. Consolidate the include-graph (Pattern 14). Adopt EF migrations to replace `EnsureCreated`.

For each recommendation, the **test + rollback strategy**:

| Change | Test strategy | Rollback |
| --- | --- | --- |
| Host-signed identity (R1) | integration: spoofed `externalId` rejected; signed accepted | feature-flag the verification; flag off = current behavior |
| Outbox / guarded notify (R6) | fault-inject throwing notifier; assert write still 200 + event retried | wrap is additive; revert the wrapper |
| Idempotency key (R7) | duplicate POST → one row | nullable key column; absence = old behavior |
| SignalR backplane | multi-instance integration test (two hosts, one group) | config-toggle backplane vs in-memory |
| Round-robin lock (R5) | concurrent-send spread test (currently red) | the lock is internal; revert the query |
| Snooze-wake job (R9) | unit: clock past `SnoozedUntilUtc` reopens | new recurring job; remove the registration |
| EF migrations | CI runs `MigrateAsync` against a fresh container | keep `EnsureCreated` branch until migrations proven |

## What the synthetic origin specifically hid

Re-stating the hunt's results: the repo *looks* finished because every happy path is implemented and tested. The defects cluster exactly where generated code tends to stop — **time passing** (snooze-wake, token purge), **concurrency** (round-robin, refresh rotation, optimistic conflict), **trust boundaries** (externalId, agent scoping, hub Typing), and **partial-failure** (notify-after-commit). None are visible in a demo; all are visible to a reader who asks "what happens the second time, or at the same time, or when this fails?" Train that question — it's the whole job.

**Self-grade:** Basic = can list two strengths and two risks. Solid = can tier the risks by launch-impact with anchors. Strong = can defend the 3-month ordering (why R1 before scaling, why tests-as-you-go) and name the four failure-classes the synthetic origin hid.
