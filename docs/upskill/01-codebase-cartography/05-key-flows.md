# Key Flows

Five end-to-end traces. Do them in order; each assumes vocabulary from the previous one. The trace tables tell you exactly what to open — the value comes from *opening it* and confirming each row.

All paths relative to repo root; anchors are symbol + line range as of the snapshot in the [verification log](../08-reference/verification-log.md).

---

## Flow 1: Visitor's first message → routed conversation with SLA timer

**Why it matters:** this is the product. It crosses every layer (widget → controller → MediatR pipeline → domain → EF → SignalR) and contains the three pieces of real business logic: routing, assignment, SLA start.

**Open first:** [`SendVisitorMessageHandler.Handle`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L47-L115) — everything else hangs off it.

**Trace:**

| Step | Owner | File | What happens | Data shape | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | Widget (browser) | [frontend/widget/src/Widget.tsx#L80-L90](../../../frontend/widget/src/Widget.tsx#L80-L90) | `send()` posts body + current conversation id (or null) | `{conversationId?, body, sourceUrl}` | No retry/idempotency: double-click or flaky network can duplicate |
| 2 | API | [WidgetController.Send](../../../src/NetIntercom.Api/Controllers/WidgetController.cs#L39-L46) | `VisitorId` taken from JWT claim, **not** the request body | `SendVisitorMessageCommand` | Visitor policy + per-IP rate limit (60/min) apply |
| 3 | MediatR pipeline | [ValidationBehavior](../../../src/NetIntercom.Application/Common/Behaviors.cs#L7-L32) | FluentValidation runs before the handler; failure → `ValidationException` → 400 | validator: body ≤ 10k chars | |
| 4 | Application | [SendVisitorMessage.cs#L52-L68](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L52-L68) | Loads existing conversation **scoped to this visitor** (`c.VisitorId == visitor.Id`) | `Conversation` w/ includes | The ownership filter here *is* the authorization — IDOR check lives in the query |
| 5 | Application | [SendVisitorMessage.cs#L71-L89](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L71-L89) | New conversation created; `RouteAsync` called before save | unsaved entity graph | Router queries DB mid-unit-of-work |
| 6 | Application | [ConversationRouter.RouteAsync](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L29-L73) | First enabled rule (by priority) matching body/URL wins; outside office hours → fallback team; SLA policy from team default | rules list, team, `SlaPolicyId` | All enabled rules loaded every message; no caching |
| 7 | Application | [ConversationRouter.PickRoundRobinAsync](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L75-L86) | Online member with oldest `LastAssignedAtUtc` gets it; nobody online → stays queued | `TeamMember` | **Race:** two concurrent messages can pick the same agent; nothing serializes this |
| 8 | Domain | [Conversation.RecordVisitorMessage](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L103-L127) | Reopens if not open; starts FirstResponse or NextResponse timer unless one is running | `SlaTimer.Start(...)` | No policy → silently no timer (by design) |
| 9 | Infrastructure | [AppDbContext.SaveChangesAsync](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L123-L149) | Audit stamps; one transaction for conversation+message+timer (single SaveChanges) | rows in `Conversations`, `Messages`, `SlaTimers` | |
| 10 | API realtime | [RealtimeNotifier.MessageCreatedAsync](../../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs#L20-L27) | Fan-out to `agents` group; visitors get it only if not an internal note | `MessageDto` | Runs **after** commit; a SignalR failure here throws after data is saved → client sees 500 for a write that succeeded |

**Validation & authorization:** [SendVisitorMessageValidator](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L23-L30); visitor identity via [CurrentUser.VisitorId](../../../src/NetIntercom.Api/Infrastructure/CurrentUser.cs#L20-L21); ownership enforced in the query (step 4).
**Persistence & side effects:** steps 9–10. Note the order: persist, then notify — DB is source-of-truth, hubs are delivery-only.
**Tests:** [HappyPathTests.VisitorToAgentConversationFlow](../../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs#L22-L104) covers steps 1–10 over real HTTP+Postgres; [ConversationTests](../../../tests/NetIntercom.UnitTests/ConversationTests.cs#L27-L55) covers step 8. The router's round-robin and office-hours fallback have **no direct test** — only `RoutingRule.Matches` and `OfficeHours.IsOpenAt` are unit-tested ([RoutingAndOfficeHoursTests](../../../tests/NetIntercom.UnitTests/RoutingAndOfficeHoursTests.cs#L7-L77)).
**Juniors miss:** the conversation id in the request body is *advisory*; the visitor id never is. Also that `StartNew` only matters when the referenced conversation is closed.
**Seniors notice:** router writes `LastAssignedAtUtc` on an entity loaded in the same context — assignment and message share one transaction (good) but there is no row lock or unique constraint preventing two routers from picking the same "least recently assigned" member concurrently (gap).
**Drill:** with the API running, send two messages in the same conversation 5 seconds apart, then close it from the inbox and send a third with `startNew: true`. Predict, before each send: state, timer kind, timer count. Verify in the DB (`select * from "SlaTimers"`).
**Self-grade:** Basic = can name the layers in order. Solid = can say where authorization happens and why step 10's position matters. Strong = can explain the round-robin race and propose a fix (e.g., `SELECT ... FOR UPDATE SKIP LOCKED` or unique scheduling token) with its costs.

---

## Flow 2: Agent login, token refresh, and the 401 dance

**Why it matters:** this is the repo's auth boundary, and the only flow where client and server have to agree on a *protocol over time* (expiry → 401 → refresh → retry).

**Open first:** [`api()`](../../../frontend/inbox/src/api.ts#L25-L60) — the client half of the contract.

**Trace:**

| Step | Owner | File | What happens | Data shape | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | Inbox | [Login.tsx#L24-L32](../../../frontend/inbox/src/pages/Login.tsx#L24-L32) | zod-validated form calls `login()` | `{email, password}` | |
| 2 | API | [AuthController.Login](../../../src/NetIntercom.Api/Controllers/AuthController.cs#L27-L30) | Anonymous + `auth` rate limit (10/min/IP) | `LoginCommand` | Brute-force mitigation is per-IP only |
| 3 | Application | [LoginCommandHandler.Handle](../../../src/NetIntercom.Application/Auth/Login.cs#L38-L59) | Email lowercased for lookup; PBKDF2 verify; refresh token stored **hashed** | `AuthResult` | Same generic 401 for unknown email vs wrong password (good); timing differs slightly (verify only runs when agent found) |
| 4 | Infrastructure | [TokenService.CreateAgentTokens](../../../src/NetIntercom.Infrastructure/Auth/TokenService.cs#L43-L64) | 15-min access JWT (roles: agent, +admin); 64-byte random refresh token, 14 days | `AgentTokenPair` | HMAC-SHA256 symmetric key from config |
| 5 | Inbox | [setTokens](../../../frontend/inbox/src/api.ts#L8-L18) | access → sessionStorage, refresh → **localStorage** | strings | XSS that can run JS can exfiltrate a 14-day credential |
| 6 | Inbox (later) | [api.ts#L38-L51](../../../frontend/inbox/src/api.ts#L38-L51) | On any 401: one-shot POST `/auth/refresh`, then retry original request once | | **Race:** N concurrent 401s → N parallel refreshes; rotation revokes the presented token, so the losers' retries fail → spurious logout |
| 7 | Application | [RefreshTokenCommandHandler.Handle](../../../src/NetIntercom.Application/Auth/Refresh.cs#L31-L56) | Hash lookup; reject if revoked/expired/agent inactive; **rotate**: revoke presented, issue new pair | `AuthResult` | Old rows accumulate forever (no purge job) |
| 8 | SignalR | [Program.cs#L60-L69](../../../src/NetIntercom.Api/Program.cs#L60-L69) | Hubs read the JWT from `?access_token=` query string (browsers can't set headers on WebSocket upgrade) | | Token may land in proxy/server logs as a URL param |

**Validation & authorization:** policies declared at [Program.cs#L73-L76](../../../src/NetIntercom.Api/Program.cs#L73-L76); claims read back via [CurrentUser](../../../src/NetIntercom.Api/Infrastructure/CurrentUser.cs#L6-L24).
**Persistence & side effects:** `RefreshTokens` rows (hashed) via [RefreshToken](../../../src/NetIntercom.Domain/Entities/AgentUser.cs#L17-L29).
**Tests:** login happy path + anonymous/visitor rejection in [HappyPathTests](../../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs#L106-L117). Refresh rotation has **no direct coverage** (no test posts to `/auth/refresh`).
**Juniors miss:** the refresh retry happens inside `api()` invisibly — a "random logout" bug report is usually step 6's race, not the server.
**Seniors notice:** rotation without a *grace window* or single-flight client mutex makes the race inevitable under concurrent queries (TanStack fires several on mount). Also: visitor tokens (30 days, [CreateVisitorToken](../../../src/NetIntercom.Infrastructure/Auth/TokenService.cs#L66-L75)) have no revocation story at all.
**Drill:** write the sequence diagram for two simultaneous 401s with rotation. Then design the minimal client fix (share one in-flight refresh promise) and the minimal server fix (accept the previous token for 60s). Which do you ship first, and why?
**Self-grade:** Basic = can list access vs refresh token lifetimes and storage. Solid = can explain rotation and why tokens are stored hashed. Strong = can articulate the race, both fixes, and the trade-off (client fix is cheap but every client must implement it; server grace weakens rotation's theft-detection property).

---

## Flow 3: Agent replies → SLA met → visitor sees it live

**Why it matters:** the read-side counterpart of Flow 1; shows how domain methods (not handlers) own SLA semantics, and how internal notes are kept from visitors at *every* hop.

**Open first:** [`SendAgentMessageHandler.Handle`](../../../src/NetIntercom.Application/Conversations/SendAgentMessage.cs#L36-L73).

**Trace:**

| Step | Owner | File | What happens | Data shape | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | Inbox | [Inbox.tsx#L209-L218](../../../frontend/inbox/src/pages/Inbox.tsx#L209-L218) | `send` mutation posts body + `isInternalNote`; appends to message cache on success | `MessageDto` | Optimistic-ish: cache append only if cache exists |
| 2 | API | [ConversationsController.Send](../../../src/NetIntercom.Api/Controllers/ConversationsController.cs#L50-L52) | Agent policy; agent id from claims | `SendAgentMessageCommand` | **Any agent can write to any conversation** — whole-inbox model, no team scoping |
| 3 | Application | [SendAgentMessage.cs#L60-L65](../../../src/NetIntercom.Application/Conversations/SendAgentMessage.cs#L60-L65) | Public reply → `RecordAgentReply`; note → nothing touches SLA or previews | | Reply to a **Closed** conversation is silently accepted and does not reopen it (only Snoozed reopens) — investigate: intended? |
| 4 | Domain | [Conversation.RecordAgentReply](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L87-L100) | Stamps `FirstAgentReplyAtUtc` once; marks all Running timers Met; Snoozed → reopen | | |
| 5 | Realtime | [RealtimeNotifier.MessageCreatedAsync](../../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs#L20-L27) | `agents` group always; `vconv:{id}` group **only if not a note** | `MessageDto` | The DTO still carries `isInternalNote` to agents — visitors never receive the event at all |
| 6 | Widget | [Widget.tsx#L38-L41](../../../frontend/widget/src/Widget.tsx#L38-L41) | `messageCreated` appended (dedup by id), typing indicator cleared | | |
| 7 | Widget | [Widget.tsx#L75-L78](../../../frontend/widget/src/Widget.tsx#L75-L78) | Open widget marks read → read receipts flow back via `messagesRead` | | |
| 8 | Application | [MarkReadByVisitorHandler](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L59-L88) | Ownership re-checked; only agent-authored public messages stamped | | |

**Validation & authorization:** body ≤ 10k ([SendAgentMessageValidator](../../../src/NetIntercom.Application/Conversations/SendAgentMessage.cs#L14-L21)); visitor-side ownership in step 8; agent-side — none beyond role (see risk register R2).
**Persistence & side effects:** message row + timer status changes in one SaveChanges; two SignalR sends after.
**Tests:** integration test asserts SLA met + note privacy ([HappyPathTests#L73-L98](../../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs#L73-L98)); domain transitions in [ConversationTests#L57-L81](../../../tests/NetIntercom.UnitTests/ConversationTests.cs#L57-L81).
**Juniors miss:** internal-note privacy is enforced in **three places** (notifier event routing, visitor message query filter [WidgetQueries.cs#L32-L39](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L32-L39), and the widget never asking for notes). Remove one and the others still hold — defense in depth, deliberate or not.
**Seniors notice:** `RecordAgentReply` marks *all* running timers met — currently only one can run at a time (invariant in `RecordVisitorMessage`), but the loop silently encodes "at most one" without asserting it. If someone later adds a Resolution timer kind, this loop quietly changes meaning.
**Drill:** list every code location that would have to be wrong simultaneously for a visitor to see an internal note. Then break exactly one (in a scratch branch) and write the integration test that catches it.
**Self-grade:** Basic = traces steps 1–6. Solid = names all three privacy enforcement points. Strong = articulates the closed-conversation-reply question and the timer-loop invariant, and proposes a test for each.

---

## Flow 4: SLA breach detection (background job)

**Why it matters:** the repo's only background write path. Teaches durable-scheduling vs in-memory, and what "survives restarts" actually means.

**Open first:** [`CheckSlaBreachesHandler.Handle`](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L31-L62).

**Trace:**

| Step | Owner | File | What happens | Data shape | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | Startup | [Program.cs#L133-L142](../../../src/NetIntercom.Api/Program.cs#L133-L142), [L187-L191](../../../src/NetIntercom.Api/Program.cs#L187-L191) | Hangfire server + `Cron.Minutely` recurring job, schedule persisted in Postgres | | Job and API share one process; heavy API load delays sweeps |
| 2 | Job | [SlaBreachJob.RunAsync](../../../src/NetIntercom.Infrastructure/BackgroundJobs/SlaBreachJob.cs#L10-L17) | Thin shim → MediatR command (so the logic is testable without Hangfire) | | |
| 3 | Application | [CheckSlaBreaches.cs#L34-L40](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L34-L40) | Loads Running timers past `BreachAtUtc`, with 4 chained Includes | list of `SlaTimer`+graph | Index exists for the filter ([AppDbContext#L95-L99](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L95-L99)); the Includes are the heavy part |
| 4 | Domain | [SlaTimer.MarkBreached](../../../src/NetIntercom.Domain/Entities/Sla.cs#L42-L51) | Running → Breached; anything else throws | | Two app instances running the job concurrently would race here (single-instance assumption) |
| 5 | Realtime | [CheckSlaBreaches.cs#L51-L59](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L51-L59) | Save first, then `slaBreached` per conversation to all agents | `ConversationDto` + kind | Notify-after-save: a crash between the two loses notifications but never breach state |
| 6 | Inbox | [Inbox.tsx#L48](../../../frontend/inbox/src/pages/Inbox.tsx#L48) | `slaBreached` → invalidate conversations query → badge appears | | If sorting by `slaBreach`, list reorders live |

**Validation & authorization:** none — internal command, never exposed over HTTP. Worth checking: nothing stops a future controller from exposing it accidentally; MediatR commands are only as private as their callers.
**Persistence & side effects:** timer status updates; SignalR fan-out.
**Tests:** none directly. The breach path is the **largest untested behavior** in the repo (unit tests cover `MarkBreached` transition only, [ConversationTests#L123-L132](../../../tests/NetIntercom.UnitTests/ConversationTests.cs#L123-L132)). Writing one is [test recipe 6](../05-quality-engineering/02-writing-tests-here.md).
**Juniors miss:** "every minute" is a *polling floor*, not a guarantee — a breach is detected up to ~60s late, plus job queue delay. SLAs are accurate to the minute, not the second, and the UI countdown ([SlaCountdown](../../../frontend/inbox/src/pages/Inbox.tsx#L148-L165)) shows seconds it can't honor.
**Seniors notice:** snoozing does **not** pause timers — a snoozed conversation can breach while hidden from the Open filter. Defensible (SLA is a promise to the visitor, not the agent) but never stated, and the seed data makes it easy to hit. Also: nothing reopens snoozed conversations when `SnoozedUntilUtc` passes — that's a missing job, see [mid-level ticket M2](../06-contribution-practice/02-mid-level-feature-tickets.md).
**Drill:** Hangfire is configured with Postgres storage. Explain precisely what survives an API restart: the schedule? a mid-flight job? a breach detected but not yet notified? Verify your answers against [Program.cs#L133-L142](../../../src/NetIntercom.Api/Program.cs#L133-L142) and the notify-after-save ordering.
**Self-grade:** Basic = can describe the poll loop. Solid = explains notify-after-save and what a crash loses. Strong = identifies the snooze/timer interaction and the multi-instance race, with a remediation for each.

---

## Flow 5: Presence — connect, heartbeat, grace, sweep

**Why it matters:** the only purely in-memory subsystem, and the input to round-robin assignment. Teaches hosted services, concurrent state, and the cost of "just keep it in memory."

**Open first:** [`PresenceTracker.Compute`](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L76-L90) — the whole state machine is these 15 lines.

**Trace:**

| Step | Owner | File | What happens | Data shape | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | Inbox | [realtime.ts#L18-L28](../../../frontend/inbox/src/realtime.ts#L18-L28) | Hub start + throttled (30s) mousemove/keydown heartbeats | | Heartbeat listeners are never removed — re-login in same tab stacks them (benign-ish) |
| 2 | Hub | [AgentHub.OnConnectedAsync](../../../src/NetIntercom.Api/Realtime/AgentHub.cs#L27-L33) | Tracker `Connected`; immediate `presenceChanged: Online` broadcast | | Connect broadcasts directly; *disconnect transitions are owned by the sweeper* — asymmetry is deliberate (grace) |
| 3 | Tracker | [PresenceTracker.cs#L30-L57](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L30-L57) | ConcurrentDictionary of connections + last activity/disconnect stamps | in-memory only | **Singleton, single-node**: second API instance = split-brain presence and broken round-robin |
| 4 | Tracker | [Compute](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L76-L90) | Offline iff no connections *and* >60s since last disconnect; Away iff idle >5min | enum | Status is *derived on read* — no stored state to go stale |
| 5 | Sweeper | [PresenceSweeper.SweepAsync](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L56-L87) | Every 15s: diff vs last broadcast, push `presenceChanged` + `teamPresence` | | `_lastBroadcast` never prunes departed agents (tiny leak) |
| 6 | Sweeper | [PersistAsync](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L89-L110) | Creates a DI **scope** to get a DbContext from a singleton — the captive-dependency escape hatch, done right | `PresenceSnapshot` rows | |
| 7 | Consumer | [ConversationRouter.PickRoundRobinAsync](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L75-L86) | `GetOnlineAgentIds()` gates assignment | | Presence flaps → assignment flaps |
| 8 | Widget | [Widget.tsx#L45](../../../frontend/widget/src/Widget.tsx#L45) | `teamPresence` toggles "We're online" | bool | |

**Validation & authorization:** hubs gated by Agent/Visitor policies ([AgentHub](../../../src/NetIntercom.Api/Realtime/AgentHub.cs#L12-L13), [VisitorHub](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L13-L14)).
**Persistence & side effects:** `PresenceSnapshot` upserts (step 6) — written for durability, **never read** by any code path. Dead write or future feature? Investigate.
**Tests:** none. The tracker is pure in-memory logic and eminently unit-testable (inject time — but note it uses `DateTime.UtcNow` directly, *not* `IClock`, unlike the rest of the codebase — see risk register R10).
**Juniors miss:** the grace period is why a page refresh doesn't flash an agent offline; and why "Online" in the UI can lag reality by up to 15s (sweep interval).
**Seniors notice:** `IClock` exists precisely for this ([Interfaces.cs#L26-L29](../../../src/NetIntercom.Application/Common/Interfaces.cs#L26-L29)) and the tracker bypasses it — making the grace-period logic untestable without real sleeps. Cheap, high-value refactor (good-first ticket G7).
**Drill:** write the state table for one agent: events (connect, disconnect, heartbeat, 61s pass, 5min pass) × resulting `Compute` output. Then answer: an agent closes their laptop at 17:00 with a conversation assigned at 16:59 — who notices, when, and what *doesn't* happen (hint: nothing reassigns).
**Self-grade:** Basic = can read the state machine. Solid = explains both grace periods and the sweeper diffing. Strong = explains the single-node constraint, what breaks at 2 replicas (presence, viewers, round-robin, SignalR groups), and sketches the Redis-backplane + shared-presence fix.
