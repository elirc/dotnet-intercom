# Fast Track: One Weekend

Goal: by Sunday night you can run NetIntercom locally, explain how a visitor message becomes an assigned conversation with an SLA timer, and you've shipped one small, safe, tested change.

## 0. Prerequisites & gotchas (this machine class)

- .NET 8 SDK. On the authoring machine `dotnet` was **not on PATH** — it lives at `~\.dotnet\dotnet.exe`. If `dotnet --version` fails, use the full path or fix PATH. *(verified: SDK 8.0.422 builds the solution cleanly)*
- Docker Desktop **running** (Postgres + integration tests need it). *(the daemon was down at authoring time, so the run-steps below are inferred — they follow the repo's own README and verified config)*
- Node 20+.

## 1. Run it

```powershell
# from the repo root
docker compose up -d                      # Postgres 16 on :5432            (inferred)
dotnet run --project src/NetIntercom.Api  # API on http://localhost:5179    (inferred)
```

First start creates the schema via `EnsureCreated` and seeds dev data — see [Program.cs](../../src/NetIntercom.Api/Program.cs#L147-L165) and [DbSeeder.SeedAsync](../../src/NetIntercom.Infrastructure/Persistence/DbSeeder.cs#L13-L79). Seed logins: `admin@netintercom.local` / `sara@netintercom.local` / `alex@netintercom.local`, all password `Passw0rd!`.

```powershell
cd frontend/inbox; npm install; npm run dev    # inbox on :5173, proxies /api + /hubs   (inferred)
cd frontend/widget; npm install; npm run build # builds dist/netintercom-widget.js      (inferred)
```

Open `http://localhost:5173`, sign in as Sara. For the widget, serve `frontend/widget/demo.html` from any static server.

Verified without Docker, in case your environment is also constrained:

```powershell
dotnet build NetIntercom.sln                       # verified: 0 warnings, 0 errors
dotnet test tests/NetIntercom.UnitTests            # verified: 27/27 pass, ~225ms
```

## 2. Trace these two flows (90 minutes, no shortcuts)

**Flow A — visitor's first message becomes a routed conversation.** Open these in order and write one sentence per hop:

1. [`WidgetApi.sendMessage`](../../frontend/widget/src/api.ts#L61-L67) — what the widget actually POSTs.
2. [`WidgetController.Send`](../../src/NetIntercom.Api/Controllers/WidgetController.cs#L39-L46) — where `VisitorId` comes from (claim, not body — why does that matter?).
3. [`SendVisitorMessageHandler.Handle`](../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L47-L115) — creation, routing call, SLA bookkeeping, notifier.
4. [`ConversationRouter.RouteAsync`](../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L29-L73) — rule match → office hours → fallback → round-robin.
5. [`Conversation.RecordVisitorMessage`](../../src/NetIntercom.Domain/Entities/Conversation.cs#L103-L127) — where the SLA timer is born and why it doesn't stack.
6. [`RealtimeNotifier.MessageCreatedAsync`](../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs#L20-L27) — who hears about it.

**Flow B — agent login.** [`LoginCommandHandler.Handle`](../../src/NetIntercom.Application/Auth/Login.cs#L38-L59) → [`TokenService.CreateAgentTokens`](../../src/NetIntercom.Infrastructure/Auth/TokenService.cs#L43-L64) → client storage in [`setTokens`](../../frontend/inbox/src/api.ts#L8-L18). Note what is stored where (access token: sessionStorage; refresh token: localStorage) and hold that thought for the security checklist.

## 3. First 10 files, in order

1. [README.md](../../README.md) — the repo's own map; surprisingly honest about its mechanisms.
2. [NetIntercom.sln](../../NetIntercom.sln) + [Directory.Build.props](../../Directory.Build.props) — 6 projects, net8.0, nullable enabled everywhere.
3. [src/NetIntercom.Domain/Entities/Conversation.cs](../../src/NetIntercom.Domain/Entities/Conversation.cs) — the heart of the domain; state machine + SLA logic.
4. [src/NetIntercom.Domain/Entities/Sla.cs](../../src/NetIntercom.Domain/Entities/Sla.cs) — timer lifecycle (Running → Met/Breached/Cancelled).
5. [src/NetIntercom.Application/Common/Interfaces.cs](../../src/NetIntercom.Application/Common/Interfaces.cs) — every seam in the architecture on one page.
6. [src/NetIntercom.Application/Widget/SendVisitorMessage.cs](../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs) — the busiest write path.
7. [src/NetIntercom.Application/Routing/ConversationRouter.cs](../../src/NetIntercom.Application/Routing/ConversationRouter.cs) — the most "product" logic per line.
8. [src/NetIntercom.Api/Program.cs](../../src/NetIntercom.Api/Program.cs) — composition root: auth, CORS, rate limits, Hangfire, pipeline order.
9. [src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs](../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs) — mappings, indexes, audit + soft-delete in `SaveChangesAsync`.
10. [tests/NetIntercom.IntegrationTests/HappyPathTests.cs](../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs) — the whole product narrated as one test.

## 4. One small safe change

Make the SLA countdown badge turn amber earlier. In [`SlaCountdown`](../../frontend/inbox/src/pages/Inbox.tsx#L148-L165) the warning threshold is `minutes < 5`. Change it to 10, run the inbox, snooze/reopen a conversation to see badges. Why this one: pure presentation, one line, zero blast radius, but it forces you to find a component, read its data source (`activeSlaTimer.breachAtUtc` from [`ConversationDto.From`](../../src/NetIntercom.Application/Dtos.cs#L67-L77)), and observe the realtime update path.

Backend alternative if you prefer C#: in [`SaveCannedReplyValidator`](../../src/NetIntercom.Application/CannedReplies/CannedReplies.cs#L52-L63), lower the shortcut max length and watch the 400 ProblemDetails shape change in Swagger.

## 5. One check to run

```powershell
dotnet test tests/NetIntercom.UnitTests        # verified: must stay 27/27
```

If Docker is up, also run the integration suite — it exercises login → widget → routing → SLA → internal-note privacy in one pass (inferred; see [HappyPathTests](../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs#L22-L104)).

## 6. Teach-back (the actual exit test)

Explain to a person or a rubber duck, without opening the code: *"When a visitor types 'my invoice is wrong' on a pricing page at 3am, what happens — which team gets it, who is assigned, what timer starts, and what does the agent see at 9am?"* You should touch: keyword rule priority, office-hours fallback ([`RouteAsync`](../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L40-L51)), unassigned queue when nobody's online, FirstResponse timer, and the breach job ([`CheckSlaBreachesHandler`](../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L31-L62)).

## What this fast track skips

Presence internals, read receipts, canned-reply rendering, reports, refresh-token rotation, all of the architecture critique, and every drill. That's the rest of the curriculum.
