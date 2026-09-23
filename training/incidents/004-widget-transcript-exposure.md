# INC-004: Pen-test finding. A visitor can read support replies from another customer's chat

**Branch:** `training/incidents/004-widget-transcript-exposure` · **Severity:** SEV-1 (security) · **Time box:** 45 min

## Report (Fri 16:20 UTC, external pen-test firm, draft finding NI-07)
> Using two throwaway browsers (visitor A and visitor B) on the demo site, we took B's conversation id from B's own network traffic and requested it with **A's** visitor token:
> ```
> GET /api/v1/widget/conversations/0d6c3e1a-…/messages
> Authorization: Bearer <visitor A token>
> → 200 OK
> [{"senderType":"Agent","senderName":"Sara Support","body":"Hi Morgan, I've refunded order #88412 to the card ending 0417…", …}]
> ```
> The response contained the agent's replies to B but **not B's own messages**. `POST …/{id}/read` with A's token correctly returns 403. The hub's `JoinConversation` with A's token correctly throws. We rate this High: authorization bypass exposing PII and payment details.

## What on-call sees
- The endpoint returned 403 for foreign conversations in last month's pen test (finding NI-02 was closed as "not reproducible").
- Changes to the widget API since then: #251 `perf(widget): single query for reconnect catch-up` (merged 9 days ago, reviewed by 2 people, 27 unit tests + integration tests green).
- API metrics: `/widget/conversations/{id}/messages` p50 dropped from 11 ms to 7 ms after #251. 403 responses on that route dropped from ~40/day to 0.

## Suspect PR #251 (as merged)
```diff
--- a/src/NetIntercom.Application/Widget/WidgetQueries.cs
     public async Task<IReadOnlyList<MessageDto>> Handle(GetVisitorMessagesQuery request, CancellationToken ct)
     {
-        var owns = await _db.Conversations
-            .AnyAsync(c => c.Id == request.ConversationId && c.VisitorId == request.VisitorId, ct);
-        if (!owns)
-        {
-            throw new ForbiddenException();
-        }
-
+        // Reconnect catch-up is the hottest widget endpoint (every SignalR reconnect of
+        // every open widget). Scope by visitor inside the message query itself instead of
+        // a separate ownership round-trip: visitor messages must be this visitor's, and
+        // agent replies are only ever written into a conversation, so they're safe to include.
         return await _db.Messages
             .Include(m => m.AgentUser)
             .Where(m => m.ConversationId == request.ConversationId
                         && !m.IsInternalNote
+                        && (m.VisitorId == request.VisitorId || m.SenderType == SenderType.Agent)
                         && (request.AfterUtc == null || m.CreatedAtUtc > request.AfterUtc))
```

## Your tasks
1. Explain precisely why the new predicate leaks, and why the code comment sounded convincing to two reviewers.
2. "Conversation ids are random GUIDs, so this is low severity." Argue against that with at least three ways ids leak in *this* system.
3. The "403s dropped to 0" metric was visible for 9 days. What alert would have caught it, and what's wrong with alerting on it naively?
4. Fix it, and add the negative tests the suite is missing: every widget endpoint and hub method with a foreign conversation id.
5. Blast radius: can you determine from existing logs and data whether anyone *other than* the pen-testers used this? What's missing from the logs?

Sealed answer: [`../_answers/incident-004.md`](../_answers/incident-004.md)
