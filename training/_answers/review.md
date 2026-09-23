# Model review: NetIntercom `main` @ f2198dd

Ranked by production severity. "AI-smell" marks typical patterns of old AI-generated code. Items marked **(new)** are *not* in `docs/architecture-review.md` or the upskill risk register. The rest overlap with them, sometimes with a correction.

## Critical
**R1. Visitor identity takeover via client-supplied `externalId`.** `Application/Widget/StartConversation.cs:51-52` matches on `ExternalId` from an anonymous request, issues a 30-day visitor token (`TokenService.cs:66-75`) and returns the full history, then overwrites name/email/attributes (`:60-63`). Any caller who knows or guesses a host site's user id reads that customer's transcripts. Fix: HMAC identity verification (`userHash`). *AI-smell: trust boundary taken from a tutorial, no threat model.*

**R2. Production seeds a known admin password (new).** `Seed:Enabled` is `true` in the **base** `appsettings.json:16-18`, not in Development. `DbSeeder` creates `admin@netintercom.local` / `Passw0rd!` on any empty database (`DbSeeder.cs:11, 15-18, 39`) and logs the password (`:78`). A first production deploy ships a publicly documented admin login (README:45-51, the login page hint `Login.tsx:38`). Fix: move to `appsettings.Development.json` and refuse to seed outside Development. *AI-smell: dev convenience left in shared config.*

## High
**R3. Widget stops receiving live messages after any reconnect (new, partly in B §2.4).** Groups belong to connection ids. `withAutomaticReconnect` gets a new id, and nobody re-invokes `JoinConversation` (`Widget.tsx:52-63`). `VisitorHub.OnConnectedAsync` re-adds only `"visitors"` (`VisitorHub.cs:31`). Every visitor who has a network blip gets no live replies until reload. The inbox loses `conv:{id}` (typing/collision) the same way (`Inbox.tsx:193-203`). B only reports the stale closure.

**R4. Snoozed conversations never wake.** `SnoozedUntilUtc` is written (`Conversation.cs:63-77`) and read nowhere. Snooze keeps SLA timers running. *AI-smell: feature half-implemented. The field exists, the behavior doesn't.*

**R5. SLA sweep and reply race, with duplicate escalations under overlap.** `SlaTimer` has no concurrency token (`AppDbContext.cs:95-99`). Overlapping sweeps each load `Running` in their own `DbContext`, and each marks, saves and notifies **without error**. B §2.3's claim that the loser throws `DomainException` is incorrect: the guard is in-memory. The reply/sweep interleaving is a lost update (the toast says breached, the row says Met). Fix: set-based `UPDATE … WHERE Status = Running RETURNING`.

**R6. `MarkMet` ignores the deadline (new).** `Sla.cs:31-40`. A reply after `BreachAtUtc` but before the next sweep counts as Met. The SLA report depends on sweep timing, so up to 60 s of silent grace per breach.

**R7. Concurrent 401s log agents out.** `frontend/inbox/src/api.ts:38-51` has no single-flight refresh, and rotation (`Refresh.cs:43`) revokes the token the parallel requests are still using, then `setTokens(null)` wipes the fresh pair and hard-reloads.

**R8. No idempotency on sends, and the widget loses text on failure.** `SendVisitorMessage.cs:47-115` inserts on every call. `Widget.tsx:80-90` clears the textbox before `await` and has no `catch` (unhandled rejection, message gone). A retried first message creates a second conversation.

## Medium
**R9. Single-node assumptions.** In-memory `PresenceTracker` feeds routing (`ConversationRouter.cs:81`), there's no SignalR backplane (`Program.cs:37`), and the sweeper state is per process. At 2+ replicas, routing assigns only to agents connected to the node that handled the request.

**R10. Notify-after-commit with no outbox.** All handlers call `SaveChangesAsync` then `IRealtimeNotifier`. A crash in between leaves connected clients stale. Mitigated only by reconnect catch-up, which is itself broken (R3).

**R11. `DbUpdateConcurrencyException` becomes a 500.** `RowVersion` is mapped (`AppDbContext.cs:79`), but `ApiExceptionHandler.cs:18-33` doesn't map it. `LoggingBehavior` logs it at Error.

**R12. The inbox list query is expensive, and every event multiplies it (partly new).** `ListConversations.cs:28-33` uses full `SlaTimers` include, a correlated `MIN` sort (`:67-70`), an unindexable `ToLower().Contains` (`:55-62`), and `COUNT(*)` per page. **New:** each `messageCreated`/`conversationUpdated` invalidates the list for **every** agent (`Inbox.tsx:41,47`), so DB load scales with agents × events.

