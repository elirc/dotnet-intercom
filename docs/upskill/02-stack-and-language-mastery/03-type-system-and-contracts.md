# Type System and Contracts

## Nullable reference types as documentation

`<Nullable>enable</Nullable>` is set globally ([Directory.Build.props](../../../Directory.Build.props)). That turns every reference type's nullability into a *stated contract*:

- `Guid? TeamId` on [Conversation](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L10) means "unrouted is a legal state" — the compiler forces callers to handle it.
- `string Body = string.Empty` ([Message#L15](../../../src/NetIntercom.Domain/Entities/Message.cs#L15)) declares non-null with a default, so EF materialization and `new` both satisfy it.
- `Visitor Visitor { get; set; } = null!;` ([Conversation#L8](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L8)) is the EF idiom: "this navigation is non-null *once loaded*; trust me." The `null!` is a promise the [Include contract](../03-architecture-and-patterns/05-pattern-catalog.md) must keep — break it and you get the NRE the type system was told to ignore.

**Failure mode:** `null!` is an escape hatch that moves a runtime risk past the compiler. The repo uses it correctly for EF navigations but it's exactly where `ConversationDto.From` can NRE if a caller forgets an Include. **Drill:** list every `= null!` in the domain entities and, for each, name the code that's responsible for fulfilling the non-null promise.

## Records, value equality, and `with`

DTOs and commands are positional records ([Dtos.cs](../../../src/NetIntercom.Application/Dtos.cs#L6-L94), [SendVisitorMessageCommand](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L19-L24)). What this buys:
- **Value equality** — two `MessageDto` with the same fields are equal; useful for client dedupe semantics.
- **Immutability** — a command can't be mutated mid-pipeline by a behavior.
- **Greppable contracts** — the positional parameter list *is* the API shape.

Entities are **classes**, deliberately — they need identity equality (two `Conversation`s with equal fields are still different rows) and mutable, EF-tracked state. **Failure mode:** a record with a collection has value equality that compares the *reference* of the list — a silent lie. None of the DTOs nest mutable collections in a way that bites, but [`TeamDto`](../../../src/NetIntercom.Application/Dtos.cs#L18-L23) carries `IReadOnlyList<AgentDto>` — equality over it is reference-based. **Drill:** would using a record for `Conversation` break EF change tracking? Explain in terms of identity vs value semantics.

## Generics

Two instructive uses:
- [`PaginatedList<T>`](../../../src/NetIntercom.Application/Common/PaginatedList.cs) — a generic container with a static `CreateAsync` factory constrained to `IQueryable<T>`. One implementation, every list endpoint.
- [`IPipelineBehavior<TRequest,TResponse>`](../../../src/NetIntercom.Application/Common/Behaviors.cs#L7) — open generic registered once ([AddApplication#L15-L16](../../../src/NetIntercom.Application/DependencyInjection.cs#L15-L16)) and closed by MediatR per request type. The `where TRequest : notnull` constraint is the kind of detail that matters: it lets the behavior assume a non-null request.

**Drill:** `PaginatedList<ConversationDto>` is built by mapping a `PaginatedList<Conversation>` ([ListConversations#L74-L76](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L74-L76)). Why map *after* paginating rather than projecting to DTO inside the query? (Trade-off: post-map keeps the query on entities EF can translate; in-query projection would be more efficient but the mapping computes from loaded navigations — connect this to performance thinking.)

## Pattern matching

The exception-to-status mapper is a `switch` expression over types with `when` filters absent but property patterns present ([ApiExceptionHandler#L18-L33](../../../src/NetIntercom.Api/Infrastructure/ApiExceptionHandler.cs#L18-L33)); the soft-delete branch uses a property pattern `entry is { State: EntityState.Deleted, Entity: ISoftDeletable soft }` ([AppDbContext#L140](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L140)). The state-change handler switches on an enum ([ManageConversation#L102-L113](../../../src/NetIntercom.Application/Conversations/ManageConversation.cs#L102-L113)).
**Failure mode:** non-exhaustive switches. The exception switch's `_ => null` arm is the safety net that turns *unmapped* exceptions into a logged 500 — without it, a new exception type would fall through unhandled. **Drill:** add a `ConflictException` arm (risk R13) and decide whether the `_ => null` default should stay.

## LINQ deferred execution

The single most important EF behavior to internalize here: an `IQueryable` is a *query description*, not data, until a terminal op (`ToListAsync`, `AnyAsync`, `FirstOrDefaultAsync`, `CountAsync`) runs it. [`ListConversationsHandler`](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L28-L73) builds the query in stages — `.Where` per filter, then `.OrderBy`, then hands the *still-unexecuted* `IQueryable` to `PaginatedList.CreateAsync`, which appends `Skip/Take` and finally executes. All of it becomes **one** SQL statement.

**Failure modes:**
- Calling `.ToList()` too early turns the rest of the pipeline into in-memory LINQ over the whole table (silent full scan). The repo gets the boundary right — but [`GetSlaReportHandler`](../../../src/NetIntercom.Application/Reports/Reports.cs#L113-L138) intentionally materializes then computes percentages in memory; understand which side of the line each `ToListAsync` sits on.
- `IEnumerable` vs `IQueryable`: once you're on `IEnumerable`, filtering runs client-side. [`PickRoundRobinAsync`](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L77-L85) deliberately `ToListAsync()`s members *then* filters by the in-memory online set — because "online" lives in process memory, not the DB, so it *can't* be translated to SQL. That's a correct, conscious client-side filter.

**Drill:** classify every `ToListAsync` in [Reports.cs](../../../src/NetIntercom.Application/Reports/Reports.cs) as "executes the final query" vs "materializes early to compute in memory," and for each early one, say why it can't be done in SQL.

## The wire contract: C# records ↔ TypeScript interfaces

The same shapes exist twice — [Dtos.cs](../../../src/NetIntercom.Application/Dtos.cs) and [frontend/inbox/src/types.ts](../../../frontend/inbox/src/types.ts) — hand-kept in sync (no codegen). The serializer uses web defaults (camelCase) so `LastMessageAtUtc` → `lastMessageAtUtc`, enums serialize as **strings** (`"Open"`, see [HappyPathTests reading `state=Open`](../../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs#L68)). **Failure mode:** the two definitions drift silently — rename a C# field, the TS `interface` still compiles against `undefined`. **Drill:** find one field present in the C# `ConversationDto` ([Dtos.cs#L48-L66](../../../src/NetIntercom.Application/Dtos.cs#L48-L66)) and confirm its presence and type in [types.ts](../../../frontend/inbox/src/types.ts#L32-L50). Then argue: is hand-sync acceptable at this size, and what's the switch signal to introduce OpenAPI codegen?
