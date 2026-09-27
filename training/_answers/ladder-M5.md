# Answer: M5 "cache the routing rules"

*(Added in B08. It's reasoned from the code on `main`, and no benchmark was run. Treat the latency figures as orders of magnitude and measure them yourself.)*

## The decision a strong answer reaches
**Probably don't cache it, or cache a tiny snapshot with a DB-checked version.** Measure first.
- The rules table holds tens of rows, and the query is a filtered scan returning a handful of rows with a join to `Teams`. On a warm Postgres that's typically well under 1 ms of DB time plus one round-trip.
- The same request also looks up the visitor, the fallback team and the team members, inserts 2–3 rows, runs `SaveChangesAsync` and sends two SignalR messages.
- 40 landing pages raise the number of *new conversations*, not the cost of each one. At, say, 10 new conversations per second, the rules query is still noise next to the insert.

If the teammate can't show a trace where routing dominates the request, the PR is a consistency risk bought for no measurable gain. "Caching is a consistency decision, not a performance tweak" ([06](../../../opusorganize/apprenticeship/curriculum/06-caching-correctness.md)).

## 1. Failure modes the 10-minute `IMemoryCache` PR risks
- **Stale forever (bounded by the TTL here).** On 3 replicas, an admin disables a rule, and the other replicas route by the old rule for up to 10 minutes. If `SaveRoutingRuleHandler` invalidates, it invalidates only the replica that served the save. The other two remain stale. Deleting a team a cached rule points at leads to a FK violation or a routing error until expiry.
- **Silent degradation.** If the cache stores tracked entities from a disposed `DbContext`, lazy-loading or attaching them later throws, or worse, `conversation.TeamId = team.Id` works but `conversation.Team = cachedTeam` makes EF try to insert or attach a second `Team` instance, raising a duplicate-key error. Nothing in the PR tests that.
- **Wrong audience:** not today, since there's a single tenant. After S2 (workspaces), a key without `WorkspaceId` would route company A's chats with company B's rules. Put the tenant in the key from day one.
- **Stampede:** negligible. The recompute is cheap.

## 2. What else gets cached by accident
`.Include(r => r.Team)` brings in `Team.OfficeHoursJson`, `IsFallback` and `DefaultSlaPolicyId`. So caching rules also caches **office hours and the default SLA policy** for 10 minutes:
- An admin fixes the night-shift hours (see the on-ramp: overnight windows never match, `OfficeHours.cs:33`), and routing ignores the fix.
- New conversations get the *old* SLA policy.

J3's office-hours endpoint would now need to invalidate the rule cache too, which is an invalidation owner nobody wrote down.

## 3. Per-process cache across 3 replicas
Only the replica that handled `SaveRoutingRuleHandler` sees the change immediately, if the handler evicts. The others see it at TTL expiry. With round-robin load balancing, 2/3 of new conversations use the old rules for up to the full TTL.

## 4. Cross-replica invalidation options
1. **A version stamp in the DB.** Add a `RoutingConfigVersion` row, or `max(UpdatedAtUtc)` over rules and teams, incremented in the same transaction as any rule or team save. Each route runs `SELECT version` (one tiny indexed read) and reloads the snapshot only when it changes. The staleness bound is 0, and the cost is one round-trip. That's already most of what you were trying to save, which is the point: it shows caching buys little here.
2. **Postgres `LISTEN/NOTIFY`,** or the SignalR backplane planned in S1, used as an invalidation bus. Near-instant, but a missed notification (connection drop) means stale-until-TTL. You still need a TTL as the backstop, and you've added a long-lived listener per replica to operate and monitor.
3. **A short TTL (5–15 s), no invalidation.** Simple and honest if the business accepts "rule changes apply within 15 s". Write that sentence in the admin UI.
4. Redis: new infrastructure for a 50-row table. Reject it unless S1 already brings Redis in for the backplane.

## Never cache: presence and round-robin
`PickRoundRobinAsync` needs the **current** online set (`ConversationRouter.cs:81`) and the current `LastAssignedAtUtc`, which the router itself writes (`:66`). A cached member list would assign every new chat to the same "least recently assigned" agent until expiry, because the cache never sees the write. That's the wrong-audience or stale-forever failure turned into a fairness bug that agents *will* notice.

## 5. The inbox React Query cache
It doesn't lie. It over-invalidates. Every `messageCreated` and `conversationUpdated` invalidates `["conversations"]` for every agent (`Inbox.tsx:40-48`), so the cache has almost no hit rate during business hours and multiplies DB load (see INC-005 task 2 and ladder M1). It's a cost problem, not a correctness one. Fix it by patching cached rows from the event payload (`setQueryData`) or by debouncing invalidation.

## If you do implement it
- Cache an immutable record `RoutingSnapshot(long Version, IReadOnlyList<RuleSnapshot> Rules, TeamSnapshot? Fallback)`, built with `AsNoTracking()`. Assign only IDs onto the conversation.
- Key it by workspace, and after S2 by `(workspaceId)`.
- Invalidate by version (option 1), or use a TTL ≤ 15 s with a documented bound.
- Tests:
  - "save rule → next route uses it" on one host.
  - A two-`WebApplicationFactory`-hosts test against the same Testcontainers DB: save on host A, route on host B, and assert the new rule within the bound.
  - A test proving round-robin still rotates across three consecutive new conversations.
- A metric for the cache hit ratio, and a log line when a reload happens.

## Rubric
- **Basic:** implements `IMemoryCache` with invalidation in the save handler.
- **Solid:** + names the stale-replica problem and the accidental office-hours/SLA caching, and refuses to cache presence or round-robin.
- **Strong:** + measures, or credibly estimates, the query cost and concludes that caching is unnecessary *or* ships a version-checked snapshot with a written staleness bound, plus the cross-host test.
