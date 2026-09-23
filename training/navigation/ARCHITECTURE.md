# NetIntercom: architecture reconstructed from the code

Every claim cites evidence. The repo has one commit, `f2198dd`, and line numbers refer to that commit.

## System context
```
 customer website ──(IIFE widget, frontend/widget)──┐  REST /api/v1/widget/*  + WS /hubs/visitor
                                                     ▼
 agent browser ──(SPA, frontend/inbox)──────►  NetIntercom.Api (ASP.NET Core 8, one process)
                                                  ├─ Controllers (REST)      ├─ SignalR hubs
                                                  ├─ Hangfire server         └─ PresenceSweeper (hosted service)
                                                     ▼
                                               PostgreSQL 16: app tables + Hangfire schema
```
- The widget is a single IIFE exposing `NetIntercomWidget.init` (`frontend/widget/src/main.tsx:11-16`). It uses the cookie `netintercom_vid` as its anonymous identity (`frontend/widget/src/api.ts:25-37`).
- The inbox proxies `/api` and `/hubs` to `:5179` in dev (`frontend/inbox/vite.config.ts:6-11`).
- A single database holds both app data and the Hangfire tables (`Program.cs:140`, the same `connectionString`).

## Components and dependency boundaries
| Project | Depends on | Holds | Evidence |
|---|---|---|---|
| Domain | nothing | entities + invariants (SLA timer lifecycle, conversation state machine, routing match, office hours) | `NetIntercom.Domain.csproj` (no refs), `Entities/Conversation.cs:36-135` |
| Application | Domain, EF Core, MediatR, FluentValidation | commands/queries, `IAppDbContext`, `ConversationRouter`, DTOs, interfaces for realtime/presence/tokens | `NetIntercom.Application.csproj`, `Common/Interfaces.cs` |
| Infrastructure | Application | `AppDbContext`, JWT/password services, Hangfire job class, seeder | `Infrastructure/DependencyInjection.cs` |
| Api | Application, Infrastructure | controllers, hubs, `PresenceTracker` (implements `IPresenceService`), `RealtimeNotifier` (implements `IRealtimeNotifier`), middleware | `Program.cs:31-41` |

Boundary notes: Application references `Microsoft.EntityFrameworkCore` and exposes `DbSet<>` on `IAppDbContext`, so it isn't persistence-ignorant, and handlers write LINQ directly. Realtime and presence are inverted behind interfaces, so Application never references SignalR. That's the cleanest seam in the repo, and the one S1 (scale-out) would swap. No architecture tests enforce any of this.

## Request pipeline
`CorrelationIdMiddleware` → `UseExceptionHandler` (`ApiExceptionHandler`) → Serilog request logging → CORS("inbox") → authn (JWT, with `access_token` from the query string on `/hubs`) → authz → rate limiter → controllers/hubs (`Program.cs:167-185`). MediatR pipeline: `LoggingBehavior` → `ValidationBehavior` → handler (`Application/DependencyInjection.cs:15-16`). Registration order means Logging wraps Validation.

## Flow 1: visitor's first message (widget → routed conversation → agents)
1. `Widget.tsx:80-90` `send()` → `WidgetApi.sendMessage` POSTs `/api/v1/widget/messages` with the visitor bearer (`api.ts:61-67`).
2. `WidgetController.Send` (`:39-46`), with `[Authorize(Policy="Visitor")]`, CORS "widget" and rate limit "widget", → `SendVisitorMessageCommand(VisitorId from claim)`.
3. `SendVisitorMessageHandler` (`Application/Widget/SendVisitorMessage.cs:47-115`): load visitor → no conversation id → new `Conversation` → `ConversationRouter.RouteAsync` (`Routing/ConversationRouter.cs:29-73`: rules by priority → office hours → fallback team → `PickRoundRobinAsync` over **online** members from `IPresenceService`) → load `SlaPolicy` → add `Message` → `RecordVisitorMessage` starts a FirstResponse `SlaTimer` (`Conversation.cs:103-127`) → one `SaveChangesAsync` (one transaction).
4. After the commit: `RealtimeNotifier.MessageCreatedAsync` sends to the `agents` group plus `vconv:{id}`, then `ConversationUpdatedAsync` (`Api/Realtime/RealtimeNotifier.cs:20-38`).
5. The inbox `messageCreated` handler invalidates the list and appends to the cached transcript (`Inbox.tsx:40-46`). The widget joins `vconv:{id}` after the response (`Widget.tsx:86-89`).

