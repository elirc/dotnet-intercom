# Checklist: verifying AI-written changes in NetIntercom

Tick a box only after doing it yourself.

## Run
- [ ] `dotnet build NetIntercom.sln`: 0 errors, and no *new* warnings (compare the warning count with `main`).
- [ ] `dotnet test tests/NetIntercom.UnitTests`: the count went **up** by the number of tests the agent claims to have added.
- [ ] `dotnet test tests/NetIntercom.IntegrationTests` with Docker running. If Docker isn't available, write "integration UNVERIFIED". Don't accept the agent's "passes".
- [ ] Frontend touched → `npm run build` in `frontend/widget` and/or `frontend/inbox` (`tsc -b` is the only type check CI runs).

## Read the diff (all of it)
- [ ] `git diff main --stat`: every file is one the spec mentions, or you can say why it's there.
- [ ] Nothing unrelated was reformatted or renamed. (Formatting-only noise hides real changes.)
- [ ] No new package references in `*.csproj` or `package.json` unless the spec allowed them.
- [ ] No `appsettings*.json` change that would be live outside Development (this repo already has `Seed:Enabled=true` in the base file, so don't add another like it).

## Check the claims against the code
- [ ] For every sentence in the agent's summary, find the line that makes it true. If you can't find it, the claim is false.
- [ ] "Handles concurrency": is there a **database** constraint, or only an `AnyAsync` pre-check?
- [ ] "Tests cover X": open the test and find the assertion for X. Look out for names like `…_Works` that only assert a 200.
- [ ] Any new widget/visitor path: does it re-check ownership against `VisitorId` like `WidgetQueries.cs:25-30`?
- [ ] Any new DTO field: does it reach a visitor surface (`RealtimeNotifier` visitor payloads, `/widget/session`, `/widget/messages`)?

## Look for fake or placeholder behavior
- [ ] `catch { }`, `catch (Exception) { return default; }`, `.catch(() => {})` added around the new code.
- [ ] `// TODO`, `NotImplementedException`, hard-coded ids or GUIDs, `Task.Delay` used to "fix" a race.
- [ ] Tests that mock the thing under test (e.g. a mocked `IAppDbContext` "proving" a unique constraint).
- [ ] UI that renders a state the backend never sends (see the empty `<span />` at `Widget.tsx:128` on `main`).

## Schema and rollback
- [ ] Does the change need schema? Remember `EnsureCreated` never alters an existing DB (`Program.cs:150-157`). Is the deployment path written down?
- [ ] Is it reversible? Can the old API run against the new schema (nullable column, no rename) so that a rollback is safe?
- [ ] Indexes on big tables: `CONCURRENTLY`, outside a transaction.

## Operate
- [ ] Is there a log line or metric that shows the new behavior working in production (e.g. "idempotent replay served")?
- [ ] Is there an Error-level log for something that is actually expected? (`LoggingBehavior` already over-logs.)
