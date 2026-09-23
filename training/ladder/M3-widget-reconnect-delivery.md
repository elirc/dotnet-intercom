# M3: After a Wi-Fi blip, visitors stop receiving live replies

**Rung:** mid · **Est.:** 4–6 h · **Skills:** SignalR connection lifecycle, groups, catch-up cursors, React effects

## Context
The widget connects to `/hubs/visitor` with `withAutomaticReconnect()` (`frontend/widget/src/Widget.tsx:34-37`). It joins its conversation group once, right after `start()` (`:61-63`), and again only when `send()` creates a new conversation (`:86-89`). On reconnect it refetches the transcript for `session.conversation` (`:52-58`). Server side, `VisitorHub.JoinConversation` adds the connection to `vconv:{id}` after an ownership check (`Api/Realtime/VisitorHub.cs:37-46`), and `RealtimeNotifier` sends replies only to that group (`Api/Realtime/RealtimeNotifier.cs:23-26`). The README says both clients catch up with `GET …/messages?after=…` (`README.md:79`).

## Ticket
> Customers say: "I left the tab open, the agent says they replied, but I only see it when I refresh." It happens most on laptops that sleep and on train Wi-Fi.

## Constraints
- No server-side session state. The fix must still work if the reconnect lands on a *different* API instance later (see S1).
- Catch-up must not refetch the whole transcript on every reconnect. Use the `after` cursor the API already supports.
- Don't double-render messages that arrive via both catch-up and push.

## Definition of done
- Reproduce first: a written repro (DevTools offline toggle, or stop the API for 5 s) showing no live messages after reconnect.
- Fix in the widget (and the server, if you decide it's needed). The inbox equivalent (`conv:{id}` group used for typing and collision) is fixed or ticketed.
- A test that fails before the fix. A Vitest + mocked `HubConnection` test is acceptable, and so is a SignalR .NET client integration test against `WebApplicationFactory`. Justify which.

## Explain before touching
1. What is a SignalR *group*, and what happens to a connection's group memberships when the client reconnects with a new connection id?
2. Which closure does `onreconnected` capture? What is `session.conversation` for a visitor whose first message was sent *after* page load?
3. The code does `setMessages(latest)` after a full refetch. What does that do to a message that arrived via push during the fetch?
4. `after` compares `CreatedAtUtc > after` (`Application/Widget/WidgetQueries.cs:34-36`). Which message timestamp should the client send, and what edge case remains?
5. Is this better fixed on the server (auto-join the visitor's open conversations in `OnConnectedAsync`) or on the client? Give the trade-off.

<details><summary>Hints</summary>

- `useRef` for "current conversation id" avoids stale closures in long-lived handlers.
- Compare with `docs/architecture-review.md` §2.4 **after** you've written your analysis. It misses one of the two root causes.
</details>

Sealed answer: [`../_answers/ladder-M3.md`](../_answers/ladder-M3.md)
