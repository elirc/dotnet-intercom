# M4: Agents are kicked to the login page about every 15 minutes

**Rung:** mid · **Est.:** 4 h · **Skills:** token refresh concurrency, React Query, SignalR auth, frontend testing

## Context
Access tokens live 15 minutes (`src/NetIntercom.Api/appsettings.json:9`). The inbox fetch wrapper refreshes on 401 (`frontend/inbox/src/api.ts:36-51`). Refresh rotates the token: the presented one is revoked and a new pair is issued (`Application/Auth/Refresh.cs:31-56`). The inbox page runs several queries at once (conversations, messages, canned replies, teams, agents: `Inbox.tsx:26, 182, 187, 355, 356`). The SignalR connection reads the token through `accessTokenFactory` (`frontend/inbox/src/realtime.ts:10-13`).

## Ticket
> Agents get thrown to the login screen mid-conversation, and a half-typed reply is lost. It isn't every time, but it happens several times a day. Nobody changed anything.

## Constraints
- Keep refresh-token rotation on the server. Don't weaken it to "reuse allowed".
- No new dependencies in the inbox.
- Don't lose the typed composer text on a real logout.

## Definition of done
- A written explanation of the exact interleaving that causes the logout. Include the HTTP calls in order.
- Fix: all concurrent 401s share **one** in-flight refresh. Any request that 401s after the refresh finishes retries with the new token.
- A test (Vitest + mocked `fetch`) with 3 requests that 401 at once, expecting exactly one `/auth/refresh` call and all 3 retried.
- A note on what happens to the SignalR connection when its token expires. Does it matter while the socket stays open? What about on reconnect?

## Explain before touching
1. Draw the timeline: 3 parallel requests, token expired, each hits the 401 branch. Which refresh succeeds, what do the others send, and what does the server answer?
2. Line 48 calls `setTokens(null)` and does `window.location.href = "/login"`. Why is a hard redirect especially bad in this app? (Think about in-memory state and the hub.)
3. If the server *did* implement refresh-token reuse detection (revoke the whole family on reuse), what would this client bug turn into?
4. Where does a module-level `let refreshing: Promise<…> | null` live across React StrictMode double-mounts and HMR? Is that a problem?
5. What is `accessTokenFactory` called for: every hub message, or only negotiate/reconnect?

<details><summary>Hints</summary>

- ASP.NET Core validates the bearer token when the WebSocket connection is established, not per message (unless you configure `CloseOnAuthenticationExpiration`).
</details>

Sealed answer: [`../_answers/ladder-M4.md`](../_answers/ladder-M4.md)
