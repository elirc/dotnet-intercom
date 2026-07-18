# NetIntercom

A two-sided live chat (Intercom-style): an **embeddable visitor widget** plus an **agent inbox** for support teams.
Built as a learning project for **SignalR real-time messaging, inbox routing, SLAs, canned replies and presence**.

## Stack

- **Backend**: .NET 8 Web API, Clean/layered architecture (Api → Application → Domain → Infrastructure), CQRS-lite with MediatR, FluentValidation, EF Core (PostgreSQL, code-first), Hangfire (PostgreSQL storage), SignalR, Serilog, OpenTelemetry, JWT auth with refresh tokens.
- **Frontend**: React + TypeScript (Vite), TanStack Query, react-hook-form + zod. Two separate apps:
  - `frontend/inbox` — the agent app (3-pane inbox, reports, canned replies)
  - `frontend/widget` — the embeddable widget, built as a single IIFE script (`netintercom-widget.js`)

## Repository layout

```
src/NetIntercom.Domain          entities, enums, domain invariants (SLA timers, conversation state machine, routing rules, office hours)
src/NetIntercom.Application     MediatR commands/queries, validators, DTOs, routing engine, interfaces
src/NetIntercom.Infrastructure  EF Core DbContext, JWT/password services, Hangfire job, seeder
src/NetIntercom.Api             controllers (/api/v1), SignalR hubs, presence tracker/sweeper, middleware
tests/NetIntercom.UnitTests     domain + application unit tests (xUnit)
tests/NetIntercom.IntegrationTests  API + DB tests via Testcontainers (real PostgreSQL), happy-path E2E
frontend/inbox                  agent inbox SPA
frontend/widget                 embeddable widget bundle (+ demo.html host page)
```

## Run it

Prereqs: .NET 8 SDK, Node 20+, Docker.

```bash
# 1. infra (PostgreSQL on :5432)
docker compose up -d

# 2. API on http://localhost:5179 (creates schema + seeds dev data on first run)
dotnet run --project src/NetIntercom.Api

# 3. agent inbox on http://localhost:5173 (proxies /api and /hubs to the API)
cd frontend/inbox && npm install && npm run dev

# 4. widget demo
cd frontend/widget && npm install && npm run build
# then open frontend/widget/demo.html via any static server
```

Seeded agent logins (password `Passw0rd!`):

| Email | Role | Team |
|---|---|---|
| admin@netintercom.local | admin | Support |
| sara@netintercom.local | agent | Support |
| alex@netintercom.local | agent | Billing (office hours 08–18 UTC weekdays) |

Swagger UI: `http://localhost:5179/swagger` · Health: `/healthz` · Hangfire dashboard: `/hangfire` (admin JWT required).

> **Migrations**: the project currently bootstraps via `EnsureCreated` when no migrations exist. To switch to
> migrations: `dotnet tool install -g dotnet-ef` then
> `dotnet ef migrations add Initial -p src/NetIntercom.Infrastructure -s src/NetIntercom.Api` — startup applies
> pending migrations automatically.

> **JWT signing key**: configured in `appsettings.Development.json` for dev. In any other environment set it via
> user-secrets or `Jwt__SigningKey` env var; the app refuses to start without one.

## Tests

```bash
dotnet test tests/NetIntercom.UnitTests          # domain logic: SLA timers, routing, office hours, placeholders
dotnet test tests/NetIntercom.IntegrationTests   # needs Docker; spins up postgres:16 via Testcontainers
```

The integration suite covers the full happy path: agent login → visitor session → message creates + routes a
conversation with an SLA timer → agent inbox shows it → agent reply meets the SLA → internal note stays hidden
from the visitor → history resumes across widget reloads.

## How the interesting parts work

**Real-time (SignalR)** — two hubs, both delivery-only (the DB is source-of-truth):
- `/hubs/agent` (agent JWT): groups `agents` (inbox updates) and `conv:{id}` (active viewers). Server→client events: `messageCreated`, `conversationUpdated`, `slaBreached`, `messagesRead`, `typing`, `presenceChanged`, `viewerJoined/Left` (collision detection: "Sara is already viewing this"). Client→server: `JoinConversation`, `LeaveConversation`, `Typing`, `Heartbeat`.
- `/hubs/visitor` (visitor JWT via `access_token` query param): group `vconv:{id}` per conversation; receives `messageCreated` (never internal notes), `typing`, `teamPresence`, `messagesRead`.
- Reconnect handling: `withAutomaticReconnect()` + on reconnect both clients refetch `GET …/messages?after=…` to catch up on anything missed.

**Routing** — `ConversationRouter`: enabled rules ordered by priority; first keyword match (against first message text or page URL) wins; catch-all rule has a null keyword. The matched team is skipped when outside its office hours (per-team weekly windows + timezone), falling back to the fallback team's queue. Within a team, the *online* member assigned least recently gets the conversation (round-robin via `TeamMember.LastAssignedAtUtc`); with nobody online it stays queued unassigned.

**SLAs** — per-team default `SlaPolicy` (first/next response minutes). A visitor message starts a `SlaTimer` row (`FirstResponse` until the first public agent reply, `NextResponse` afterwards); a public agent reply marks running timers `Met`; closing cancels them. A Hangfire recurring job (every minute, schedule persisted in PostgreSQL so it **survives restarts**) marks overdue timers `Breached` and pushes `slaBreached` escalations to agents. The inbox shows live countdown badges; `/api/v1/reports/sla` aggregates met/breached by team and timer kind.

**Presence** — in-memory `PresenceTracker` fed by hub connect/disconnect plus client activity heartbeats. Grace periods prevent flapping: 60s after a disconnect the agent still counts as online (page refreshes don't flicker); 5 min without activity degrades to Away. A background sweeper broadcasts transitions and persists `PresenceSnapshot` rows.

**Canned replies** — team-shared or personal, inserted by typing `/shortcut` in the composer; placeholders (`{{visitor.name}}`, `{{agent.name}}`, `{{team.name}}`) are rendered server-side against the conversation context.

**Security** — single JWT scheme with role policies (`agent`, `admin`, `visitor`); visitors get a narrow token scoped by a `visitor_id` claim and every widget endpoint/hub method re-checks conversation ownership; refresh tokens are rotated and stored hashed (SHA-256); rate limiting on the public widget + auth endpoints; RFC 7807 problem details everywhere; correlation IDs flow through requests and logs.
