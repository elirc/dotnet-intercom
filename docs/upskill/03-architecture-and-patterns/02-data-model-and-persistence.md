# Data Model and Persistence

All mappings are in one file: [`AppDbContext.OnModelCreating`](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L30-L121). Read it once end to end — it's the schema.

## Entities and relationships

```
Visitor 1───* Conversation *───1 Team 1───* TeamMember *───1 AgentUser
                  │                  │                            │
                  ├──* Message       └──1 SlaPolicy (default)     ├──* RefreshToken
                  ├──* SlaTimer                                   └──1 PresenceSnapshot
                  ├──0..1 Assignee (AgentUser)
                  └──0..1 SlaPolicy
RoutingRule *───1 Team
CannedReply ──0..1 Team / 0..1 AgentUser  (soft-deletable)
```

Key cardinalities and their FK delete behavior ([AppDbContext#L70-L120](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L70-L120)):
- Conversation → Team/Assignee/SlaPolicy are all `OnDelete(SetNull)` — deleting a team orphans (un-teams) conversations rather than cascading. Deliberate: you don't want deleting a team to delete chat history.
- Conversation → Visitor is the default (cascade-ish) required relationship.
- Message → Conversation required; Message → AgentUser `SetNull` (deleting an agent keeps their messages, drops attribution).

## Indexes (and what they're for)

| Index | Entity | Serves |
| --- | --- | --- |
| `AnonymousId` unique, `ExternalId` | Visitor | session resume lookup ([StartConversation#L51-L52](../../../src/NetIntercom.Application/Widget/StartConversation.cs#L51-L52)) |
| `Email` unique | AgentUser | login ([Login#L40-L41](../../../src/NetIntercom.Application/Auth/Login.cs#L40-L41)) |
| `TokenHash` unique | RefreshToken | rotation lookup ([Refresh#L34-L36](../../../src/NetIntercom.Application/Auth/Refresh.cs#L34-L36)) |
| `(State, LastMessageAtUtc)` | Conversation | the default inbox sort/filter ([ListConversations#L71](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L71)) |
| `AssigneeId`, `TeamId` | Conversation | "mine"/team filters |
| `(ConversationId, CreatedAtUtc)` | Message | transcript ordering + `after` catch-up |
| `(Status, BreachAtUtc)` | SlaTimer | the breach scan ([CheckSlaBreaches#L39](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L39)) |
| `AgentUserId` unique | PresenceSnapshot | upsert per agent |

**Strong point:** the indexes match the actual query shapes — the breach scan, inbox sort, and transcript order all have covering-ish composite indexes. That's not accidental; someone read the queries.
**Gaps to notice:** the `slaBreach` sort ([ListConversations#L67-L70](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L67-L70)) computes `Min(BreachAtUtc)` over a filtered subquery per row — no index helps that; the `Search` filter does `ToLower().Contains` (leading-wildcard `LIKE`) which **can't use an index** at all. Both are fine at seed scale, both are the first things to hurt.

## Transactions and the unit of work

EF's `SaveChangesAsync` is the transaction boundary. Each handler does *one* `SaveChanges`, so multi-row writes are atomic:
- [`SendVisitorMessage`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L107) commits conversation + message + SLA timer + visitor update together.
- The router mutates the conversation and a `TeamMember.LastAssignedAtUtc` ([ConversationRouter#L66](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L66)) — saved by the *caller's* single SaveChanges, so assignment and message land in one transaction. Good.

**What's missing:** no explicit transaction spans *multiple* SaveChanges anywhere, and the notify step happens *after* commit with no outbox (risk R6). There's also no row-locking, so the round-robin read-modify-write can interleave (risk R5).

## Concurrency control

[`Conversation.RowVersion`](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L30-L31) maps to PostgreSQL's `xmin` system column as an optimistic concurrency token ([AppDbContext#L78-L79](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L78-L79)). EF will add `WHERE xmin = @original` to updates, so a stale write throws `DbUpdateConcurrencyException` — **but no code catches it** (risk R13), so concurrent agent edits surface as a 500 rather than a 409. The token *protects* data integrity; the *UX* of conflict is unhandled.

## The audit + soft-delete override

[`SaveChangesAsync`](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L123-L149) stamps `CreatedAtUtc`/`UpdatedAtUtc` and converts deletes of `ISoftDeletable` into modifies, paired with `HasQueryFilter(r => !r.IsDeleted)` on CannedReply. See [Pattern 8](05-pattern-catalog.md) for the failure modes (filters leak into joins, unique indexes still see soft-deleted rows).

## Safe schema change — the real constraint here

The repo uses `EnsureCreated` and ships **no migrations** ([Program.cs#L147-L165](../../../src/NetIntercom.Api/Program.cs#L147-L165)). That means:
- You **cannot** evolve a populated database. `EnsureCreated` is all-or-nothing.
- The path to production-safe schema change is: adopt EF migrations (`dotnet ef migrations add Initial`), let startup `MigrateAsync`, and from then on every schema change is a reviewed, ordered migration.

**Safe-migration principles to apply once migrations exist** (general, and tested against this model):
1. **Expand/contract, never rename in place.** To rename `LastMessagePreview`, add the new column, backfill, switch reads, drop the old — across deploys.
2. **Add nullable or with default first.** A new non-null column on a populated `Messages` table needs a default or a backfill step.
3. **Index concurrently.** Adding an index to a live `Messages`/`Conversations` table should be `CREATE INDEX CONCURRENTLY` (Npgsql migration via raw SQL) to avoid locking writes.
4. **Make the soft-delete unique index filtered.** If you ever make `CannedReply.Shortcut` unique (Pattern 8 drill), it must be a partial index `WHERE "IsDeleted" = false`, or soft-deleted duplicates block inserts.

**Drill 1:** write the EF model change + the migration intent (not the generated SQL) to add `Conversation.Priority (int, default 0)` with an index for an inbox sort. Sequence the steps so a rolling deploy never breaks.
**Drill 2:** the `Search` filter can't use an index. Propose two fixes (a `pg_trgm` GIN index for `ILIKE %term%`, or a `tsvector` full-text column) and state which you'd pick and why.
**Self-grade:** Basic = can read the relationships and indexes. Solid = explains why `SetNull` on team-delete is correct and which queries lack index support. Strong = can write an expand/contract migration plan and justify `CREATE INDEX CONCURRENTLY`.
