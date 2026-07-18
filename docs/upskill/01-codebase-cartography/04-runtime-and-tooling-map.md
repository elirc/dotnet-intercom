# Runtime and Tooling Map

## Processes at runtime

| Process | What runs | Port | Notes |
| --- | --- | --- | --- |
| API (`NetIntercom.Api`) | ASP.NET Core host + SignalR + Hangfire server + presence sweeper (hosted service) | 5179 (http) | All in one process — single-node assumptions live here ([Program.cs](../../../src/NetIntercom.Api/Program.cs)) |
| PostgreSQL | App schema + Hangfire schema | 5432 | `docker compose up -d` ([docker-compose.yml](../../../docker-compose.yml)) |
| Inbox dev server (Vite) | Agent SPA, proxies `/api` + `/hubs` to 5179 | 5173 | [vite.config.ts](../../../frontend/inbox/vite.config.ts) |
| Widget | Built as a static IIFE; no dev server in prod | 5174 (dev) | Embedded by host sites via one `<script>` |

The Hangfire server, SignalR hubs, and the API share the .NET process and the same DB connection pool. That's why "scale out" is a real project, not a config change — presence and SignalR groups are per-process memory.

## Boundaries

- **Browser ↔ API:** REST (`/api/v1`) + WebSocket (`/hubs`). Two trust levels: agent/admin JWT (first-party inbox) and visitor JWT (third-party-embeddable widget).
- **API ↔ DB:** EF Core via Npgsql; one `AppDbContext` per request scope; the sweeper makes its own scope.
- **API ↔ background:** Hangfire reads/writes its own Postgres tables; the recurring job re-enters the app via MediatR ([SlaBreachJob](../../../src/NetIntercom.Infrastructure/BackgroundJobs/SlaBreachJob.cs#L10-L17)).
- **Layer boundary:** Application defines ports; Infrastructure + Api supply implementations (see [system map](01-system-map.md)).

## Configuration and secrets

| Setting | Where | Notes |
| --- | --- | --- |
| DB connection | [appsettings.json#L2-L4](../../../src/NetIntercom.Api/appsettings.json#L2-L4) | dev credentials in plain text (fine for local) |
| `Jwt:SigningKey` | empty in [appsettings.json](../../../src/NetIntercom.Api/appsettings.json#L5-L12), set in [appsettings.Development.json](../../../src/NetIntercom.Api/appsettings.Development.json) | **App refuses to start without one** ([AddInfrastructure#L16-L21](../../../src/NetIntercom.Infrastructure/DependencyInjection.cs#L16-L21)) — set via `Jwt__SigningKey` env var or user-secrets in non-dev |
| `Seed:Enabled` | true in appsettings, also forced on in tests | seeding is idempotent ([DbSeeder#L15-L18](../../../src/NetIntercom.Infrastructure/Persistence/DbSeeder.cs#L15-L18)) |
| `Hangfire:Enabled` | true; **false in integration tests** ([ApiFactory#L26](../../../tests/NetIntercom.IntegrationTests/ApiFactory.cs#L26)) | gates the whole background subsystem |
| `Cors:InboxOrigins` | [appsettings.json#L19-L21](../../../src/NetIntercom.Api/appsettings.json#L19-L21) | inbox is allow-listed; widget CORS allows any origin ([Program.cs#L106-L110](../../../src/NetIntercom.Api/Program.cs#L106-L110)) |

Secrets posture: dev signing key is committed (clearly labeled dev-only); production expects env/user-secrets. No secret manager wiring — that's deployment's job.

## Build & test toolchain

| Task | Command | Verified? |
| --- | --- | --- |
| Build all | `dotnet build NetIntercom.sln` | **Verified** — 0 warnings, 0 errors, SDK 8.0.422 |
| Unit tests | `dotnet test tests/NetIntercom.UnitTests` | **Verified** — 27/27 pass |
| Integration tests | `dotnet test tests/NetIntercom.IntegrationTests` | Inferred — needs Docker (was down) |
| Run API | `dotnet run --project src/NetIntercom.Api` | Inferred — needs Postgres |
| Inbox dev | `cd frontend/inbox && npm install && npm run dev` | Inferred |
| Widget build | `cd frontend/widget && npm install && npm run build` | Inferred |
| CI | [.github/workflows/ci.yml](../../../.github/workflows/ci.yml) | builds Release, runs both test suites, builds both frontends (matrix) |

> On the authoring machine `dotnet` was not on PATH; it lives at `~\.dotnet\dotnet.exe`. See the [verification log](../08-reference/verification-log.md) and [command cheatsheet](../08-reference/command-cheatsheet.md).

## Build-system specifics

- **Centralized TFM/nullable** via [Directory.Build.props](../../../Directory.Build.props) — every project is net8.0, nullable enabled, implicit usings. No `Directory.Packages.props`, so package versions live per-csproj (not centrally managed).
- **Schema bootstrap:** no `Migrations/` folder exists; startup calls `EnsureCreated` (or `MigrateAsync` if migrations are ever added — [Program.cs#L147-L165](../../../src/NetIntercom.Api/Program.cs#L147-L165)). This is a known dev shortcut the README flags; production-grade work means adding real migrations (see [data-model doc](../03-architecture-and-patterns/02-data-model-and-persistence.md)).
- **Frontend builds** run `tsc -b` then `vite build` — TypeScript strictness gates the bundle.
