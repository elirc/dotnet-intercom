# Performance Thinking

## Rule zero: measure before you touch anything

Nothing in this repo has a demonstrated performance problem — it runs at seed scale. So every item below is a *latent* hotspot: where it would hurt *first* under load, identified by reading, to be confirmed by measuring. Do not "optimize" any of these without a benchmark or a query plan. The fastest way to look junior is to cache something that was never slow (review kata 4).

How to measure here: EF query logging (`Microsoft.EntityFrameworkCore: Information` in [appsettings.json](../../../src/NetIntercom.Api/appsettings.json#L22-L31)) to see the SQL; `EXPLAIN ANALYZE` in psql for plans; the OpenTelemetry ASP.NET instrumentation ([Program.cs#L128-L130](../../../src/NetIntercom.Api/Program.cs#L128-L130)) for request spans; browser devtools for render/network.

## Likely hotspots, ordered by how soon they bite

### 1. The inbox list query (read path, highest traffic)
[`ListConversationsHandler`](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L28-L73) loads 4 navigations including the `SlaTimers` *collection*, paginated.
- **Row multiplication:** a collection Include with `Skip/Take` makes EF either fan out rows or split queries; check the actual plan. Consider `AsSplitQuery()` or projecting straight to `ConversationDto` in the query (the repo maps *after* materializing — [#L74-76](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L74-L76)).
- **`Search` is unindexable:** `ToLower().Contains(term)` → `LIKE '%term%'`, a full scan ([#L57-61](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L57-L61)). First thing to hurt with data volume. Fix: `pg_trgm` GIN index or a `tsvector` column.
- **`slaBreach` sort:** correlated `Min` subquery per row, no index ([#L67-70](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L67-L70)).
- **Polling amplifies it:** the inbox refetches every 30s ([Inbox.tsx#L32](../../../frontend/inbox/src/pages/Inbox.tsx#L32)) *and* on every hub event — this query runs a lot.

### 2. The breach job's Include graph (background, scales with timer volume)
[`CheckSlaBreaches`](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L34-L40) loads `Conversation` + Visitor + Team + Assignee + the conversation's *whole* SlaTimers collection, per overdue timer, every minute — just to build a DTO for notification. The `(Status, BreachAtUtc)` index makes the *filter* fast; the Includes are the cost. Fix when it bites: project only the fields the `slaBreached` payload needs.

### 3. Serial awaits in the hot write path
[`SendVisitorMessage`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L49-L112) does: load visitor → (maybe) load conversation → route (which itself loads rules + members) → re-fetch policy → save → 2 notifies, all sequential. Most are dependent (can't parallelize), but the router loads **all** enabled rules and **all** team members on every new conversation ([ConversationRouter#L31-L35,L77-79](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L31-L35)) with no caching. Small tables today; a cache (with invalidation!) is the eventual fix — *not* the static-field bug from kata 4.

### 4. Unbounded-ish queries
Pagination is clamped to ≤100 ([PaginatedList#L24](../../../src/NetIntercom.Application/Common/PaginatedList.cs#L24)) — good. But the read-receipt handlers load *all* unread messages into memory to stamp them ([MarkReadByAgent](../../../src/NetIntercom.Application/Conversations/ManageConversation.cs#L141-L156)) — a long conversation with thousands of unread visitor messages materializes them all. A set-based `ExecuteUpdateAsync` (EF7+) would update in one statement without loading. Latent.

### 5. Frontend render cost
- [`SlaCountdown`](../../../frontend/inbox/src/pages/Inbox.tsx#L148-L165) re-renders **every second** via a forced state bump, one per visible conversation with a timer. At a few dozen rows it's fine; at hundreds it's wasteful re-renders. Memoization or a single shared ticker is the fix.
- The inbox's unfiltered `invalidateQueries()` on reconnect ([#L73-76](../../../frontend/inbox/src/pages/Inbox.tsx#L73-L76)) refetches *every* query at once — a thundering-herd of requests on every reconnect.

## The discipline, as a checklist

1. **Reproduce with realistic data** — seed thousands of conversations before judging the list query.
2. **Look at the SQL** before guessing — turn on EF logging; count statements; read the plan.
3. **Fix the algorithm before the cache** — an index or projection beats a cache with an invalidation bug.
4. **One change, re-measure** — don't bundle optimizations; you won't know which helped.
5. **Mind the multiplier** — a 50ms query that runs on every 30s poll across N agents is the real cost, not the 50ms.

**Drill:** pick hotspot #1. Turn on EF query logging, hit `/api/v1/conversations?search=foo&sort=slaBreach`, and read the emitted SQL. Identify the scan and the subquery. Write the index (or projection) you'd add, then explain why you'd still benchmark before merging it.
**Self-grade:** Basic = can name two hotspots with anchors. Solid = explains why the `Search` filter can't use an index and what to do. Strong = can order all five by "bites first," and correctly refuses to optimize #3 with a static cache.
