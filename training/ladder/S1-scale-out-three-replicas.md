# S1: Run NetIntercom on 3 API replicas behind a load balancer

**Rung:** senior · **Est.:** design doc 1 day + spike 2 days · **Skills:** SignalR scale-out, distributed presence, background jobs across nodes, rollout/rollback

## Scenario
Sales closed a customer with 300 agents and ~40k concurrent widget sessions at peak. One API process won't do. Ops will run 3 replicas on Azure Container Apps (or AWS ECS) behind an L7 load balancer with rolling deploys. You own the design and the rollout.

## What the code assumes today (verify each yourself)
- `PresenceTracker` is an in-process singleton (`Api/Program.cs:38-39`, `Api/Realtime/PresenceTracker.cs`). `ConversationRouter.PickRoundRobinAsync` asks it who is online (`Application/Routing/ConversationRouter.cs:81`).
- `AddSignalR()` has no backplane (`Program.cs:37`). `RealtimeNotifier` sends to groups (`Api/Realtime/RealtimeNotifier.cs`).
- `PresenceSweeper` holds `_lastBroadcast` in memory per process (`Api/Realtime/PresenceSweeper.cs:23-24`).
- Hangfire server runs in every API process (`Program.cs:141`). The recurring job is registered at startup (`:190`).
- Rate limiting partitions on `RemoteIpAddress` with no forwarded-headers middleware (`Program.cs:114-123`).
- Schema bootstraps through `EnsureCreated` when no migrations exist (`Program.cs:150-157`), on every replica, at startup.

## Operating constraints
- Rolling deploys every weekday. Zero dropped *messages* (a dropped *socket* is fine if the client recovers).
- p99 visitor→agent push latency under 1 s. Presence must stay correct within 60 s of a node dying.
- Budget: one managed Redis is acceptable. Azure SignalR Service / AWS-managed websockets need a cost argument.
- Rollback within 10 minutes to the single-replica topology.

## Deliverable
A design doc of 2–4 pages, `training/ladder/S1-design.md` (yours). It must cover:
1. The failure mode each assumption above causes at N=3, with a concrete user-visible symptom.
2. Backplane choice (Redis backplane vs Azure SignalR vs sticky sessions only), including what "sticky" does and doesn't solve for WebSockets vs long-polling fallback.
3. Presence redesign: data structure, TTL/heartbeat semantics, how the 60 s disconnect grace survives a node crash, and who broadcasts transitions.
4. Hangfire: which nodes run servers, what guarantees the recurring job actually gives, and what makes `CheckSlaBreachesHandler` safe if two runs overlap.
5. Schema: stop `EnsureCreated`-on-startup, and introduce migrations on a live database that was created *without* them.
6. Rollout plan with a feature flag or config switch, the metrics you watch, and the rollback trigger.

## Explain before touching
- Which of the six assumptions breaks **correctness** (wrong assignment, lost message) and which only breaks **UX** (a stale dot)?
- If node B restarts, which agents does node A's router think are online for the next 60 s? Why?
- What exactly does a Redis backplane deliver: at-most-once or at-least-once? What does that make `?after=` catch-up responsible for?

Sealed answer (reference design + grading rubric): [`../_answers/ladder-S1.md`](../_answers/ladder-S1.md)
