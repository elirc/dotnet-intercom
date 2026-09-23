# INC-005: Inbox p95 from 180 ms to 3.7 s, DB CPU pinned during business hours

**Branch:** `training/incidents/005-inbox-list-latency` · **Severity:** SEV-2 · **Time box:** 60 min

## Alert (Mon 09:38 UTC)
```
[ALERT] netintercom-api: http_server_duration p95{route="api/v{version}/conversations"} > 1s for 10m
        current p95: 3.7 s   last week: 180 ms
[ALERT] netintercom-db: CPU > 85% for 15m   (last week peak: 35%)
[WARN]  Npgsql pool: 'The connection pool has been exhausted' ×212 in 5m
```
> **#support-leads**, from Priya: The inbox is unusable, it spins after every new message. The unread badges look nice though.

## What on-call sees
- Deploy Fri 18:10 UTC: #260 `feat(inbox): unread count badge per conversation`, #261 `chore: bump vite 5.4.0 → 5.4.8`.
- It was quiet over the weekend (2 agents, low traffic) and broke Monday 09:00 when 25 agents logged in.
- DB top query by total time (`pg_stat_statements`), new since Friday:
```
calls   mean_ms  rows/call  query
48,211  612.4    9,480      SELECT c."Id", … FROM (SELECT … FROM "Conversations" AS c WHERE c."State" = $1
                            ORDER BY c."LastMessageAtUtc" DESC LIMIT $2 OFFSET $3) AS t
                            … LEFT JOIN "SlaTimers" AS s ON t."Id" = s."ConversationId"
                            LEFT JOIN "Messages" AS m ON t."Id" = m."ConversationId" ORDER BY …
```
- API warning log, once per process start:
```
[09:01:12 WRN] Compiling a query which loads related collections for more than one collection navigation,
               either via 'Include' or through projection, but no 'QuerySplittingBehavior' has been configured…
```
- Request rate on `GET /conversations` is 41 req/s at 09:30. Last Monday it was 39 req/s, so the request rate hasn't changed.
- Data: 400k conversations, 6M messages. A handful of B2B conversations have 3,000+ messages and 200+ SLA timers each.

## Suspect PR #260 (as merged)
```diff
--- a/src/NetIntercom.Application/Conversations/ListConversations.cs
             .Include(c => c.SlaTimers)
+            .Include(c => c.Messages) // unread badge
             .AsQueryable();
--- a/src/NetIntercom.Application/Dtos.cs
     SlaTimerDto? ActiveSlaTimer,
-    bool HasBreachedSla)
+    bool HasBreachedSla,
+    int UnreadCount)
 …
-            c.SlaTimers.Any(t => t.Status == SlaTimerStatus.Breached));
+            c.SlaTimers.Any(t => t.Status == SlaTimerStatus.Breached),
+            c.Messages.Count(m => m.SenderType == SenderType.Visitor && m.ReadByAgentAtUtc == null));
--- a/frontend/inbox/src/pages/Inbox.tsx
+        {c.unreadCount > 0 && <span className="badge unread">{c.unreadCount}</span>}
```

## Your tasks
1. Compute roughly how many rows one list call returns when the page contains one of the big B2B conversations. Show the multiplication.
2. The request rate didn't change. Explain why the load still scales with "agents × messages per second". Find the client code that makes that true.
3. Mitigate within 15 minutes, then write the real fix: one that keeps the badge and makes the list query cheaper than before #260.
4. `UnreadCount` is also wrong in some places even when it's fast. Where, and why? (Hint: which other handlers build a `ConversationDto`?)
5. What guard in CI would have caught this PR?

Sealed answer: [`../_answers/incident-005.md`](../_answers/incident-005.md)
