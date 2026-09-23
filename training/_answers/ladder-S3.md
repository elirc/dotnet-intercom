# Answer: S3 SLA escalation pipeline (reference design + rubric)

## Today's failure points (the "explain before touching" answers)
- **Crash between save and notify** (`CheckSlaBreaches.cs:53-58`): the timers are durably `Breached`, but no toast was sent. A Hangfire retry re-runs the query, finds no `Running` overdue timers and notifies nothing. The escalation is **lost**, i.e. at-most-once.
- **Overlapping sweeps:** the second run doesn't fail. Separate `DbContext`s both see `Running`, and the in-memory guard in `MarkBreached` passes in both. Both `UPDATE`s succeed (no concurrency token on `SlaTimer`) → duplicate notifications. (`docs/architecture-review.md` §2.3 says the loser throws. It doesn't.)
- **Pager dedupe alone is incomplete:** it only dedupes what *reaches* it. Lost escalations (above) never reach it. Dedupe keys must also be stable across retries, i.e. derived from the timer, not generated per attempt.

## Reference design
1. **Transition + outbox in one transaction:**
```sql
WITH breached AS (
  UPDATE "SlaTimers" SET "Status" = 2, "CompletedAtUtc" = @now
  WHERE "Status" = 0 AND "BreachAtUtc" <= @now
  RETURNING "Id", "ConversationId", "Kind")
INSERT INTO "EscalationOutbox" ("Id", "SlaTimerId", "ConversationId", "Kind", "CreatedAtUtc", "Attempts", "NextAttemptAtUtc")
SELECT gen_random_uuid(), "Id", "ConversationId", "Kind", @now, 0, @now FROM breached;
```
The update is set-based and idempotent: overlapping sweeps can't double-transition, because Postgres row locks serialize the updates and the second sees `Status <> 0`. Add `UNIQUE("SlaTimerId")` on the outbox as a second guard. Batch with `LIMIT` via `ctid IN (SELECT … FOR UPDATE SKIP LOCKED LIMIT 1000)` so 200k timers don't become one giant transaction.
2. **Dispatcher** (a Hangfire recurring job or a hosted service): `SELECT … FROM "EscalationOutbox" WHERE "SentAtUtc" IS NULL AND "NextAttemptAtUtc" <= now() ORDER BY … FOR UPDATE SKIP LOCKED LIMIT 50` → POST to the pager with `Idempotency-Key: sla-{SlaTimerId}` → mark sent. On failure: exponential backoff with jitter, `Attempts++`. After N attempts → `DeadLetteredAtUtc`, plus an alert. The SignalR toast becomes a second consumer of the same outbox row (or stays best-effort).
3. **The "500 after creating the incident" case:** the retry sends the same idempotency key → the pager dedupes. That's why the key is derived from the timer id.
4. **Snooze:** pause SLA on snooze (store `PausedAtUtc`, and on wake push `BreachAtUtc` forward by the paused duration), or cancel the running timer and restart on wake (a product decision; state it). Wake: the same sweep also runs `UPDATE "Conversations" SET "State" = Open … WHERE "State" = Snoozed AND "SnoozedUntilUtc" <= now RETURNING …` and emits `conversationUpdated`.
5. **Business-hours clock:** compute `BreachAtUtc` by walking the team's `OfficeHours` windows (a pure domain function, unit-tested across weekends and DST: `Asia/Manila` has no DST, `Europe/London` does). On an office-hours change, recompute `BreachAtUtc` for running timers of that team in a background job. Store the policy version on the timer so reports can explain it.
6. **Observability:** counters `sla_breaches_detected_total`, `escalations_sent_total`, `escalations_failed_total{reason}`, gauges `escalation_outbox_pending`, `escalation_outbox_oldest_age_seconds` (alert > 120 s), `dead_lettered_total` (alert > 0). Use a dashboard with detected vs sent over time. The gap between them is the SLO.

## Rubric
Basic: an outbox plus retries. Solid: + set-based transition, a stable idempotency key, `SKIP LOCKED`, dead-lettering. Strong: + explains today's lost-escalation interleaving and the overlapping-sweep behavior correctly, the snooze pause semantics, a DST-aware clock, and outbox-age alerting.
