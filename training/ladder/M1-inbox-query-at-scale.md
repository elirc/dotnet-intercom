# M1: The inbox list takes 4 s at 400k conversations

**Rung:** mid · **Est.:** 1 day · **Skills:** query plans, indexes, EF Core includes, pagination, measurement

## Context
Production data after 18 months: 400k conversations, 6M messages, 1.1M SLA timers, 25 agents. `GET /api/v1/conversations?state=Open&sort=slaBreach` p95 is 4.1 s. `sort=lastMessage` is 900 ms. `search=acme` is 6 s. The handler is `Application/Conversations/ListConversations.cs:26-77`. Every agent refetches this list **on every `messageCreated` and `conversationUpdated` event** (`frontend/inbox/src/pages/Inbox.tsx:40-48`), plus every 30 s (`:32`).

## Ticket
> Agents say the inbox "freezes" at peak times. Get the default view (`Open`, sort by `lastMessage`) under 150 ms p95 and `slaBreach` under 300 ms. Search can be slower, but not 6 s.

## Constraints
- PostgreSQL 16, EF Core 8. No new infrastructure (no Elasticsearch).
- Don't change the JSON response shape. The inbox and any scripts rely on `PaginatedList` (`Application/Common/PaginatedList.cs`).
- Index changes must be deployable without locking `Conversations` for minutes. Remember this repo has **no migrations** yet (`Api/Program.cs:150-157`).

## Definition of done
- `EXPLAIN (ANALYZE, BUFFERS)` before/after for the three query shapes, in the PR description. Seed the data with a script you write.
- The fix includes, at least: how `SlaTimers` are loaded, the `slaBreach` ordering expression, the `COUNT(*)` from `PaginatedList.CreateAsync`, and the search predicate.
- A regression guard: a test or a logged query-count check that fails if someone adds another collection `Include` to this list.

## Explain before touching
1. Write down the SQL you expect EF to generate for the default call, with its four `Include`s (`ListConversations.cs:28-33`). How many rows come back per conversation?
2. `sort=slaBreach` orders by a correlated `MIN()` over `SlaTimers` (`:67-70`). Why can't the `(Status, BreachAtUtc)` index (`AppDbContext.cs:97`) serve it?
3. The DTO needs only the earliest *running* timer and "any breached?" (`Application/Dtos.cs:72-76`). What two scalar values could the query project instead of the whole timer history?
4. `CountAsync` runs on every page load. What is the real cost at 400k rows, and what could the UI show instead of an exact total?
5. `ToLower().Contains()` over three columns (`:55-62`). What index type could serve it, and what does that cost on writes?
6. How do you ship `CREATE INDEX` with no migrations framework and no downtime?

<details><summary>Hints</summary>

- Filtered includes (`Include(c => c.SlaTimers.Where(...))`) and `AsSplitQuery()` are both available in EF Core 8. Know the trade-off.
- A denormalized `NextBreachAtUtc` column on `Conversation` is a legitimate answer, but only if you name the invariant that keeps it right.
- `CREATE INDEX CONCURRENTLY` cannot run inside a transaction.
</details>

Sealed answer: [`../_answers/ladder-M1.md`](../_answers/ladder-M1.md)
