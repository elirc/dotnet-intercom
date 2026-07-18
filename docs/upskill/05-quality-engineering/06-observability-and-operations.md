# Observability and Operations

The operational question for every flow: **"if this broke in production, how would I know — and how fast?"** This page answers it per subsystem and names the blind spots.

## What's wired

- **Structured logging:** Serilog to console with a readable template ([Program.cs#L24-L28](../../../src/NetIntercom.Api/Program.cs#L24-L28)); levels tuned down for framework noise ([appsettings.json#L22-L31](../../../src/NetIntercom.Api/appsettings.json#L22-L31)).
- **Correlation ids:** every request gets one, flowed into response headers, the Serilog `LogContext`, and ProblemDetails ([CorrelationIdMiddleware](../../../src/NetIntercom.Api/Infrastructure/CorrelationIdMiddleware.cs)). This is your single most useful debugging tool — a user reports an error, you ask for the `X-Correlation-Id`, you grep the logs.
- **Request logging:** `UseSerilogRequestLogging` ([Program.cs#L169](../../../src/NetIntercom.Api/Program.cs#L169)) — method, path, status, duration per request.
- **Tracing:** OpenTelemetry with ASP.NET Core instrumentation ([Program.cs#L128-L130](../../../src/NetIntercom.Api/Program.cs#L128-L130)) — **but no exporter is configured**, so spans are produced and dropped. Wiring an OTLP exporter is a small, high-value ops ticket.
- **Health check:** `/healthz` verifies Postgres connectivity ([Program.cs#L127,L185](../../../src/NetIntercom.Api/Program.cs#L185)).
- **Error logging:** unmapped exceptions logged at Error with correlation id before becoming 500 ([ApiExceptionHandler#L37-L38](../../../src/NetIntercom.Api/Infrastructure/ApiExceptionHandler.cs#L37-L38)).

## Per-flow: "how would I know it broke?"

| Flow | Signal that exists | Blind spot |
| --- | --- | --- |
| **Visitor message → routed** | request log (status/duration); router logs fallback ([ConversationRouter#L45](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L45)) and "no online agents" ([#L70](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L70)) | no metric for "% conversations queued unassigned" — you'd only know from logs |
| **Agent login / refresh** | 401s in request log; auth rate-limit rejections (429) | the refresh *race* (R6/key-flow-2) looks like normal 401s — no signal distinguishes "expired" from "lost the rotation race" |
| **Agent reply / SLA met** | request log | none specific; SLA-met is silent |
| **SLA breach job** | Warning log per breach with conversation/team/assignee ([CheckSlaBreaches#L45-L48](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L45-L48)); Hangfire dashboard shows job runs/failures | **no metric for breach rate**; notifier failures (R6) produce *no* signal — messages saved-but-not-delivered are invisible |
| **Presence** | sweeper logs failures ([PresenceSweeper#L51](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L51)) | no signal that presence reset after a restart; round-robin paused (no agents online) is silent |
| **Realtime delivery** | none | a hub that silently stops delivering (the swallowed-join bug, scenario 4) has **zero** signal — the worst blind spot |

## The biggest observability gaps (ranked)

1. **Notifier failures are invisible (R6).** A write that commits but fails to broadcast produces a 500 to that one caller and nothing else — no counter, no log of "delivery failed." You'd learn about it from user reports, not telemetry. Fix: log + a metric on every notifier catch.
2. **No exporter for traces/metrics.** OpenTelemetry is half-wired; without an exporter you have logs only. Add OTLP → a backend (Jaeger/Tempo/Honeycomb) and you get request waterfalls for free.
3. **No business metrics.** Queued-unassigned rate, breach rate, refresh-failure rate, median first response (computed in [reports](../../../src/NetIntercom.Application/Reports/Reports.cs) on demand but not emitted as a live metric) — these are the numbers an on-call engineer wants on a dashboard, and none are exported.
4. **Hangfire dashboard auth is uncertain (R12)** — you may not be able to *see* the job dashboard you'd rely on during an incident.

## Operational runbook seeds

- **"Conversations aren't being assigned":** check `/healthz` (DB up?), then logs for "no online agents" (presence reset after restart? — it's in-memory, R11), then the round-robin race (R5).
- **"Visitors not getting replies":** distinguish *saved* (reload shows them → delivery problem, scenario 4) from *not saved* (handler error in logs by correlation id).
- **"SLAs not breaching":** is Hangfire enabled (`Hangfire:Enabled`) and the server running? Check the dashboard for the `sla-breach-check` recurring job's last execution.
- **Restart impact:** schedule + data survive; presence + SignalR connections + viewers reset (clients auto-reconnect). Assignment pauses until agents reconnect.

## Drills

**Drill 1:** add the OTLP exporter (config-only) and trace one `/api/v1/widget/messages` request end to end. Identify which span dominates (likely routing's two queries). *(Inferred — needs a running collector.)*
**Drill 2:** design the three metrics you'd put on an on-call dashboard for this system and name the exact code location each would be emitted from. (Suggested: `notifier_failures_total` at every notifier catch; `conversations_queued_unassigned_total` at [ConversationRouter#L70](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L70); `sla_breaches_total` at [CheckSlaBreaches#L44](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L42-L44).)
**Self-grade:** Basic = can use a correlation id to find a request in logs. Solid = can answer "how would I know?" for three flows. Strong = identifies the realtime-delivery and notifier-failure blind spots and proposes the exact instrumentation points.
