# Systematic Debugging

The loop, always: **reproduce → narrow (binary-split the system) → hypothesize → test the hypothesis cheaply → fix the root cause, not the symptom → add regression coverage.** The scenarios below are repo-specific; each one trains a different narrowing instinct.

Tools available here: VS/Rider debugger on the API; Serilog console output with correlation ids ([CorrelationIdMiddleware](../../../src/NetIntercom.Api/Infrastructure/CorrelationIdMiddleware.cs#L13-L26) — grab `X-Correlation-Id` from a failing response and grep the log); EF query logging (set `Microsoft.EntityFrameworkCore` to `Information` in [appsettings.json](../../../src/NetIntercom.Api/appsettings.json#L22-L31)); browser network panel (filter `/api` and the `/hubs` WebSocket frames); React Query devtools if you add them.

---

## Scenario 1: "Agents get logged out at random"

**Reproduction:** log in, let the access token expire (15 min, or temporarily set `Jwt:AccessTokenMinutes` to 1), then click anything that fires several queries at once (open the inbox: conversations + canned-replies + teams + agents fire together).
**First question:** client or server? (Does `/auth/refresh` return 401 in the network panel? Then the server rejected a token — why?)
**Narrowing path:**
1. Network panel: several requests 401 simultaneously → several `/auth/refresh` calls fire.
2. First refresh 200s; the rest 401 ("Refresh token is invalid or expired").
3. Read [`Refresh.cs#L43`](../../../src/NetIntercom.Application/Auth/Refresh.cs#L43): rotation revokes the presented token on first use.
4. Read [`api.ts#L38-L51`](../../../frontend/inbox/src/api.ts#L38-L51): no shared in-flight refresh — each 401 races.
**Useful probes:** breakpoint on `RefreshTokenCommandHandler.Handle`; count hits per user action.
**Likely root cause:** unsynchronized concurrent refresh + strict rotation.
**Regression test to add:** integration test that calls `/auth/refresh` twice with the same token and asserts the second fails (locks in server behavior), plus a client test that two concurrent 401s produce exactly one refresh call.
**Senior lesson:** the bug lives in the *protocol between* client and server, not in either one. Fixing only the client leaves every future client to rediscover it.

## Scenario 2: "Billing chats land in Support after 6pm — but Billing says they're open until 8"

**Reproduction:** seed data gives Billing 08–18 UTC windows ([DbSeeder#L26-L36](../../../src/NetIntercom.Infrastructure/Persistence/DbSeeder.cs#L26-L36)). Send "invoice question" at 18:30 *local* somewhere west of UTC.
**First question:** is this a routing bug or a timezone definition mismatch?
**Narrowing path:**
1. Log line "Team Billing is outside office hours; falling back" ([ConversationRouter#L45](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L45)) confirms the router *chose* this.
2. Read [`OfficeHours.IsOpenAt`](../../../src/NetIntercom.Domain/Entities/OfficeHours.cs#L14-L34): converts UTC → `TimeZoneId`. Seed says `"UTC"`; Billing's humans think in local time.
3. Also check the silent fallback: unknown `TimeZoneId` → quietly treated as UTC (L26-29) — a typo'd zone id produces this exact symptom *with no log line*.
**Useful probes:** unit test calling `IsOpenAt` with the team's real JSON; no server needed.
**Likely root causes:** (a) office-hours configured in the wrong zone; (b) typo'd zone id silently swallowed.
**Regression test to add:** `IsOpenAt` test for a non-UTC zone across a DST boundary; and decide — should an unknown zone *throw* at save time instead of defaulting at read time? (That's the root fix: validate at write, not read.)
**Senior lesson:** silent fallbacks convert configuration errors into behavior mysteries weeks later. Fail at the moment the bad data enters.

## Scenario 3: "The same agent gets every new conversation"

**Reproduction:** two agents online in Support; fire 5 widget messages quickly (or with `Promise.all`).
**First question:** is round-robin broken, or is its *input* (presence) broken?
**Narrowing path:**
1. Check presence first: `GET /api/v1/agents` shows each agent's status — if only one is Online, round-robin is *working*. (Is the other agent Away from 5 min idle? [Compute](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L76-L90).)
2. If both Online: read [`PickRoundRobinAsync`](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L75-L86) — order by `LastAssignedAtUtc`. Query the table: did the stamp actually advance? It's written at [RouteAsync L66](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L66) but only *saved* when the handler's SaveChanges runs.
3. Concurrent requests: both read the same `LastAssignedAtUtc` snapshot before either saves → same "least recent" winner. No lock, no retry-on-conflict.
**Useful probes:** EF query logging shows two identical SELECTs before any UPDATE.
**Likely root cause:** read-modify-write race on `LastAssignedAtUtc` under concurrency (plus the Away surprise as a frequent false alarm).
**Regression test to add:** integration test firing N concurrent first-messages, asserting assignment spread (will be flaky-red until fixed — that's the point; mark it as the fix's acceptance test).
**Senior lesson:** before debugging an algorithm, verify its inputs. Half of "routing is broken" reports are presence policy working as designed but defined differently in the reporter's head.

## Scenario 4: "Visitor stops receiving replies after their first-ever message (sometimes)"

**Reproduction:** intermittent — cold load the widget on a *fresh* identity (no conversation yet), type and send within ~1s of page load, slow network helps (throttle in devtools).
**First question:** are messages missing from the *DB* or from the *delivery*? (Reload the widget: history shows the agent replies → delivery problem.)
**Narrowing path:**
1. The widget joins its conversation group in two places: bootstrap ([Widget.tsx#L61-L63](../../../frontend/widget/src/Widget.tsx#L61-L63), only if a conversation already exists) and post-send ([Widget.tsx#L86-L89](../../../frontend/widget/src/Widget.tsx#L86-L89)).
2. Post-send join runs `connRef.current?.invoke(...)` — if the hub connection hasn't finished `start()` yet, `connRef.current` is null or the invoke rejects, and `.catch(() => {})` swallows it.
3. Server side: group membership is the only routing for `vconv:{id}` events ([RealtimeNotifier#L25](../../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs#L25)) — never joined = never delivered, forever.
**Useful probes:** WebSocket frames in devtools (no `messageCreated` arriving); add a temporary log in `VisitorHub.JoinConversation`.
**Likely root cause:** unawaited/raced group join with swallowed failure; no re-join on the `onreconnected` path for conversations created post-connect (re-join there uses `session.conversation` from the *closure*, which is null for fresh visitors — [Widget.tsx#L52-L58](../../../frontend/widget/src/Widget.tsx#L52-L58)).
**Regression test to add:** hard without browser automation; minimum viable = unit-test the join logic extracted from the effect, plus a manual test script in the PR.
**Senior lesson:** `.catch(() => {})` is where reliability goes to die. Swallow only what you can prove is safe to swallow, and log even then.

## Scenario 5: "Reports page numbers don't match the inbox"

**Reproduction:** create conversations, close some, compare `/reports/overview` counts with filtered inbox counts.
**First question:** different data, or different *definitions*?
**Narrowing path:**
1. Read the report query: window is `CreatedAtUtc >= from && < to` ([Reports.cs#L30-L33](../../../src/NetIntercom.Application/Reports/Reports.cs#L30-L33)) — conversations *created* in the period.
2. `ClosedConversations` counts conversations created-in-window that are closed *now* — not "closed during the window." A conversation created May 1, closed today, counts in neither this month's closed nor May's (if you query May, it counts — closed *now* is true). Write the truth table.
3. The inbox counts by current state with no time window at all ([ListConversationsQuery](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L10-L18)).
4. Also: default ranges use `DateTime.UtcNow` at the controller ([ReportsController#L20-L28](../../../src/NetIntercom.Api/Controllers/ReportsController.cs#L20-L28)), bypassing `IClock` — tests can't pin it.
**Likely root cause:** no bug in the mechanism — undocumented metric definitions (cohort-by-creation vs event-in-period).
**Regression test to add:** none until definitions exist; the deliverable is a written definition per metric, then tests asserting it.
**Senior lesson:** "the numbers are wrong" is usually "the numbers are undefined." Get the definition in writing before touching the query — this is the difference between a query fix and a credibility fix.
