# Command Cheatsheet

**verified** = actually run while writing these docs (output in the [verification log](verification-log.md)). **inferred** = not run (usually because Docker was down or a tool wasn't installed) but derived from the repo's config/README.

> On the authoring machine `dotnet` was **not on PATH**; it lives at `~\.dotnet\dotnet.exe`. Commands below assume `dotnet` resolves — if not, prefix with the full path (`& "$env:USERPROFILE\.dotnet\dotnet.exe" ...`).

## Build & test

| Command | Purpose | Status |
| --- | --- | --- |
| `dotnet build NetIntercom.sln` | build all 6 projects | **verified** (0 warn/err, SDK 8.0.422) |
| `dotnet test tests/NetIntercom.UnitTests` | domain + app logic | **verified** (27/27, ~225ms) |
| `dotnet test tests/NetIntercom.UnitTests --filter "FullyQualifiedName~ConversationTests"` | one class | inferred |
| `dotnet test tests/NetIntercom.UnitTests --filter "DisplayName~Snooze_InThePast_Throws"` | one test | inferred |
| `dotnet test tests/NetIntercom.IntegrationTests` | full-stack + Postgres | inferred (needs Docker) |
| `dotnet build NetIntercom.sln -c Release` | CI-style build | inferred (CI does this) |

## Run locally

| Command | Purpose | Status |
| --- | --- | --- |
| `docker compose up -d` | Postgres 16 on :5432 | inferred (daemon was down) |
| `dotnet run --project src/NetIntercom.Api` | API on :5179; creates schema + seeds | inferred |
| `cd frontend/inbox; npm install; npm run dev` | inbox SPA on :5173 | inferred |
| `cd frontend/widget; npm install; npm run build` | builds `dist/netintercom-widget.js` | inferred |
| `cd frontend/widget; npm run dev` | widget dev server :5174 | inferred |

Seed logins (password `Passw0rd!`): `admin@netintercom.local`, `sara@netintercom.local`, `alex@netintercom.local`.
Endpoints: Swagger `/swagger` (dev), health `/healthz`, Hangfire `/hangfire` (admin — see R12).

## EF / database

| Command | Purpose | Status |
| --- | --- | --- |
| `dotnet tool install -g dotnet-ef` | install EF tooling | inferred (not installed at authoring) |
| `dotnet ef migrations add Initial -p src/NetIntercom.Infrastructure -s src/NetIntercom.Api` | first migration (replaces EnsureCreated) | inferred |
| `dotnet ef database update -p src/NetIntercom.Infrastructure -s src/NetIntercom.Api` | apply migrations | inferred |
| `dotnet list package --vulnerable` | dependency CVE scan | inferred (run before release) |

> The repo currently has **no `Migrations/` folder** and bootstraps via `EnsureCreated` ([Program.cs#L147-L165](../../../src/NetIntercom.Api/Program.cs#L147-L165)). Adding migrations switches startup to `MigrateAsync`.

## Debugging probes

| Action | How | Status |
| --- | --- | --- |
| See EF SQL | set `Microsoft.EntityFrameworkCore: Information` in [appsettings.json](../../../src/NetIntercom.Api/appsettings.json#L22-L31) | inferred |
| Trace a request in logs | grab `X-Correlation-Id` from the response, grep logs | inferred |
| Inspect a hub | browser devtools → WS frames on `/hubs/*` | inferred |
| Query the DB | `psql -h localhost -U netintercom -d netintercom` (pwd `netintercom`) | inferred |

## Exploration (used while authoring)

| Command | Purpose | Status |
| --- | --- | --- |
| `Get-ChildItem -Recurse -File ... -notmatch '\\(bin\|obj\|node_modules)\\'` | file inventory | **verified** |
| `($cs \| Get-Content \| Measure-Object -Line).Lines` | LOC count (4686, excl. build) | **verified** |
| `git log --oneline` | history (empty — synthetic) | **verified** |

## The docs' own link/anchor audit (lives in the log, not product code)

```powershell
# Resolve every markdown link target under docs/upskill against the filesystem:
Get-ChildItem -Recurse docs/upskill -Filter *.md | ForEach-Object {
  $dir = $_.DirectoryName
  Select-String -Path $_.FullName -Pattern '\]\(([^)]+)\)' -AllMatches |
    ForEach-Object { $_.Matches } | ForEach-Object {
      $target = ($_.Groups[1].Value -split '#')[0]
      if ($target -and $target -notmatch '^https?:') {
        $resolved = Join-Path $dir $target
        if (-not (Test-Path $resolved)) { "BROKEN: $($_.Groups[1].Value)  in  $($_.Filename)" }
      }
    }
}
```
Result of running this is recorded in the [verification log](verification-log.md) self-audit section.
