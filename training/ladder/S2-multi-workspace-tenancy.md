# S2: Sell NetIntercom to many companies (workspace multi-tenancy)

**Rung:** senior · **Est.:** design doc 1 day · **Skills:** multi-tenant data isolation, auth claims, zero-downtime migration, security testing

## Scenario
Today one deployment serves one support organization. The business wants a SaaS: every customer company gets a **workspace** with its own agents, teams, routing rules, SLA policies, canned replies, widget and visitors. Target: 2,000 workspaces, from 1 agent to 300 agents. A workspace must never see another workspace's data, and that includes realtime events.

## What the code assumes today (verify each yourself)
- No table has a tenant column. Queries are global, e.g. `ListConversationsHandler` (`Application/Conversations/ListConversations.cs:28`) and `GetAgentsHandler` (`Application/Teams/TeamsAndAdmin.cs:49`).
- `ICurrentUser` exposes only `AgentUserId`, `VisitorId`, `IsAdmin` (`Application/Common/Interfaces.cs:32-37`). Tokens carry no workspace claim (`Infrastructure/Auth/TokenService.cs:43-75`).
- SignalR groups are global: `"agents"`, `"visitors"`, `conv:{id}` (`Api/Realtime/AgentHub.cs:30`, `VisitorHub.cs:31`, `PresenceSweeper.cs:72-80`).
- The widget session is anonymous and trusts `externalId` from the browser (`Application/Widget/StartConversation.cs:51-52`). Unique indexes are global (`AppDbContext.cs:34, 45`).
- The router's fallback picks "the first team ever created" (`ConversationRouter.cs:50-51`).
- `Admin` is one global role (`Program.cs:73-76`).

## Operating constraints
- The existing production database (one implicit tenant) is migrated **with no downtime** and becomes workspace #1.
- One shared Postgres cluster. Justify schema-per-tenant or database-per-tenant if you choose either.
- A missed tenant filter must fail **closed**, and at least one layer must catch it in CI.
- The widget embed snippet changes (it now needs a workspace key). Old snippets on customer sites keep working for 30 days.

## Deliverable
A design doc of 2–4 pages covering: the isolation model (row-level `WorkspaceId` + EF global query filters vs Postgres RLS vs schema-per-tenant), how the workspace reaches the handler (claim → `ICurrentUser` → `DbContext`), unique-index changes, SignalR group naming, widget identity verification (HMAC `userHash`), the admin-role split (workspace admin vs platform operator), the migration sequence (expand → backfill → enforce), and the test strategy (a cross-tenant test suite, not just a happy path).

## Explain before touching
- Global query filters are bypassed by `IgnoreQueryFilters()`, by raw SQL, and by one more thing in this codebase. Which? (Look at how Hangfire jobs get their `DbContext`.)
- `CheckSlaBreachesHandler` runs with no HTTP user. Where does its workspace come from, and what should the filter do when there is none?
- `AnonymousId` is unique globally today. Is that still right when two workspaces embed the widget on the same customer site?

Sealed answer: [`../_answers/ladder-S2.md`](../_answers/ladder-S2.md)
