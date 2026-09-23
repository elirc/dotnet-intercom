# S3: Reliable SLA escalations to an external pager, with snooze and business-hours clocks

**Rung:** senior · **Est.:** design doc 1 day + spike 1–2 days · **Skills:** background jobs, outbox, idempotency, time semantics, observability

## Scenario
Enterprise customers want SLA breaches to page an on-call lead (a PagerDuty-style webhook), not just a toast in the inbox. They also want SLA clocks that pause outside the team's office hours, and snoozed conversations that wake up on time. Contract: **every breach pages exactly once as far as the customer can tell**. A duplicate page is a support ticket. A missed page is a contract violation.

## What the code does today (verify each yourself)
- A Hangfire recurring job runs every minute: `SlaBreachJob.RunAsync` → `CheckSlaBreachesHandler` (`Infrastructure/BackgroundJobs/SlaBreachJob.cs:16`, `Application/Sla/CheckSlaBreaches.cs:31-62`). It loads **all** overdue running timers with four `ThenInclude`s, marks them, saves, then notifies over SignalR one by one.
- `SlaTimer.MarkBreached` throws if the timer isn't running (`Domain/Entities/Sla.cs:42-51`). `SlaTimer` has no concurrency token. Only `Conversation` does (`AppDbContext.cs:79`).
- Timers start on visitor messages (`Domain/Entities/Conversation.cs:103-127`) with wall-clock minutes. Office hours only affect routing (`ConversationRouter.cs:40-48`).
- `Snooze` stores `SnoozedUntilUtc` (`Conversation.cs:63-77`). Nothing ever reads it back to wake the conversation. Snoozing also leaves SLA timers running.

## Operating constraints
- Up to 200k running timers. Breach detection latency under 90 s.
- The pager webhook times out regularly (p99 8 s) and sometimes returns 500 after it has already created the incident. It accepts an `Idempotency-Key` header.
- Deploys happen mid-sweep. Nodes die mid-sweep.

## Deliverable
A design doc covering: the breach state transition as a set-based, race-free update; an outbox table for escalations (schema, who writes it, who drains it, retry/backoff, poison/dead-letter, idempotency key choice); the snooze-wake mechanism and what happens to running timers during a snooze; a business-hours SLA clock (data model, and how `BreachAtUtc` is computed and recomputed when office hours change); and observability (metrics, alerts, a dashboard for "breaches detected vs paged vs failed").

## Explain before touching
- Walk through a node dying **between** `SaveChangesAsync` and the SignalR loop in `CheckSlaBreachesHandler`. What does each party see, and what does a Hangfire retry do?
- Two overlapping sweeps: does the "loser" fail today, or does it succeed silently? Trace both `DbContext`s. (Don't trust any doc that tells you the answer. Check it.)
- Why is "the pager dedupes on its side" an incomplete answer for exactly-once from the customer's point of view?

Sealed answer: [`../_answers/ladder-S3.md`](../_answers/ladder-S3.md)
