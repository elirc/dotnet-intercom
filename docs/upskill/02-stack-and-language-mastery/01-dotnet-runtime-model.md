# .NET Runtime Model

Each concept: what it is, why it matters *here*, the real code, how it fails, a drill.

## async/await and the thread pool

**What:** `await` splits a method into a state machine; the thread is released at the await point and a (possibly different) pool thread resumes the continuation.
**Why here:** every handler is async and I/O-bound — DB round-trips ([SendVisitorMessage.Handle](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L47-L115)) and SignalR sends. Releasing threads during I/O is what lets one box serve many concurrent conversations.
**Failure modes:** **sync-over-async** — calling `.Result`/`.Wait()` on these tasks would block a pool thread; under load you exhaust the pool and the API stops responding (in classic ASP.NET it would also *deadlock* on the captured `SynchronizationContext`; ASP.NET Core has none, so it "merely" starves). Grep the repo: there are **no** `.Result`/`.Wait()` calls on hot paths — confirm that's still true before you add one.
**Drill:** in [`MarkReadByAgentHandler`](../../../src/NetIntercom.Application/Conversations/ManageConversation.cs#L138-L159) every DB call is awaited. Rewrite (on paper) the `foreach` loop that stamps messages as if you (wrongly) awaited inside a `Task.Run`. What would that buy? (Nothing — it's already async; `Task.Run` here just moves work to another pool thread for no reason.)

## ConfigureAwait

**What:** `ConfigureAwait(false)` says "don't bother resuming on the captured context."
**Why here:** the repo **doesn't use it anywhere** — and that's correct for ASP.NET Core, which has no `SynchronizationContext`, so there's nothing to capture. In a *library* meant for arbitrary hosts you'd add it; in app code targeting ASP.NET Core it's noise.
**Failure mode:** cargo-culting `ConfigureAwait(false)` into app handlers adds clutter and teaches juniors a rule they'll misapply. Cargo-culting its *absence* into a library bites you on a UI thread.
**Drill:** explain to a teammate why this app correctly omits `ConfigureAwait` but the `OfficeHours`/`PlaceholderRenderer` *domain* code (if it were ever extracted into a shared NuGet package) might want it.

## CancellationToken

**What:** a cooperative cancellation signal threaded through async calls.
**Why here:** every handler takes `CancellationToken ct` and passes it to EF ([ListConversations](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L26)) — when a client disconnects or a request times out, ASP.NET cancels the token and the in-flight query is abandoned.
**Failure modes:** a token that isn't *passed through* (e.g., `ToListAsync()` with no `ct`) silently ignores cancellation — wasted DB work after the client left. The background sweeper threads its `stoppingToken` correctly so shutdown is clean ([PresenceSweeper#L40-L54](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L40-L54)).
**Drill:** find a query in the repo that does **not** pass `ct`. (Hint: check the hub methods in [VisitorHub](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L37-L58) — `AnyAsync`/`FirstOrDefaultAsync` without a token.) What's the consequence, and is it harmful here?

## Task vs ValueTask

**What:** `Task` is a heap-allocated promise; `ValueTask` avoids the allocation when the result is often synchronous.
**Why here:** the repo uses `Task` throughout — correct, because every operation actually awaits I/O (no synchronous fast path to optimize). `IExceptionHandler.TryHandleAsync` returns `ValueTask<bool>` because the framework defines it that way ([ApiExceptionHandler#L16](../../../src/NetIntercom.Api/Infrastructure/ApiExceptionHandler.cs#L16)).
**Failure modes:** `ValueTask` misuse (awaiting twice, storing it) is undefined behavior — the repo sidesteps this by not introducing it. Reaching for `ValueTask` in handlers here would be premature optimization with a correctness footgun attached.
**Drill:** argue when you *would* introduce `ValueTask` in this codebase. (Answer shape: a hot, often-cached read — e.g., if presence lookups became a method awaited per message and usually hit memory. They don't, so don't.)

## GC and allocations

**What:** generational GC reclaims short-lived objects cheaply (gen0).
**Why here:** the heavy allocators are LINQ projections + DTO mapping per request ([ConversationDto.From](../../../src/NetIntercom.Application/Dtos.cs#L67-L77) builds nested records and lists). At inbox/widget scale this is gen0 churn — fine.
**Failure modes:** the breach job's Include-heavy load ([CheckSlaBreaches#L34-L40](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L34-L40)) materializes a full entity graph per overdue timer just to build a DTO; at high timer volume that's avoidable allocation (project instead). The presence dictionaries are long-lived (gen2) and the `_lastBroadcast` map never prunes departed agents — a slow leak ([PresenceSweeper#L23](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L23)).
**Drill:** identify the one place in the repo where allocation scales with *traffic over time* rather than per-request. (The never-pruned presence maps.) Sketch the fix.

## IDisposable / using

**What:** deterministic cleanup of unmanaged or scoped resources.
**Why here:** EF `DbContext` is disposed by DI at scope end; the sweeper creates and disposes a scope per sweep with `using var scope` ([PresenceSweeper#L91](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L91)); the `PeriodicTimer` is `using`-scoped ([#L42](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L42)).
**Failure modes:** forgetting the scope in the sweeper would leak a DbContext per 15s tick — a textbook captive/leak bug; the code gets it right, which is the lesson.
**Drill:** explain why the sweeper can't just inject `AppDbContext` in its constructor like a controller can. (It's a singleton; the context is scoped — capturing it would freeze one context for the process lifetime. This is the captive dependency problem; see [framework mental models](02-framework-mental-models.md).)

## Putting it together: the concurrency hazards that aren't about threads

The subtle bugs here aren't thread-safety of a data structure — they're **logical** races across awaits and requests: the round-robin read-modify-write ([ConversationRouter#L75-L86](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L75-L86)) and the refresh-token rotation race ([Refresh.cs#L43](../../../src/NetIntercom.Application/Auth/Refresh.cs#L43)). `async`/`await` made the code non-blocking; it did *not* make these interleavings safe. Knowing the difference is the runtime-model payoff.
