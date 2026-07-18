# Boundaries and Layers

## The intended shape

Four backend layers, dependencies pointing inward:

```
Api ──► Application ──► Domain ◄── Infrastructure ──► Application (implements ports)
```

| Layer | Owns | Must not own | Boundary mechanism |
| --- | --- | --- | --- |
| **Domain** | invariants, state machines, pure rules | I/O, frameworks | zero dependencies (check the [csproj](../../../src/NetIntercom.Domain/NetIntercom.Domain.csproj)) |
| **Application** | use cases, orchestration, ports | concrete EF/SignalR/HTTP | interfaces in [Interfaces.cs](../../../src/NetIntercom.Application/Common/Interfaces.cs#L8-L73) |
| **Infrastructure** | persistence, tokens, jobs, seeding | business rules | implements Application's ports |
| **Api** | HTTP/WS, presence, middleware, wiring | domain rules, persistence detail | controllers dispatch to MediatR |

The discipline is real: the Application project cannot `using Microsoft.AspNetCore.*` or reference `AppDbContext` — it only sees `IAppDbContext`. Verify by trying (mentally) to call `_db.Database.BeginTransaction()` in a handler — you can't, the port doesn't expose it.

## Where the boundaries hold

- **Domain purity:** SLA/timer/routing/office-hours logic is testable with zero mocks ([RoutingAndOfficeHoursTests](../../../tests/NetIntercom.UnitTests/RoutingAndOfficeHoursTests.cs), [ConversationTests](../../../tests/NetIntercom.UnitTests/ConversationTests.cs)) — proof the rules don't depend on infrastructure.
- **Port ownership:** realtime is an interface ([IRealtimeNotifier](../../../src/NetIntercom.Application/Common/Interfaces.cs#L39-L51)) implemented in the Api project ([RealtimeNotifier](../../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs)) — the Application layer fires events without knowing SignalR exists.
- **Composition at the edge:** every concrete decision is in [Program.cs](../../../src/NetIntercom.Api/Program.cs) and the two `DependencyInjection.cs` files — the layers themselves don't self-wire.

## Where the boundaries leak (the honest part)

1. **`IAppDbContext` exposes `IQueryable`/`DbSet`** ([Interfaces.cs#L8-L24](../../../src/NetIntercom.Application/Common/Interfaces.cs#L8-L24)). EF's semantics — change tracking, query translation limits, lazy/eager behavior — leak straight through the port. It's a *testing/substitution* seam, not persistence-ignorance. Consequence: handlers contain LINQ that only EF can translate (e.g. `ToLower().Contains` in [ListConversations#L57-L61](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L57-L61)). Know that you have an EF-flavored port, not an abstract repository.
2. **Anemic-domain leakage.** [Conversation](../../../src/NetIntercom.Domain/Entities/Conversation.cs) protects `State`/`ClosedAtUtc` with private setters but leaves `TeamId`, `AssigneeId`, `LastMessageAtUtc`, `SlaPolicyId` publicly mutable — and handlers set them directly ([SendVisitorMessage#L103-L104](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L103-L104), [ConversationRouter#L59-L60](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L59-L60)). The encapsulation boundary is *partial*: the state machine is protected, the assignment/preview data isn't.
3. **Domain does I/O-adjacent work.** [`OfficeHours.IsOpenAt`](../../../src/NetIntercom.Domain/Entities/OfficeHours.cs#L14-L34) calls `TimeZoneInfo.FindSystemTimeZoneById` — a *platform* dependency (zone database differs Windows vs Linux) inside "pure" domain code, with a silent UTC fallback. Defensible, but it means the domain isn't quite as host-independent as it looks.
4. **The presence subsystem lives in Api, not Application.** [PresenceTracker](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs) holds real domain logic (the online/away/offline state machine) but sits in the Api project because it's tied to hub connections. That's why it bypasses `IClock` (risk R10) — it's outside the layer where that discipline is enforced.

## Why this matters

The leaks aren't failures — they're *trade-offs the codebase made and didn't label*. A senior reads them as: "the team chose an EF-coupled port for testing pragmatism, partial encapsulation for handler convenience, and put presence in the API for connection-locality." Each is reasonable; each has a switch signal (a second persistence target kills #1; a state bug from a direct mutation motivates #2; a multi-node deploy forces #4 into a shared store).

**Drill 1:** trace the dependency of `SendVisitorMessageHandler` — list every type it touches and which layer each lives in. Confirm none is from `Microsoft.AspNetCore.*`.
**Drill 2:** pick boundary leak #2. Make `TeamId` private-set on `Conversation` and add an `AssignToTeam` method. Count the compile errors — that number *is* the boundary leak's blast radius. Decide if closing it is worth that churn.
**Drill 3 (self-grade):** Basic = can name the four layers and their dependencies. Solid = can point at one real leak with its anchor. Strong = can argue, for each leak, the switch signal that would make you fix it.
