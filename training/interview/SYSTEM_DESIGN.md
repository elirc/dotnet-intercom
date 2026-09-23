# System design: take NetIntercom to 50× scale, multi-region and enterprise tenants

## Prompt (as an interviewer would give it)
> "You built NetIntercom: an embeddable chat widget, an agent inbox, SignalR, Postgres and Hangfire. Today it serves one company, with 25 agents and about 800 concurrent visitors. We're selling it as SaaS: 2,000 workspaces, the biggest with 300 agents, 400k concurrent widget connections at peak, EU customers who require EU data residency, and an enterprise SLA of 99.95% with pager escalations. Walk me through how the architecture changes. Start from what you have."

## Expected shape of a strong answer (~35 min)
1. **Start from the current system's actual constraints** (cite them): in-process presence, no backplane, the sweep in the API process, `EnsureCreated`, a single DB, no tenant column, visitor identity trust. If you don't know your own system's limits, the interview is over.
2. **Connections tier:** 400k WebSockets. Separate connection-holding from API logic, with Azure SignalR Service or dedicated hub nodes plus a Redis backplane. Estimate the numbers: roughly 20–50k connections per node, so at least 10–20 nodes. Include client reconnect storms after a deploy (jittered backoff) and connection draining.
3. **Write path:** REST writes → Postgres → **outbox** → fan-out (realtime, webhooks, email, pager). Explain why the outbox is needed now (multiple consumers, delivery guarantees) when it wasn't at 25 agents.
4. **Data:** tenancy model (row-level + RLS), partitioning `Messages` by time or workspace, read replicas for reports (`Reports.cs` scans ranges in memory today), hot vs cold transcripts.
5. **Presence and routing:** presence in Redis with TTLs, routing as a service that consumes "conversation created" and assigns with `SKIP LOCKED` fairness, and re-routing queued conversations when agents come online.
6. **Multi-region / residency:** a workspace is pinned to a home region (EU/US) and its DB lives there. The widget's `workspaceKey` resolves to a region endpoint (DNS or a global edge that routes by key). There's no cross-region write path. Global services (billing, auth directory) keep only non-PII data.
7. **Jobs:** SLA/snooze sweeps per region, set-based and idempotent, on dedicated workers. Outbox dispatchers use `SKIP LOCKED`.
8. **Reliability:** SLOs (push p99, send success, escalation latency), error budgets, and what 99.95% means for deploys (roughly 22 min/month).

## Follow-up pressure points (the interviewer pushes on these)
- "A region's Redis fails over and the backplane drops 3 s of messages. What do users see, and what recovers them?" (Catch-up on reconnect. But a still-connected client doesn't reconnect, so you need a periodic reconciliation or a sequence-gap detection.)
- "Your biggest tenant runs a promo and 50k visitors open the widget in a minute. What breaks first?" (`/widget/session` writes a `Visitor` row per new visitor, the rate limiter keyed by IP behind a CDN, DB connection pool, the agents' inbox list refetch storm.)
- "An EU customer's agent travels to the US. Where does their inbox connect?" (To the home region, always. Residency attaches to the data, not the user's location.)
- "Why not Kafka?" (What ordering and retention would you need? Is the outbox + Postgres enough at this throughput? Give numbers.)
- "How do you migrate the existing single-tenant production into this without downtime?" (S2's expand → backfill → enforce sequence, S1's baseline migration.)
- "What would you *not* build yet?" (A good answer defers something explicitly.)

## Self-score
0–2 each: grounded in the current code · capacity numbers · outbox and delivery semantics · tenancy and residency · presence and routing · migration path · defers something consciously. 11+ out of 14 is senior-level.
