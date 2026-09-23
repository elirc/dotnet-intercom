# INC-002: New conversations pile up unassigned while agents are online

**Branch:** `training/incidents/002-unassigned-queue-spike` · **Severity:** SEV-2 · **Time box:** 60 min

## Alert (Wed 14:10 UTC)
```
[ALERT] netintercom: unassigned_new_conversations_ratio > 5% for 30m
        current: 11.4%   7-day baseline: 1.8%
```
> **#support-leads**, from Priya: The queue has 23 unassigned chats and my whole team is online, the green dots are right there. First-response SLA breaches are up 3× today. Did routing change?

## What on-call sees
- Deploy Tue 17:30 UTC: #231 `fix(presence): stop PresenceTracker growing without bound`, #232 `feat(reports): CSV export (FE only)`.
- No routing rules or teams changed (the `RoutingRules` / `Teams` tables have no `UpdatedAtUtc` values after last week).
- Unassigned conversations come in **bursts**, mostly between 09:00 and 10:00 and after lunch. Every one of them is logged:
```
[14:02:17 INF] No online agents in team Support; conversation 3f0c… queued
[14:02:19 INF] Handled SendVisitorMessageCommand
[14:02:31 INF] No online agents in team Support; conversation 81a2… queued
```
- Grafana, hub connections (unchanged since before the deploy):
```
signalr_agent_hub_connects_per_min      p50 38   (last week p50 37)
signalr_agent_hub_disconnects_per_min   p50 37   (last week p50 36)
```
- The corporate network team says their proxy cuts every WebSocket after ~100 s, whether it's busy or idle. "It's always done that." Most of the Support team logs in within the same few minutes at 09:00 and 13:00.
- `PresenceSnapshots` rows for several agents have not changed `ChangedAtUtc` since Tuesday evening, although those agents have been working all day.
- In the inbox, Priya's team shows green dots for everyone. The widget header on the website flickers between "We're online" and "We'll reply by email/asap" a few times an hour.

## Suspect PR #231 (as merged)
```diff
--- a/src/NetIntercom.Api/Realtime/PresenceTracker.cs
     public void Disconnected(Guid agentId, string connectionId)
     {
         if (_agents.TryGetValue(agentId, out var state))
         {
             state.Connections.TryRemove(connectionId, out _);
             state.LastDisconnectUtc = DateTime.UtcNow;
+
+            // Fix unbounded growth: agents who log off were kept in _agents forever
+            // (and re-broadcast by the sweeper every restart). Drop the entry once the
+            // last connection is gone; Connected() recreates it on the next login.
+            if (state.Connections.IsEmpty)
+            {
+                _agents.TryRemove(agentId, out _);
+            }
         }
 
-        foreach (var viewers in _viewers.Values)
+        foreach (var (conversationId, viewers) in _viewers)
         {
             viewers.TryRemove(connectionId, out _);
+            if (viewers.IsEmpty)
+            {
+                _viewers.TryRemove(conversationId, out _);
+            }
         }
     }
```

## Your tasks
1. Explain why agents who are "online" in the inbox are not candidates for assignment. Give the exact sequence of hub events and tracker states.
2. Explain **three** symptoms from the same root cause: the dots, the frozen `PresenceSnapshots`, and the widget flicker.
3. Fix it without reintroducing the unbounded growth the PR was trying to fix.
4. Conversations queued during a blip stay unassigned forever. Is that a second bug? Propose the follow-up.
5. What test would have caught this? `PresenceTracker` uses `DateTime.UtcNow` directly. What does that force on your test?

Sealed answer: [`../_answers/incident-002.md`](../_answers/incident-002.md)
