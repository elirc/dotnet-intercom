# Verification Log

Everything in this suite that is presented as *verified* was actually executed in the session that produced these docs, on the machine described below. Anything not listed here is labeled **inferred** where it appears.

## Snapshot pin

| Item | Value |
| --- | --- |
| Date of authoring | 2026-06-09 |
| `git rev-parse HEAD` | **No commit exists.** The enclosing git repository (`C:\Users\Owner`, branch `main`) has zero commits; `netintercom` itself has no `.git` of its own. |
| Snapshot identity | File timestamps: every source file was last written 2026-06-09 (same day as authoring). Line numbers in all anchors refer to the files as they existed at authoring time. |
| OS / shell | Windows 11 Pro, PowerShell 5.1 |
| .NET SDK | 8.0.422 (`dotnet --version`, via `C:\Users\Owner\.dotnet\dotnet.exe` — `dotnet` is not on PATH on this machine) |

**Consequence for learners:** because there is no commit, anchors can drift if you edit files. Before starting exercises, run `git init && git add -A && git commit -m "training-lab baseline"` inside `netintercom/` so you can always diff back to the state these docs describe.

## Provenance verdict: synthetic / AI-generated

Evidence, all checked in-session:

- `git log` — no history at all (zero commits anywhere in scope).
- Every file in the repo (source, configs, README, both frontends) carries the same last-write date, 2026-06-09, written within an ~45-minute window.
- No `CONTRIBUTING`, `CHANGELOG`, `LICENSE`, issue templates, or any other organic-project residue.
- Code style is perfectly uniform across all 6 projects and both frontends (same comment voice, same ordering conventions) — real multi-month projects never look like this.
- Dependency versions cluster tightly around mid-2024 versions (EF Core 8.0.8, MediatR 12.4.0, Vite 5.4.0) with no age spread.

**Adaptation applied:** modules 06 and 07 are framed as a *simulated* OSS workflow (the learner role-plays contributor; the docs encode maintainer expectations). Git-archaeology exercises are omitted. The architecture critique and risk register hunt specifically for the failure classes that clean-looking synthetic repos hide (missing ownership checks, races, N+1s, untested error paths) — several were found; see [risk-register.md](risk-register.md).

**Answer-key exclusion:** searched the full inventory for `.seeded-flaws.md`, `ANSWER-KEY.md`, and similar. **None found.** Nothing was excluded; nothing leaked.

## Commands run (verified)

### 1. Build

```
PS> & "$env:USERPROFILE\.dotnet\dotnet.exe" --version
8.0.422
PS> & "$env:USERPROFILE\.dotnet\dotnet.exe" build C:\Users\Owner\Desktop\netintercom\NetIntercom.sln --nologo -v q
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:36.43
```

### 2. Unit tests

```
PS> & "$env:USERPROFILE\.dotnet\dotnet.exe" test C:\Users\Owner\Desktop\netintercom\tests\NetIntercom.UnitTests --no-build --nologo -v q
Passed!  - Failed: 0, Passed: 27, Skipped: 0, Total: 27, Duration: 225 ms - NetIntercom.UnitTests.dll (net8.0)
```

### 3. Docker (needed for integration tests and `docker compose up`)

```
PS> docker info --format '{{.ServerVersion}}'
failed to connect to the docker API at npipe:////./pipe/docker_engine; ... the daemon is not running
```

Docker Desktop was installed but **not running** during authoring.

### 4. Source size (for curriculum tier sizing)

```
PS> ($cs | Get-Content | Measure-Object -Line).Lines   # all *.cs/*.ts/*.tsx excluding bin/obj/node_modules
4686
```

6 solution projects + 2 frontend apps, ~4.7k LOC → sized between the Small and Medium tiers (Medium by project count, Small by LOC). Artifact counts in this suite reflect that.

## NOT run (inferred — labeled as such wherever they appear)

