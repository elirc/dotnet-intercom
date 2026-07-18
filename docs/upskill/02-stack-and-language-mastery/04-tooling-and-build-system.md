# Tooling and Build System

## SDK pinning and the TFM

- No `global.json` exists, so the build uses whatever 8.0.x SDK is installed (8.0.422 was **verified** on the authoring machine). A team that wants reproducible builds adds a `global.json` pinning the SDK — a reasonable hardening ticket.
- Target framework, nullable, implicit usings, and `LangVersion=latest` are set once in [Directory.Build.props](../../../Directory.Build.props) and inherited by all six projects. That's why no individual csproj repeats them.

## MSBuild project graph

The [solution](../../../NetIntercom.sln) wires 6 projects; references flow Api → {Application, Infrastructure} → … → Domain (see [system map](../01-codebase-cartography/01-system-map.md)). Two SDK styles:
- `Microsoft.NET.Sdk.Web` for the API ([csproj](../../../src/NetIntercom.Api/NetIntercom.Api.csproj#L1)) — pulls in the ASP.NET shared framework.
- `Microsoft.NET.Sdk` for libraries and tests.

**Drill:** the integration test project references the **Api** project ([csproj#L16-L18](../../../tests/NetIntercom.IntegrationTests/NetIntercom.IntegrationTests.csproj#L16-L18)) and `Program` is made partial for `WebApplicationFactory` ([Program.cs#L196](../../../src/NetIntercom.Api/Program.cs#L196)). Explain why that single `public partial class Program {}` line is what makes full-stack testing possible.

## NuGet — decentralized versions

There is **no** `Directory.Packages.props`, so package versions live in each csproj. They cluster around mid-2024 (EF Core 8.0.8, MediatR 12.4.0, FluentValidation 11.9.2, Hangfire 1.8.14). **Failure mode:** without central package management, two projects can reference different versions of the same package and you won't notice until a runtime `MethodNotFound`. At this size it's fine; the switch signal is "more than a couple of projects sharing a drifting dependency." **Drill:** check that both `NetIntercom.Application` and `NetIntercom.Infrastructure` reference the *same* `Microsoft.EntityFrameworkCore` version (8.0.8). What would a mismatch cause?

## EF Core tooling and the migration gap

The repo has **no `Migrations/` folder**. Startup bootstraps the schema with `EnsureCreated` (or applies migrations if any exist — [Program.cs#L147-L165](../../../src/NetIntercom.Api/Program.cs#L147-L165)). Consequences to understand:
- `EnsureCreated` builds the schema from the model in one shot — **no upgrade path**. You cannot evolve a populated database with it; you'd drop and recreate.
- The README documents the switch: install `dotnet-ef`, `dotnet ef migrations add Initial`, and startup then calls `MigrateAsync`.
- This is a *deliberate dev shortcut* that production work must replace — covered as a real exercise in the [data-model doc](../03-architecture-and-patterns/02-data-model-and-persistence.md) and senior project.

**Drill (inferred — `dotnet-ef` not installed at authoring):** write the exact commands to add the first migration and the one-line behavioral difference at startup once a `Migrations/` folder exists.

## Frontend toolchain

Both apps are Vite + React + TypeScript; build is `tsc -b && vite build` ([inbox package.json](../../../frontend/inbox/package.json#L7), [widget package.json](../../../frontend/widget/package.json#L7)). The `tsc -b` step means **type errors fail the build** — TypeScript is a gate, not a suggestion.

Two different build *targets*:
- **Inbox** ([vite.config.ts](../../../frontend/inbox/vite.config.ts)) — a standard SPA, dev server on 5173 proxying `/api` and `/hubs` (with `ws: true` for SignalR) to the API. Route-level code splitting via `lazy()` ([App.tsx#L6-L9](../../../frontend/inbox/src/App.tsx#L6-L9)).
- **Widget** ([vite.config.ts](../../../frontend/widget/vite.config.ts)) — a **library build** producing one self-contained IIFE (`netintercom-widget.js`) with React bundled in and `process.env.NODE_ENV` defined to `"production"`. This is why the widget hand-rolls fetch state instead of adding TanStack Query: every dependency inflates a script that host sites load on every page ([comparison card 5](../03-architecture-and-patterns/07-comparative-implementations.md)).

**Drill:** open both `vite.config.ts` files. The widget sets `build.lib` and `formats: ["iife"]`; the inbox sets neither. Explain what each output is *for* and why the widget can't be a normal SPA bundle.

## CI

[.github/workflows/ci.yml](../../../.github/workflows/ci.yml) runs two jobs: `backend` (restore → Release build → unit tests → integration tests with Testcontainers) and a `frontend` **matrix** over `[inbox, widget]` (install + build). Note CI runs the integration suite — so Docker-in-CI is the supported path even though it was unavailable locally. **Drill:** the local machine couldn't run integration tests (no Docker daemon). Does CI catch what you can't locally? (Yes — that's the safety net; it's also why the integration tests must stay deterministic, since they're the gate.)