## Flow 2: agent reply meets the SLA
`Inbox.tsx:209-218` → `POST /conversations/{id}/messages` → `SendAgentMessageHandler` (`Conversations/SendAgentMessage.cs:36-73`): if not a note → `RecordAgentReply` marks running timers `Met` and reopens a snoozed conversation (`Conversation.cs:87-100`) → save → push to both hubs (notes go to agents only: `RealtimeNotifier.cs:23`). Test coverage: `HappyPathTests.cs:73-84`.

## Flow 3: SLA breach sweep (background)
Startup registers the recurring job `sla-breach-check`, which runs every minute (`Program.cs:190`) → Hangfire resolves `SlaBreachJob` from DI (`Infrastructure/DependencyInjection.cs:27`) → `ISender.Send(CheckSlaBreachesCommand)` → the handler loads `Running && BreachAtUtc <= now` timers with their conversation graph, marks them `Breached`, saves, then sends `slaBreached` per timer (`Application/Sla/CheckSlaBreaches.cs:31-62`). No HTTP user exists in this flow. Nothing tests it.

## Realtime and presence (the stateful part)
- `AgentHub` groups: `agents` and `conv:{id}`. `VisitorHub` groups: `visitors` and `vconv:{id}` (ownership-checked join: `VisitorHub.cs:37-46`).
- Presence is **in memory**: connections plus heartbeats, with a 60 s disconnect grace and 5 min → Away (`PresenceTracker.cs:15-16, 76-90`). `PresenceSweeper` runs every 15 s, broadcasts transitions and upserts `PresenceSnapshot` (`PresenceSweeper.cs:40-110`). Nothing reads snapshots back.

## Data model (`AppDbContext.cs:30-121`)
`Visitor 1─* Conversation 1─* Message`, `Conversation 1─* SlaTimer`, `Conversation *─1 Team / AgentUser (assignee) / SlaPolicy`, `Team 1─* TeamMember *─1 AgentUser`, `RoutingRule *─1 Team`, `CannedReply` (team or personal, soft-deleted via query filter + `SaveChangesAsync` override: `:113, 140-145`), `RefreshToken` (hashed), `PresenceSnapshot` (1 per agent). Concurrency: only `Conversation.RowVersion` (xmin). Key indexes: `Conversations(State, LastMessageAtUtc)`, `Messages(ConversationId, CreatedAtUtc)`, `SlaTimers(Status, BreachAtUtc)`, unique `Visitors.AnonymousId`, `AgentUsers.Email`, `RefreshTokens.TokenHash`.

## Test strategy (as it is)
- 27 unit tests over Domain and a pure helper: SLA transitions, routing match, office hours, placeholders, median (`tests/NetIntercom.UnitTests/*`). The unit project references only Application, so hubs, presence and the notifier can't be unit-tested there.
- 3 integration tests on Testcontainers Postgres with `Hangfire:Enabled=false` (`ApiFactory.cs:21-27`): one long happy path, one authn/authz check, health.
- No frontend tests, no hub tests, no negative authorization matrix.

## Build and deploy
CI (`.github/workflows/ci.yml`) builds and runs the unit and integration tests, and builds both frontends. There's no deploy pipeline, Dockerfile or IaC in the repo. Local run: `docker-compose.yml` (Postgres 16) plus `dotnet run`. The schema is created at startup by `EnsureCreated` (`Program.cs:150-157`).
