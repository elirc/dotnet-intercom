# System Map

## Two clients, one API, one database

```
┌────────────────────┐         ┌────────────────────┐
│ frontend/widget    │         │ frontend/inbox     │
│ (embeddable IIFE)  │         │ (agent SPA)        │
│ visitor JWT        │         │ agent/admin JWT    │
└─────────┬──────────┘         └─────────┬──────────┘
          │ REST /api/v1/widget          │ REST /api/v1/*
          │ WS /hubs/visitor             │ WS /hubs/agent
          └──────────────┬───────────────┘
                         ▼
            ┌────────────────────────────┐
            │ NetIntercom.Api            │  controllers, SignalR hubs,
            │                            │  presence tracker/sweeper,
            │                            │  middleware, composition root
            └─────────────┬──────────────┘
                          │ MediatR
            ┌─────────────▼──────────────┐
            │ NetIntercom.Application    │  commands/queries + validators,
            │                            │  routing engine, DTOs, ports
            └──────┬──────────────┬──────┘
                   │              │ implements ports
        references │              ▼
            ┌──────▼─────┐  ┌────────────────────────────┐
            │ Domain     │  │ NetIntercom.Infrastructure │  EF DbContext, JWT/password
            │ entities,  │◄─┤                            │  services, Hangfire job, seeder
            │ invariants │  └─────────────┬──────────────┘
            └────────────┘                │ Npgsql
                                          ▼
                                  ┌───────────────┐
                                  │ PostgreSQL 16 │  + Hangfire schema
                                  └───────────────┘
```

## Project graph and ownership

| Project | Owns | Must NOT own | Depends on |
| --- | --- | --- | --- |
| [NetIntercom.Domain](../../../src/NetIntercom.Domain/NetIntercom.Domain.csproj) | Entities, enums, invariants (state machines, SLA/timer/office-hours/routing-rule logic), `DomainException` | EF, ASP.NET, MediatR, any I/O | nothing (pure C#) |
| [NetIntercom.Application](../../../src/NetIntercom.Application/NetIntercom.Application.csproj) | Use-case handlers, validators, DTOs, routing *orchestration*, port interfaces ([Interfaces.cs](../../../src/NetIntercom.Application/Common/Interfaces.cs#L8-L73)) | Concrete EF/SignalR/JWT, HTTP types | Domain, MediatR, FluentValidation, EF *abstractions* only |
| [NetIntercom.Infrastructure](../../../src/NetIntercom.Infrastructure/NetIntercom.Infrastructure.csproj) | `AppDbContext` + mappings, token/password services, the Hangfire job shim, the seeder | Business rules, HTTP | Application (to implement its ports), Npgsql, EF, JWT |
| [NetIntercom.Api](../../../src/NetIntercom.Api/NetIntercom.Api.csproj) | Controllers, SignalR hubs, presence (in-memory), middleware, composition root ([Program.cs](../../../src/NetIntercom.Api/Program.cs)) | Domain rules, persistence details | Application + Infrastructure |
| [tests/UnitTests](../../../tests/NetIntercom.UnitTests/NetIntercom.UnitTests.csproj) | Domain + small application logic | external services | Domain, Application |
| [tests/IntegrationTests](../../../tests/NetIntercom.IntegrationTests/NetIntercom.IntegrationTests.csproj) | Full-stack HTTP + real Postgres via Testcontainers | — | the Api project + Testcontainers |

Dependency arrows point **inward** toward Domain. Infrastructure depends on Application (not the reverse) because it *implements Application's interfaces* — the dependency-inversion shape. Verify it: Domain's csproj has zero PackageReferences.

## Public vs private surface

**Public (externally reachable):**
- Agent/admin REST: `/api/v1/auth`, `/conversations`, `/canned-replies`, `/teams|agents|sla-policies|routing-rules`, `/reports` (all `[Authorize(Policy="Agent")]`, admin-only writes on the directory controller).
- Widget REST: `/api/v1/widget/*` (`session` anonymous, rest visitor-scoped, IP rate-limited).
- Hubs: `/hubs/agent` (agent), `/hubs/visitor` (visitor).
- Ops: `/swagger` (dev only), `/healthz`, `/hangfire` (admin — but see risk R12).

**Private (internal only):**
- All MediatR commands/queries — reachable only through controllers/hubs/jobs.
- `CheckSlaBreachesCommand` — invoked solely by the Hangfire job; never exposed.
- Presence tracker/sweeper — process-internal singletons.
- The DbContext — reached only via `IAppDbContext`, except the sweeper's explicit scope and test setup.

## Where the real logic concentrates

Most files are plumbing. The product lives in a handful of places — spend your attention here:
- [`Conversation`](../../../src/NetIntercom.Domain/Entities/Conversation.cs) — state machine + SLA bookkeeping.
- [`ConversationRouter`](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs) — routing + office hours + round-robin.
- [`PresenceTracker`](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs) — the only stateful in-memory subsystem.
- [`Program.cs`](../../../src/NetIntercom.Api/Program.cs) — every cross-cutting decision (auth, CORS, rate limits, Hangfire, pipeline order) in one screen.
- [`AppDbContext`](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs) — the data model, indexes, audit/soft-delete behavior.
