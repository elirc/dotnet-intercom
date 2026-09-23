# INC-001: Customer saw agent-only text in the chat widget

**Branch:** `training/incidents/001-widget-note-leak` · **Severity:** SEV-1 (data exposure) · **Time box:** 60 min

## Page (Tue 10:42 UTC)
> **#support-escalations**, from Priya (Support lead): Customer "Pat D." sent us a screenshot. The little preview bubble next to the chat button on our pricing page says *"VIP but flagged for refund abuse, don't approve >$50 without Alex"*. That's an internal note Sara wrote 20 min ago. Pat is furious. Is the widget showing our notes??

## What on-call sees
- The widget transcript (opened) for Pat's conversation does **not** show the note. Only the bubble next to the launcher does.
- Deploy at Mon 16:05 UTC included:
  - #212 `feat(widget): unread teaser on the collapsed launcher`
  - #213 `chore(deps): bump Serilog.AspNetCore 8.0.1 → 8.0.2`
  - #214 `docs: README realtime section`
- `/api/v1/widget/conversations/{id}/messages` responses for Pat's conversation (from the API log, 10:31–10:44): 3 calls, 200, none include `isInternalNote: true` rows.
- Log excerpt:
```
[10:22:04 INF] Handled SendAgentMessageCommand {"CorrelationId":"c1f0…","RequestPath":"/api/v1/conversations/7b1e…/messages"}
[10:22:04 INF] HTTP POST /api/v1/conversations/7b1e…/messages responded 200 in 38.1 ms
[10:31:40 INF] Handled StartWidgetSessionCommand {"CorrelationId":"9aa2…","RequestPath":"/api/v1/widget/session"}
```

## Suspect PR #212 (as merged)
```diff
--- a/src/NetIntercom.Application/Conversations/SendAgentMessage.cs
         if (!request.IsInternalNote)
         {
             conversation.RecordAgentReply(now);
-            conversation.LastMessageAtUtc = now;
-            conversation.LastMessagePreview = request.Body.Length <= 120 ? request.Body : request.Body[..120] + "…";
         }
+
+        // Notes count as activity too: busy conversations were sinking in the inbox
+        // because only public replies bumped LastMessageAtUtc.
+        conversation.LastMessageAtUtc = now;
+        conversation.LastMessagePreview = request.Body.Length <= 120 ? request.Body : request.Body[..120] + "…";
--- a/src/NetIntercom.Api/Realtime/RealtimeNotifier.cs
             conversation.State,
-            assigneeName = conversation.AssigneeName
+            assigneeName = conversation.AssigneeName,
+            // Powers the unread teaser on the collapsed widget launcher.
+            lastMessagePreview = conversation.LastMessagePreview,
+            conversation.LastMessageAtUtc
         }, ct);
--- a/frontend/widget/src/Widget.tsx
+      {!open && conversation?.lastMessagePreview && (
+        <div className="ni-teaser" onClick={() => setOpen(true)}>
+          <strong>{conversation.assigneeName ?? "Support"}</strong>
+          <div>{conversation.lastMessagePreview}</div>
+        </div>
+      )}
```
The PR had two approvals. CI was green: 27 unit tests plus the happy-path integration test.

## Your tasks
1. Explain the exact data path from Sara's note to Pat's screen. List **every** path (push *and* pull), not just the first one you find.
2. Decide on containment for the next 15 minutes (rollback? config? hotfix?) and justify it.
3. Write the fix and a regression test that would have failed on this branch.
4. Scope the exposure: which conversations and which visitors, since when? What query answers that?
5. Draft the postmortem (use the template in `_answers/incident-001.md` only *after* you've written yours).

Sealed answer: [`../_answers/incident-001.md`](../_answers/incident-001.md)
