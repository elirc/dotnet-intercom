# Pattern Catalog

Fourteen patterns this repo actually uses, as cards. "Second" = a second real occurrence, so you learn the pattern, not the instance. Failure modes are general *and* checked against this code.

---

## Pattern 1: Vertical slice per use case (CQRS-lite)

**Problem:** layered codebases scatter one feature across Controllers/, Services/, Validators/, Dtos/ — every change touches five folders.
**General shape:** one file = command/query record + validator + handler. Controllers are one-line dispatchers to MediatR.
**Real example:** [`SendVisitorMessage.cs`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L16-L115) — record, validator, handler in one file.
**Second:** [`Login.cs`](../../../src/NetIntercom.Application/Auth/Login.cs#L12-L60).
**Why it works:** the unit of change matches the unit of reading; new use case = new file, no merge conflicts in god-services.
**Failure modes:** shared logic duplicates across slices (see Pattern 14's counterexample); handlers grow into transaction scripts that bypass the domain.
**Use when:** request/response use cases with distinct rules. **Avoid when:** two slices must share a transaction or invariant — push that into the domain instead.
**Drill:** find every handler that loads a `Conversation` with the same 4 `.Include` calls. Count them, then read [`AssignConversationHandler.LoadAsync`](../../../src/NetIntercom.Application/Conversations/ManageConversation.cs#L58-L65) — the repo's own half-step toward deduplication. Would you finish the job? What does a shared loader cost?

## Pattern 2: Cross-cutting via pipeline behaviors

**Problem:** validation and logging repeated in every handler.
**General shape:** `IPipelineBehavior<TReq,TRes>` decorators registered once, wrap every MediatR request.
**Real example:** [`ValidationBehavior`](../../../src/NetIntercom.Application/Common/Behaviors.cs#L7-L32) — runs all validators for the request type, throws aggregate `ValidationException`.
**Second:** [`LoggingBehavior`](../../../src/NetIntercom.Application/Common/Behaviors.cs#L34-L56); both registered in [`AddApplication`](../../../src/NetIntercom.Application/DependencyInjection.cs#L11-L19).
**Why it works:** handlers can assume valid input; "validation happens" is enforced by registration, not discipline.
**Failure modes:** behavior order = registration order — a behavior added before validation sees raw input; exceptions as control flow allocate on hot paths (irrelevant at this scale).
**Use when:** the concern is truly universal. **Avoid when:** only some requests need it — explicit calls beat conditional behaviors.
**Drill:** registration order is Logging then Validation. Which wraps which? Does a validation failure get logged as an error? Confirm against the `catch` filter at [Behaviors.cs#L50](../../../src/NetIntercom.Application/Common/Behaviors.cs#L50).

## Pattern 3: Invariants live on the entity

**Problem:** state transitions enforced "wherever someone remembered" drift apart.
**General shape:** private setters + intention-revealing methods that throw `DomainException` on illegal transitions.
**Real example:** [`Conversation`](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L36-L127) — `State`, `FirstAgentReplyAtUtc`, `ClosedAtUtc` are `private set`; `Close/Snooze/Reopen/RecordAgentReply/RecordVisitorMessage` are the only doors.
**Second:** [`SlaTimer.MarkMet/MarkBreached/Cancel`](../../../src/NetIntercom.Domain/Entities/Sla.cs#L31-L62).
**Why it works:** the compiler enforces the state machine; handlers can't half-close a conversation.
**Failure modes:** anemic leakage — `TeamId`, `AssigneeId`, `LastMessageAtUtc` are still public-settable and handlers mutate them directly ([SendVisitorMessage.cs#L103-L104](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L103-L104)); the boundary is partial.
**Use when:** there's a real state machine. **Avoid when:** the entity is a bag of attributes — methods for ceremony's sake help nobody.
**Drill:** list which `Conversation` properties are mutable from outside and decide, for each, whether that's a leak or pragmatism. Defend one of each in writing.

## Pattern 4: Ports owned by the application layer

**Problem:** business logic needs DB/clock/realtime/presence without referencing EF, SignalR, or the API project.
**General shape:** interfaces declared in Application ([`Interfaces.cs`](../../../src/NetIntercom.Application/Common/Interfaces.cs#L8-L73)); implementations registered by the host: EF in [Infrastructure DI](../../../src/NetIntercom.Infrastructure/DependencyInjection.cs#L14-L34), SignalR/presence in [Program.cs#L37-L41](../../../src/NetIntercom.Api/Program.cs#L37-L41).
**Why it works:** dependency arrows point inward; Application compiles without ASP.NET.
**Failure modes:** `IAppDbContext` exposes `DbSet`/`IQueryable`, so EF semantics (tracking, translation limits) leak through the port — it's a *testing seam*, not persistence-ignorance. Know which one you have.
**Use when:** you need substitutability. **Avoid when:** the abstraction would mirror one concrete class forever while hiding its real semantics.
**Drill:** which interface in `Interfaces.cs` would be hardest to fake in a unit test, and why? (Hint: which one returns `IQueryable`-backed `DbSet`s?)

## Pattern 5: Static factory mapping (`Dto.From`)

**Problem:** entity → wire-shape mapping without reflection magic.
**General shape:** records with a static `From(entity)`; nesting composes factories.
**Real example:** [`ConversationDto.From`](../../../src/NetIntercom.Application/Dtos.cs#L67-L77) — it also *computes* (`ActiveSlaTimer`, `HasBreachedSla`), not just copies.
**Second:** [`TeamDto.From`](../../../src/NetIntercom.Application/Dtos.cs#L18-L23) with an injected presence lookup.
**Why it works:** compile-time safe, greppable, debuggable; unlike AutoMapper, renames can't silently break a convention.
**Failure modes:** `From` quietly *requires* navigations to be loaded — it dereferences `c.Visitor` and enumerates `c.SlaTimers`; call it on a partially-loaded entity and you get a `NullReferenceException` or wrong (empty) timer data, not a compile error. Every caller must honor the Include contract (Pattern 14).
**Use when:** mapping count is manageable. **Avoid when:** dozens of trivial 1:1 mappings and a team already fluent in a mapper.
**Drill:** `MessageDto.From` takes `senderName` as a parameter instead of computing it. Compare what's loaded in [GetConversationMessages](../../../src/NetIntercom.Application/Conversations/GetConversation.cs#L49-L58) vs [SendAgentMessage](../../../src/NetIntercom.Application/Conversations/SendAgentMessage.cs#L69) and explain the design.

## Pattern 6: Typed exceptions → RFC 7807 at one chokepoint

**Problem:** consistent error responses without try/catch in every controller.
**General shape:** semantic exceptions ([`Exceptions.cs`](../../../src/NetIntercom.Application/Common/Exceptions.cs#L3-L16), [`DomainException`](../../../src/NetIntercom.Domain/Common/DomainException.cs)) thrown anywhere; one `IExceptionHandler` maps to status codes.
**Real example:** [`ApiExceptionHandler.TryHandleAsync`](../../../src/NetIntercom.Api/Infrastructure/ApiExceptionHandler.cs#L16-L46) — ValidationException → 400 with field dictionary, DomainException → 422, unknown → logged 500 with correlation id.
**Second:** the correlation id comes from [`CorrelationIdMiddleware`](../../../src/NetIntercom.Api/Infrastructure/CorrelationIdMiddleware.cs#L13-L26).
**Why it works:** handlers state *what* went wrong; HTTP mapping is policy, decided once.
**Failure modes:** new exception types silently fall through to 500 if the switch isn't extended; exceptions for *expected* outcomes cost clarity (see comparison card 1 on result types).
**Use when:** errors are exceptional. **Avoid when:** the caller routinely branches on failure kinds.
**Drill:** add (on paper) a `ConflictException` for optimistic-concurrency failures. List every file you'd touch. Now you know this pattern's change-cost shape.

## Pattern 7: Authorization in the WHERE clause

**Problem:** resource-level authorization ("this visitor may only see *their* conversation") — roles alone can't express it.
**General shape:** identity from claims; the ownership predicate is part of the query itself. No row = 404/403; there is no path that loads first and checks later.
**Real example:** [`GetVisitorMessagesHandler`](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L23-L40) — `AnyAsync(c.Id == … && c.VisitorId == …)` before reading messages.
**Second:** [`SendVisitorMessageHandler`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L55-L62) folds ownership into the load; [`VisitorHub.JoinConversation`](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L37-L46) repeats it for hub groups.
**Why it works:** you can't forget to check what you structurally can't load.
**Failure modes:** the pattern only protects paths that use it — [`VisitorHub.Typing`](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L48-L58) skips the ownership check (risk register R4), and the agent side deliberately has no scoping at all (whole-inbox model, R2).
**Use when:** always, for resource access. **Avoid when:** never — but make each endpoint's scoping story explicit.
**Drill:** audit all six widget-facing operations (4 REST in [WidgetController](../../../src/NetIntercom.Api/Controllers/WidgetController.cs#L33-L60), 2 hub methods). Build the table: operation × ownership-checked? You'll find the gap yourself.

## Pattern 8: Audit + soft delete in one `SaveChangesAsync` override

**Problem:** every entity needs CreatedAt/UpdatedAt; some must never hard-delete.
**General shape:** override `SaveChangesAsync`, walk the ChangeTracker: stamp `AuditableEntity`, convert deletes of `ISoftDeletable` to modifies; pair with a global query filter.
**Real example:** [`AppDbContext.SaveChangesAsync`](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L123-L149) + [`HasQueryFilter(r => !r.IsDeleted)`](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L108-L114) on `CannedReply`.
**Second:** none — one soft-deletable entity so far; the mechanism is generic via [`ISoftDeletable`](../../../src/NetIntercom.Domain/Common/Entity.cs#L14-L18).
**Why it works:** impossible to forget; behavior travels with the contract, not the call site.
**Failure modes:** query filters exclude rows in joins too; `IgnoreQueryFilters()` escapes get forgotten in admin/restore paths; unique indexes still see soft-deleted rows. Note the comment at [CannedReplies.cs#L118](../../../src/NetIntercom.Application/CannedReplies/CannedReplies.cs#L118) says "interceptor" — it's actually this override. Comments drift; code doesn't.
**Use when:** recoverability matters. **Avoid when:** legal deletion (GDPR) is required — soft delete becomes a liability.
**Drill:** `HasIndex(r => r.Shortcut)` is non-unique. What breaks if you make it unique while soft-deleted duplicates exist? Write the fixed definition (filtered unique index).

## Pattern 9: Persist-then-notify; hubs are delivery-only

**Problem:** realtime systems that treat the socket as source of truth lose messages on every reconnect.
**General shape:** all writes via REST → DB commit → *then* SignalR fan-out; clients reconcile after reconnect by refetching `?after=lastSeen`.
**Real example:** [`SendVisitorMessageHandler`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L107-L112) (save L107, notify L111-112); catch-up: [Widget.tsx#L52-L58](../../../frontend/widget/src/Widget.tsx#L52-L58), inbox `invalidateQueries()` on reconnect ([Inbox.tsx#L73-L76](../../../frontend/inbox/src/pages/Inbox.tsx#L73-L76)).
**Second:** [`CheckSlaBreachesHandler`](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L51-L59) — same ordering in the background path.
**Why it works:** the hub can drop anything and the system stays correct; "missed" only means "late."
**Failure modes:** notify throws after a successful commit → caller sees 500 for a write that happened (no outbox — comparison card 4); duplicate delivery (HTTP response *and* hub event) requires client dedupe by id — both clients do it ([Widget.tsx#L19-L21](../../../frontend/widget/src/Widget.tsx#L19-L21), [Inbox.tsx#L42-L44](../../../frontend/inbox/src/pages/Inbox.tsx#L42-L44)).
**Use when:** correctness > latency. **Avoid when:** the stream *is* the product (collaborative cursors) — then you need op-logs/CRDTs.
**Drill:** breakpoint between save and notify, then kill the connection. Enumerate what each client shows and how each recovers. Which recovery is the 30s poll ([Inbox.tsx#L32](../../../frontend/inbox/src/pages/Inbox.tsx#L32)) quietly responsible for?

## Pattern 10: Thin job shim over MediatR

**Problem:** background-job logic welded to the scheduler is untestable and unportable.
**General shape:** the scheduler-visible class is 3 lines; all logic in a normal command handler.
**Real example:** [`SlaBreachJob`](../../../src/NetIntercom.Infrastructure/BackgroundJobs/SlaBreachJob.cs#L10-L17) → [`CheckSlaBreachesCommand`](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L14-L16).
**Second:** none — one job. The seam is ready for the snooze-wake job (ticket M2).
**Why it works:** swap Hangfire for Quartz/K8s CronJob by rewriting 3 lines; test the handler like any other.
**Failure modes:** job-level concerns (retry policy, timeouts) have nowhere to live; here `Hangfire:Enabled` gates the whole subsystem ([Program.cs#L133](../../../src/NetIntercom.Api/Program.cs#L133)) and tests turn it off ([ApiFactory.cs#L26](../../../tests/NetIntercom.IntegrationTests/ApiFactory.cs#L26)).
**Use when:** always, for scheduled work.
**Drill:** Hangfire retries failed jobs. Suppose `CheckSlaBreachesHandler` half-completes (saved, not notified) and the retry runs. Is the handler idempotent? Prove it from the `Status == Running` filter at [CheckSlaBreaches.cs#L39](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L39).

## Pattern 11: Rotate-and-hash refresh tokens

**Problem:** long-lived credentials stored server-side are a breach jackpot; stolen ones should be detectable.
**General shape:** opaque random token to the client; only the SHA-256 hash stored; each use revokes the presented token and issues a new pair.
**Real example:** [`RefreshTokenCommandHandler.Handle`](../../../src/NetIntercom.Application/Auth/Refresh.cs#L31-L56); hashing in [`TokenService.HashRefreshToken`](../../../src/NetIntercom.Infrastructure/Auth/TokenService.cs#L77-L78).
**Second:** issuance in [`LoginCommandHandler`](../../../src/NetIntercom.Application/Auth/Login.cs#L48-L56).
**Why it works:** a DB leak exposes hashes, not tokens; reuse of a rotated token is a theft signal — though this repo doesn't *act* on the signal (no token-family revocation).
**Failure modes:** rotation races with concurrent requests (Flow 2 in [key flows](../01-codebase-cartography/05-key-flows.md)); revoked/expired rows accumulate (ticket G6).
**Use when:** SPA + JWT. **Avoid when:** cookie sessions would dissolve the whole problem (comparison card 3).
**Drill:** an attacker steals a refresh token while the user keeps using the app. Walk both token timelines until the attacker is locked out — are they ever? What one extra column enables family-wide revocation on reuse?

## Pattern 12: Derived-on-read state + reconciliation sweep

**Problem:** presence has continuous semantics ("offline after 60s of silence") but inputs are discrete events.
**General shape:** store raw observations (connections, timestamps); *compute* status at read time; a periodic sweeper diffs computed state, broadcasts and persists transitions.
**Real example:** [`PresenceTracker.Compute`](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L76-L90) (derive) + [`PresenceSweeper.SweepAsync`](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L56-L87) (reconcile).
**Second:** `ConversationDto.ActiveSlaTimer`/`HasBreachedSla` apply the same idea to timers ([Dtos.cs#L72-L76](../../../src/NetIntercom.Application/Dtos.cs#L72-L76)) — derived at mapping time, never stored.
**Why it works:** no stored status to go stale; diff-based transitions are exactly-once per change.
**Failure modes:** derived reads must stay cheap (this one is O(agents)); the sweep interval bounds notification latency (15s); memory-only observations die with the process.
**Use when:** state = f(observations, now). **Avoid when:** state changes must be transactional with other writes.
**Drill:** why does [`OnConnectedAsync`](../../../src/NetIntercom.Api/Realtime/AgentHub.cs#L27-L33) broadcast Online immediately while disconnects wait for the sweeper? Describe the user-visible behavior if it were symmetric in both directions.

## Pattern 13: Clamped pagination helper

**Problem:** every list endpoint reinventing skip/take, with `pageSize=100000` as a free DoS.
**General shape:** one static `CreateAsync(IQueryable, page, pageSize)` that clamps inputs and returns items + count.
**Real example:** [`PaginatedList<T>.CreateAsync`](../../../src/NetIntercom.Application/Common/PaginatedList.cs#L21-L28) — `pageSize` clamped to 1..100.
**Second:** consumed by [`ListConversationsHandler`](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L74-L76).
**Why it works:** the guard travels with the helper.
**Failure modes:** two round-trips (COUNT + page); offset pagination drifts under concurrent inserts and degrades on deep pages (keyset is the upgrade, ticket M5).
**Use when:** admin/inbox lists. **Avoid when:** infinite scroll over fast-moving data — use cursors.
**Drill:** the inbox refetches page 1 every 30s while new conversations arrive. What anomaly does offset pagination cause on page 2? (Items repeat or skip as offsets shift.)

## Pattern 14: Shared loader for a required include-graph (and its absence)

**Problem:** `ConversationDto.From` requires Visitor+Team+Assignee+SlaTimers loaded (Pattern 5's hidden contract); five handlers need that exact graph.
**General shape:** centralize the canonical load in one helper so the Include contract has a single owner.
**Real example:** [`AssignConversationHandler.LoadAsync`](../../../src/NetIntercom.Application/Conversations/ManageConversation.cs#L58-L65) — `internal static`, reused by [`ChangeConversationStateHandler`](../../../src/NetIntercom.Application/Conversations/ManageConversation.cs#L97-L99).
**Second (the absence):** [`SendAgentMessageHandler`](../../../src/NetIntercom.Application/Conversations/SendAgentMessage.cs#L38-L44), [`GetConversationHandler`](../../../src/NetIntercom.Application/Conversations/GetConversation.cs#L20-L26) and [`SendVisitorMessageHandler`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L55-L61) each repeat the chain by hand.
**Why it works:** add a navigation to the DTO → update one loader → every consumer correct.
**Failure modes:** half-adopted (as here) it's worse than either extreme — readers assume a centralization that doesn't hold. `internal static` on a handler is also an odd home; a `ConversationQueries` helper would name the concept.
**Use when:** ≥3 call sites share a load contract. **Avoid when:** call sites genuinely need different graphs — then explicit-per-site *is* the documentation.
**Drill:** this is good-first ticket G3. Before doing it, predict: does unifying the loads change behavior anywhere? (Careful: `SendVisitorMessage` also includes `SlaPolicy` — the others don't. Why does it need it?)
