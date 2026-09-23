# STAR stories from NetIntercom (complete them *after* doing the work)

Each skeleton names the real artifacts. Fill the brackets with what you actually did and measured. Don't invent numbers. Target: 2 minutes spoken per story.

## 1. Debugging: "Visitors stopped getting replies after a network blip" (ladder M3)
- **S:** A two-sided chat. Widget on customer sites, SignalR push, DB as the source of truth.
- **T:** Customers reported replies appearing only after a refresh.
- **A:** Reproduced with DevTools offline for [N] s. Found two independent causes: lost group membership on reconnect (new connection id, `vconv:{id}` never rejoined) and a stale closure in `onreconnected`. Fixed with [server auto-join / client rejoin], plus catch-up with the `after` cursor, joining *before* fetching. Test: [SignalR client test / Vitest].
- **R:** [before/after repro result]. **Lesson:** "delivery-only hubs" make catch-up load-bearing, so the catch-up path needs its own tests.

## 2. Trade-off: "Polling sweep vs per-timer scheduled jobs for SLA breaches" (TRADEOFFS §3, ladder S3)
- **S/T:** Breach detection must survive restarts and deploys.
- **A:** Compared a one-minute sweep over `(Status, BreachAtUtc)` against 200k Hangfire delayed jobs. Chose [X] because [latency need vs storage/cancellation cost]. Made the sweep set-based so overlaps are harmless.
- **R:** [numbers from your spike]. **Lesson:** at-least-once plus idempotent beats "exactly-once" plumbing.

## 3. Incident: "Duplicate SLA escalations that grew with every deploy" (incident 003)
- **S/T:** On-call. Toasts duplicated 3–4×, and the count rose daily after the move to 3 replicas.
- **A:** Queried Hangfire's `recurring-jobs` set, found per-pod job ids accumulating across deploys. Explained why the domain guard didn't stop duplicates (a per-`DbContext` in-memory check, no row concurrency token). Fixed with a constant id, cleanup, and a set-based `UPDATE … WHERE Status = Running RETURNING`.
- **R:** [duplicates → 0; the SLA breach rate settled at the *correct* higher level because `MarkMet` no longer counted late replies]. **Lesson:** "the metric got worse" can mean the measurement got better.

## 4. Migration: "Introducing EF migrations to a database created by EnsureCreated" (ladder S1/M1)
- **S/T:** Needed an index on a live DB whose schema came from `EnsureCreated`, with no migration history.
- **A:** Baseline migration, marked applied in `__EFMigrationsHistory`, `CREATE INDEX CONCURRENTLY` with `suppressTransaction`, applied via a bundle in a pre-deploy step instead of on replica startup.
- **R:** [lock time, rollout result]. **Lesson:** [ ].

## 5. Agent-caught bug: "The reviewer model found the replay creates a second conversation" (agentic/WORKFLOW)
- **S/T:** Implemented idempotent send through an implementer → adversarial reviewer loop.
- **A:** The reviewer's check #2 found [what it found, e.g. the retried first message creating a second conversation because the key was scoped to `ConversationId`]. You rejected [finding Y] because [evidence].
- **R:** [tests added, outcome]. **Lesson:** a spec with explicit concurrency cases is what made the review useful.
