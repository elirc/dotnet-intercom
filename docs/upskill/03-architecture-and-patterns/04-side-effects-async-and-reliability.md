# Side Effects, Async and Reliability

The system has four kinds of side effect beyond DB writes: SignalR fan-out, the Hangfire breach job, the in-memory presence subsystem, and external platform calls (timezone DB). This page maps how each handles failure — and where it doesn't.

## The reliability spine: persist-then-notify, DB-as-truth

Every write commits to Postgres, *then* notifies over SignalR; clients treat the socket as lossy and reconcile from the DB after reconnect ([Pattern 9](05-pattern-catalog.md)). This is the load-bearing reliability decision and it's a good one — a dropped hub message only ever means "late," never "lost."

The reconciliation paths:
- Widget: `onreconnected` → `getMessages(after)` ([Widget.tsx#L52-L58](../../../frontend/widget/src/Widget.tsx#L52-L58)).
- Inbox: `onreconnected` → `invalidateQueries()` (everything) ([Inbox.tsx#L73-L76](../../../frontend/inbox/src/pages/Inbox.tsx#L73-L76)) + a 30s `refetchInterval` safety net ([#L32](../../../frontend/inbox/src/pages/Inbox.tsx#L32)).
- Both dedupe incoming messages by id, because the HTTP response *and* the hub event both deliver the same message.

## Where reliability is thin

**1. No outbox; notify-after-commit can fail loudly (R6).** [`SendVisitorMessage`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L107-L112) awaits the notifier after the commit. If SignalR throws, the caller gets a 500 for a write that *succeeded* — and the client, seeing failure, retries.

**2. No idempotency (R7).** That retry creates a duplicate, because [message insertion](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L91-L100) has no client-supplied key or unique constraint. The two gaps compound: #1 manufactures the retry, #2 makes it harmful.

**3. The background job loses notifications on partial failure.** [`CheckSlaBreachesHandler`](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L42-L59) saves breaches, then loops sending `slaBreached`. A throw mid-loop means: breaches are persisted (durable, good), some escalations sent, the rest lost — and Hangfire's retry re-runs the handler, which now finds *no* Running overdue timers, so the lost escalations are never resent. The *state* is correct; the *notification* is at-most-once with a silent hole.

## What IS reliable

- **SLA schedule survives restarts.** Hangfire stores the recurring-job schedule in Postgres ([Program.cs#L133-L142](../../../src/NetIntercom.Api/Program.cs#L133-L142), [#L190](../../../src/NetIntercom.Api/Program.cs#L190)) — a crash doesn't lose the every-minute cadence.
- **The breach handler is idempotent on retry.** Re-running it is safe: the `Status == Running && BreachAtUtc <= now` filter plus `MarkBreached`'s Running-only guard ([Sla.cs#L42-L51](../../../src/NetIntercom.Domain/Entities/Sla.cs#L42-L51)) mean a second run marks nothing twice. (It just can't *re-notify*.)
- **Graceful shutdown.** The presence sweeper honors its `stoppingToken` and swallows only non-cancellation exceptions ([PresenceSweeper#L43-L53](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L43-L53)) — it won't spin-crash on a transient DB blip.

## In-memory state and its durability cliff

Presence and conversation-viewers live only in process memory ([PresenceTracker](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L25-L28)). On restart everyone is Offline until clients reconnect — and since round-robin assignment consumes presence ([ConversationRouter#L81](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L81)), assignment pauses until agents reconnect. The `PresenceSnapshot` table *looks* like durability but nothing reads it back (R11) — the README's "survives API restarts" is true of the row, false of the behavior.

## Timeouts, retries, external calls

- **No Polly, no HttpClient calls** — the only "external" dependency is the timezone database via `TimeZoneInfo` ([OfficeHours#L21-L29](../../../src/NetIntercom.Domain/Entities/OfficeHours.cs#L21-L29)), which fails *closed to UTC* silently. There are no outbound webhooks or third-party APIs to wrap in retries, so their absence is correct, not a gap.
- **DB calls have no explicit timeout or retry policy** (no `EnableRetryOnFailure` in the Npgsql setup — [Infrastructure DI#L29-L30](../../../src/NetIntercom.Infrastructure/DependencyInjection.cs#L29-L30)). For a single-DB app that's defensible; transient-fault retry is a cheap hardening if Postgres ever moves to a managed service with failovers.

## Failure visibility

- Correlation ids thread request→response→logs→ProblemDetails ([CorrelationIdMiddleware](../../../src/NetIntercom.Api/Infrastructure/CorrelationIdMiddleware.cs)).
- Unmapped exceptions are logged at Error with the correlation id before becoming a 500 ([ApiExceptionHandler#L37-L38](../../../src/NetIntercom.Api/Infrastructure/ApiExceptionHandler.cs#L37-L38)).
- Breaches log a Warning with conversation/team/assignee ([CheckSlaBreaches#L45-L48](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L45-L48)).
- **Gap:** notifier failures (R6) have no dedicated log/metric — they surface only as the request's 500. There's no counter for "messages saved but not delivered."

## The fixes, ordered by leverage

1. **Make notify never fail a committed write** — wrap notifier calls in try/log, or introduce a transactional outbox. Kills R6's false-500 → R7's duplicate chain at the source.
2. **Idempotency key on send** — client `Guid` + unique index (R7).
3. **At-least-once escalation** — a `BreachNotifiedAtUtc` column so the job can resend un-notified breaches instead of relying on a single pass.
4. **Read the presence snapshot on startup** or delete it (R11).

**Drill:** for each of the four side-effect kinds, write its failure-mode line: what happens on (a) a transient error, (b) a process crash mid-operation. Then mark which the current code handles and which it silently drops. This table *is* a reliability review.
**Self-grade:** Basic = explains persist-then-notify. Solid = identifies the notify-after-commit + idempotency compound risk. Strong = produces the side-effect failure table and orders the fixes by leverage, distinguishing "state correct / notification lost" from "state lost."
