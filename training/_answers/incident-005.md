# Answer: INC-005 inbox list latency (collection include for an unread badge)

**Branch:** `training/incidents/005-inbox-list-latency` · **Commit:** daf7666 · BUILD: verified (0 errors). Unit tests 27/27 pass.

## 1. Row math
EF Core's single-query mode joins both collections onto the paged conversation set, which produces a **cartesian product per conversation**: rows = Σ over the 25 conversations of `max(1, timers) × max(1, messages)`. One B2B conversation with 3,000 messages and 200 timers gives **600,000 rows** on its own. Typical ones (20 messages × 2 timers = 40 rows) are noise. `rows/call ≈ 9,480` in `pg_stat_statements` is the *average*. Pages containing a big conversation return hundreds of thousands of rows, all materialized, then deduplicated by EF in memory. That's CPU on the DB, the network and the API, plus connection-hold time → pool exhaustion. The EF warning in the log (`MultipleCollectionIncludeWarning`) names exactly this.

## 2. Why "agents × messages/s"
The request rate held steady because it's **driven by events**. Every `messageCreated` and `conversationUpdated` invalidates `["conversations"]` for **every connected agent** (`frontend/inbox/src/pages/Inbox.tsx:40-48`), plus a 30 s poll (`:32`). With 25 agents and ~1.6 events/s you get about 40 list calls/s. That was already true last week, which is why the rate didn't change. What changed is the cost per call. On weekends there are 2 agents and few events, so it didn't show.

## 3. Mitigation and real fix
- **15 min:** roll back #260 (the frontend tolerates a missing `unreadCount`, which is just `undefined > 0 === false`). Or hot-patch by removing `.Include(c => c.Messages)`.
- **Real fix:** project instead of include. Build the DTO in a `Select`: `UnreadCount = c.Messages.Count(m => m.SenderType == Visitor && m.ReadByAgentAtUtc == null)` becomes a correlated `COUNT` subquery. Serve it with a **partial index** `CREATE INDEX CONCURRENTLY … ON "Messages"("ConversationId") WHERE "SenderType" = 0 AND "ReadByAgentAtUtc" IS NULL`. It stays tiny, because unread rows are few. Do the same for the timers (earliest running + any-breached as scalar subqueries, see ladder M1), which makes the list cheaper than before #260. The alternative is a denormalized `Conversation.UnreadByAgentCount`, incremented in `SendVisitorMessage` and zeroed in `MarkReadByAgent`, with the invariant owned by the aggregate.
- **Multiplier:** patch the cached list from the event payload (`setQueryData`) or debounce invalidations.

## 4. Wrong counts elsewhere
`ConversationDto.From` is also used by `GetConversationHandler`, `AssignConversationHandler`/`ChangeConversationStateHandler` (`LoadAsync` has no `Messages` include), `SendAgentMessageHandler` and `CheckSlaBreachesHandler`. With `Messages` not loaded, `c.Messages.Count(...)` counts an **empty collection → 0**, silently. Every `conversationUpdated` push and `GET /conversations/{id}` reports `unreadCount: 0`. In `SendVisitorMessage`, EF relationship fix-up makes the collection contain only the messages tracked in that context: 1 for a new conversation. It's a classic smell of computing from navigations in a DTO factory. The count is only right when the caller happened to include the collection. It belongs in the query projection, not in `From()`.

## 5. CI guard
An integration test that captures SQL for `GET /conversations` (a `DbCommandInterceptor` or `LogTo`) and asserts no `JOIN "Messages"` and ≤ N commands. Set `ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))` in Development/test so the warning fails the build of the query. A seeded "big conversation" fixture in perf smoke tests.