**R13. Unbounded transcript endpoint (new).** `GetConversation.cs:41-59` returns every message. The inbox always calls it without a cursor (`Inbox.tsx:184`).

**R14. Visitor surfaces receive full agent DTOs.** `/widget/session` and `/widget/messages` return `ConversationDto` with SLA/team data (`StartConversation.cs:89-93`, `WidgetController.cs:43-45`), while the hub carefully projects (`RealtimeNotifier.cs:32-37`). It's a leak waiting for a new field (see incident 001).

**R15. Rate limiter keyed on `RemoteIpAddress` with no forwarded headers.** `Program.cs:114-123`. Behind a proxy it becomes one global bucket, so a self-inflicted DoS.

**R16. Inbox handler stacking (new).** `Inbox.tsx:36-81` registers `conn.on(...)` on every mount and never calls `conn.off`. The connection is a module singleton (`realtime.ts:4-16`), so navigating Inbox → Reports → Inbox doubles every handler (N invalidations per event). `startConnection` adds new `mousemove`/`keydown` listeners every time the connection restarts (`realtime.ts:24-25`).

**R17. The observability claim is hollow (new).** The README advertises OpenTelemetry. `Program.cs:128-130` configures tracing with **no exporter** (traces go nowhere) and no EF/Npgsql/Hangfire/SignalR instrumentation or metrics. Request logs carry no user or visitor id (see incident 004 blast radius). *AI-smell: the docs claim capabilities the code doesn't have.*

**R18. Schema via `EnsureCreated` in production.** `Program.cs:150-157`. No migrations, so no schema change can ever reach an existing database (EnsureCreated no-ops on a DB that already exists). Every index or column in the ladder is blocked on this. Every replica runs it at startup.

## Low
**R19. Authorization gaps on the agent side.** Any agent can act on any conversation. Canned replies: team membership isn't checked on save/edit/render (`CannedReplies.cs:71-97, 132-150`). `VisitorHub.Typing` has no ownership check (`VisitorHub.cs:48-58`).

**R20. Widget CORS reflects any origin with credentials.** `Program.cs:106-110`. Bearer-only today, so not exploitable, but it's the wrong default.

**R21. Presence maps grow without bound, with two broadcast paths.** `PresenceTracker._agents`, `_viewers`, `PresenceSweeper._lastBroadcast`. `AgentHub.OnConnectedAsync:31` broadcasts outside the sweeper. (Incident 002 is what a naive fix of this looks like.)

**R22. `LoggingBehavior` logs expected 4xx exceptions at Error (new).** `Behaviors.cs:50-54` logs NotFound/Forbidden/Domain/Unauthorized at Error. Error-rate alerts become useless (every failed login is an Error).

**R23. Tests assert less than their names claim (new).** `HappyPathTests.cs:61` asserts only `teamName != null`, because routing depends on wall-clock office hours. There's no negative authz test on any widget endpoint or hub method, no tests for presence, the router, the SLA sweep or the hubs, and no frontend tests (CI only builds the frontends: `ci.yml:36-40`). *AI-smell: a test name promises more than the test checks.*

**R24. Dead or placeholder UI code (new).** `Widget.tsx:128` renders an empty `<span />` for "unread" (a fake read receipt). The `messagesRead` handler is an empty stub (`:49-51`), while the README lists it as a feature. *AI-smell: placeholder behavior shipped as if done.*

**R25. Docs vs code: `?after=` catch-up.** README:79 says both clients refetch with `?after=`. Neither does (`Widget.tsx:55`, `Inbox.tsx:73-76` invalidates everything).

**R26. Visitor upsert race and unbounded `AttributesJson`.** `StartConversation.cs:51-57` does check-then-insert on a unique index → 500. There's no size limit on the jsonb attributes.

## What's genuinely good (say this in an interview too)
Domain invariants live on `Conversation`/`SlaTimer`. The hubs are delivery-only and the DB is the source of truth. Internal-note filtering is applied on every read path. RFC 7807 errors come with correlation ids. Integration tests run against real Postgres. The app refuses to boot without a signing key (`Infrastructure/DependencyInjection.cs:16-21`).

## Grading
- 8+ findings including R1 and at least 2 of {R2, R3, R5, R7} → Solid mid.
- Also R2 and R3 (the two biggest *new* ones), plus a concurrency interleaving written out explicitly → senior signal.
- Credit for any finding here that you *disprove* with evidence.
