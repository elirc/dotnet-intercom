# Answer: M3 widget stops receiving replies after reconnect

## Two independent root causes
1. **Group membership is lost on reconnect.** SignalR groups belong to a *connection id*. `withAutomaticReconnect()` makes a **new** connection (new id) unless stateful reconnect is configured. `VisitorHub.OnConnectedAsync` re-adds the connection only to `"visitors"` (`VisitorHub.cs:31`), not to `vconv:{id}`. The widget calls `JoinConversation` only after the initial `start()` (`Widget.tsx:61-63`) and after a new conversation (`:86-89`), never in `onreconnected`. So after the first reconnect, `RealtimeNotifier.MessageCreatedAsync` sends to a group the visitor isn't in: **every live reply is lost until a reload.** The inbox has the same bug for `conv:{id}` (typing and collision viewers, `Inbox.tsx:193-203`). Inbox *messages* still arrive there because they go to `"agents"`, which `AgentHub.OnConnectedAsync` re-adds.
2. **Stale closure in catch-up.** `onreconnected` captures `session.conversation` from bootstrap (`:52-58`). For a first-time visitor it's `null`, so there's no catch-up at all. For returning visitors who start a new conversation, it points at the *old* one.

`docs/architecture-review.md` §2.4 describes cause 2 only. Cause 1 is the bigger one: it breaks *every* visitor after *any* blip, not just new ones.

## Secondary issues
- `setMessages(latest)` replaces state. A message pushed during the fetch is lost if the fetch response was produced before it was committed. Merge by id instead.
- It refetches the full transcript instead of using `?after=` (the README claims `after` is used: `README.md:79`, a docs-vs-code mismatch).
- `after` should be the `createdAtUtc` of the newest message the client has. Same-timestamp ties (`WidgetQueries.cs:36` uses `>`) can still drop a message. Mitigate with `>=` plus dedupe by id, or with a sequence number.

## Reference fix (client)
```ts
const convIdRef = useRef<string | null>(null); // set on bootstrap and in send()
conn.onreconnected(async () => {
  const id = convIdRef.current;
  if (!id) return;
  await conn.invoke("JoinConversation", id);          // re-subscribe FIRST
  const last = lastCreatedAtRef.current;              // newest message we have
  const missed = await api.getMessages(id, last ?? undefined);
  setMessages(old => mergeById(old, missed));
});
```
Order matters: join first, then fetch. If you fetch first, a message committed between the fetch and the join is lost.

## Server alternative
In `VisitorHub.OnConnectedAsync`, look up the visitor's open conversations and join them automatically. It's robust to buggy clients and costs one query per connect. Clients still need catch-up. Either is fine. The strongest answer does server auto-join **and** client catch-up.

## Test
The SignalR .NET client (`Microsoft.AspNetCore.SignalR.Client`) against `WebApplicationFactory` (use `Server.CreateHandler()` and `HttpMessageHandlerFactory`, LongPolling transport): connect, join, `StopAsync` then `StartAsync` (a new connection id), post an agent reply, and assert it's received. That fails before a server-side fix. A client-side fix is better tested with Vitest and a fake `HubConnection` that records `invoke` calls on `onreconnected`.

Rubric: Basic = fixes the stale closure. Solid = + finds lost group membership. Strong = + join-then-fetch ordering, merge instead of replace, a test at the right layer.
