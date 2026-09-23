# Answer: S1 scale-out to 3 replicas (reference design + rubric)

## Failure modes at N=3
| Assumption | Symptom | Class |
|---|---|---|
| In-memory `PresenceTracker` | The router on node B sees only agents connected to B. Conversations queue while online agents sit idle on A/C. The widget's "we're online" differs per node. | **Correctness** |
| No backplane | A visitor on node A sends a message and agents connected to B/C never get `messageCreated`. Typing and collision work only within a node. | **Correctness** (live delivery) |
| Per-node sweeper state | Duplicate or contradictory `presenceChanged` broadcasts. | UX |
| Hangfire server on every node | Fine *if* the recurring id is constant (Hangfire's scheduler enqueues once). The job must still be overlap-safe (retries, slow runs). | Correctness if violated (incident 003) |
| Rate limiting by `RemoteIpAddress` | Every request partitions on the LB's IP → one bucket → mass 429s on `/widget/session`. | **Outage** |
| `EnsureCreated` at startup on every replica | A race on the first boot of an empty DB. It can never apply schema changes. | Deploy blocker |

## Reference choices
- **Backplane:** Redis backplane (`AddStackExchangeRedis`) for cost and simplicity at 40k connections, or Azure SignalR Service if the WebSocket connection count per replica or the ops burden matters. Sticky sessions are **required** unless every client uses WebSockets with `skipNegotiation` (negotiate and the long-polling fallback span requests). Stickiness doesn't replace a backplane: the *writer's* node must still reach sockets held elsewhere.
- **The backplane is at-most-once.** A Redis pub/sub message published while a node is disconnected from Redis is lost, so client catch-up (fixed per M3) remains the correctness mechanism. The design should say this explicitly.
- **Presence:** Redis per agent, e.g. `presence:{agentId}:conn:{connectionId}` with a 90 s TTL refreshed by heartbeat, and an agent-level `lastSeen` sorted set. "Online" = any live conn key, or `now - lastDisconnect < 60 s` stored as a key with a 60 s TTL written on disconnect. A node crash → the conn keys expire → the agent is Offline within TTL (meets the 60 s SLO if TTL ≤ 60 s and the heartbeat is ≤ 20 s). Transitions are broadcast by **one** leader (a Redis lock or a Hangfire recurring job) that diffs snapshots, or by keyspace notifications.
- **Hangfire:** keep servers on all nodes (or a dedicated worker deployment), constant recurring ids, set-based idempotent sweep (incident 003 fix).
- **Schema:** stop running `EnsureCreated` in the app. Create a baseline migration from the current model, insert its id into `__EFMigrationsHistory` on the existing prod DB (it already matches), then apply future migrations from a **separate pre-deploy step** (`dotnet ef migrations bundle`), never from N replicas at startup.
- **Forwarded headers:** `UseForwardedHeaders` with `KnownProxies`/`KnownNetworks`. Partition the widget limiter by visitor token or `AnonymousId` where one exists.
- **Rollout:** config switch `Realtime:Backplane=None|Redis` and `Presence:Store=Memory|Redis`. Step 1: one replica with Redis enabled (proves the code path). Step 2: scale to 3. Watch unassigned ratio, push latency (server timestamp → client ack sample), 429 rate, Redis ops/latency, reconnects/min. Rollback trigger: unassigned ratio > 2× baseline, or push p99 > 2 s for 10 min → scale to 1 (still works with Redis enabled).

## Rubric
Basic: names the backplane and sticky sessions. Solid: + presence redesign with TTL semantics, the rate-limiter and migration issues, an overlap-safe job. Strong: + backplane at-most-once → catch-up is load-bearing, correctness vs UX classification, concrete rollback triggers.
