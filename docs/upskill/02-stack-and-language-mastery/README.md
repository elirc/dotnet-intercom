# Module 02: Stack and Language Mastery

The stack, taught *through this repo*. Every concept points at a real file — if a section can't anchor a claim here, it's not in this module. The goal is transferable: after this you should spot the same patterns (and traps) in any .NET 8 + React codebase.

| Page | Covers |
| --- | --- |
| [01-dotnet-runtime-model.md](01-dotnet-runtime-model.md) | CLR/JIT, GC & allocations, async/await, Task vs ValueTask, ConfigureAwait, sync-over-async, CancellationToken, IDisposable |
| [02-framework-mental-models.md](02-framework-mental-models.md) | ASP.NET Core middleware pipeline + DI lifetimes (+ captive dependency), model binding/validation, filters/options; React render vs commit, hooks, effects/cleanup, stale closures, keys |
| [03-type-system-and-contracts.md](03-type-system-and-contracts.md) | Generics, nullable reference types, records, pattern matching, structs vs classes, LINQ deferred execution; TS types and the wire contract |
| [04-tooling-and-build-system.md](04-tooling-and-build-system.md) | SDK pinning, MSBuild/Directory.Build.props, NuGet, EF tooling; npm + Vite bundling, the IIFE widget build |

Read these alongside the [code-reading gym](../04-code-reading-gym/README.md) — the concepts here become the annotation drills there.