| Command | Why not run | Confidence it works |
| --- | --- | --- |
| `dotnet test tests/NetIntercom.IntegrationTests` | Requires Docker daemon (Testcontainers spins up `postgres:16-alpine`); daemon was down | High — [ApiFactory](../../../tests/NetIntercom.IntegrationTests/ApiFactory.cs#L10-L39) is conventional Testcontainers + `WebApplicationFactory` |
| `docker compose up -d` | Same | High — compose file is a single stock postgres:16 service |
| `dotnet run --project src/NetIntercom.Api` | Needs the Postgres container | High — but note `Jwt:SigningKey` guard in [AddInfrastructure](../../../src/NetIntercom.Infrastructure/DependencyInjection.cs#L16-L21): the app refuses to start outside Development without a key |
| `npm install && npm run dev` (frontend/inbox) | Node present but install/dev not exercised; API dependency was down anyway | High |
| `npm run build` (frontend/widget) | Same | Medium-high — `tsc -b` strictness was not exercised |
| `dotnet ef migrations add ...` | `dotnet-ef` tool not installed; repo currently has **no Migrations/ folder** (uses `EnsureCreated` — see [Program.cs](../../../src/NetIntercom.Api/Program.cs#L147-L165)) | High |

A `bin/Debug/net8.0` output tree exists under `src/NetIntercom.Api`, meaning the API was built (not necessarily run) on this machine before authoring. The build artifacts include all referenced packages (Hangfire, Npgsql, Serilog, etc.), consistent with the csproj files.

## Uncertainties / "investigate" items carried into the docs

- Whether the Hangfire dashboard at `/hangfire` is actually reachable with an admin JWT: it is mapped with `RequireAuthorization("Admin")` ([Program.cs](../../../src/NetIntercom.Api/Program.cs#L189-L190)), but browsers navigating to a dashboard do not attach `Authorization` headers, and Hangfire's own default dashboard filter allows local requests only. Labeled *investigate* in the risk register, not asserted.
- Whether `xmin`-based optimistic concurrency ([Conversation.RowVersion](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L30-L31)) ever surfaces as a handled conflict: the token is configured, but no code catches `DbUpdateConcurrencyException`. Behavior under concurrent writes was not exercised (needs running DB).
- EF query translation for the grouped `First()` in [GetOverviewReportHandler](../../../src/NetIntercom.Application/Reports/Reports.cs#L60-L67) was not executed against a real database; it is *expected* to translate under EF Core 8 but is labeled inferred where discussed.

## Final self-audit results

### Anchor re-check (10 random anchors re-opened after writing)

Recorded at the end of the authoring pass — see table below.

| # | Anchor | File / lines | Symbol present? |
| --- | --- | --- | --- |
| 1 | `RouteAsync` | src/NetIntercom.Application/Routing/ConversationRouter.cs#L29-L73 | ✔ |
| 2 | `RecordVisitorMessage` | src/NetIntercom.Domain/Entities/Conversation.cs#L103-L127 | ✔ |
| 3 | `OnMessageReceived` (JWT via query string) | src/NetIntercom.Api/Program.cs#L60-L69 | ✔ |
| 4 | `SaveChangesAsync` (audit + soft delete) | src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L123-L149 | ✔ |
| 5 | `Compute` (presence state machine) | src/NetIntercom.Api/Realtime/PresenceTracker.cs#L76-L90 | ✔ |
| 6 | `Handle` (refresh rotation) | src/NetIntercom.Application/Auth/Refresh.cs#L31-L56 | ✔ |
| 7 | `api()` (fetch wrapper w/ one-shot refresh) | frontend/inbox/src/api.ts#L25-L60 | ✔ |
| 8 | `JoinConversation` (visitor ownership check) | src/NetIntercom.Api/Realtime/VisitorHub.cs#L37-L46 | ✔ |
| 9 | `VisitorToAgentConversationFlow` | tests/NetIntercom.IntegrationTests/HappyPathTests.cs#L22-L104 | ✔ |
| 10 | `IsOpenAt` | src/NetIntercom.Domain/Entities/OfficeHours.cs#L14-L34 | ✔ |

A second batch of 8 anchors was also re-opened and confirmed (symbol present at the cited range): `PaginatedList.CreateAsync` (PaginatedList.cs#L21-L28), the `slaBreach` sort arm (ListConversations.cs#L67-L70), `HasQueryFilter` on CannedReply (AppDbContext.cs#L108-L114), `SlaCountdown` (Inbox.tsx#L148-L165), `CreateVisitorToken` (TokenService.cs#L66-L75), `ConversationDto.From` (Dtos.cs#L67-L77), `VisitorHub.Typing` (VisitorHub.cs#L48-L58 — confirms R4: no ownership check), and the breach-scan Includes (CheckSlaBreaches.cs#L34-L40). **18/18 re-checked anchors correct; 0 fixes needed.**

### Relative-link check

A PowerShell script (recorded in [command-cheatsheet.md](command-cheatsheet.md)) resolved every `](...)` target in all 47 files under `docs/upskill/` against the filesystem (the `#L..` fragment stripped first). **Result: `ALL LINKS OK` — 0 broken relative links.** Re-run it after editing any doc.

## Unfilled stubs

**None.** All 47 files in the suite are filled; a scan for `TODO: not yet filled` across `docs/upskill/` returned 0 matches. The skeleton shipped complete (every file had headings + a TODO stub) and every stub was subsequently replaced with verified, anchored content.

## Coverage vs target counts (Small–Medium tier)

| Artifact | Target (S/M) | Delivered |
| --- | --- | --- |
| Key flows | 3–6 | 5 |
| Pattern cards | 6–18 | 14 |
| Comparison cards | 3–5 | 5 (incl. 1 real-vs-real, 1 migration drill) |
| Annotation drills | 4–10 | 8 |
| Trace tables | 3–6 | 5 |
| Fake-code contrasts | 4–10 | 7 |
| Review katas | 4–10 | 5 |
| Good-first tickets | 6–20 | 10 |
| Mid-level tickets | 4–12 | 5 |
| Senior projects | 2–6 | 4 |
| Debugging scenarios | 3–5 | 5 |
| Security checklist / perf / PR-RFC / interview-prep / verification log | exactly 1 each | 1 each |
