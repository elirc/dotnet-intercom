# NetIntercom — Architecture & Code Review

*Reviewed: 2026-06-11. Scope: full backend (`src/`), both frontends (`frontend/`), tests, CI, and runtime configuration.*

## Verdict

This is a well-built codebase that does what the README promises. The layering is honest (Domain has real invariants, Application is thin and consistent, Infrastructure is swappable), the "hubs are delivery-only, DB is source-of-truth" realtime design is the right call and is applied consistently, and the operational basics — problem details, correlation IDs, rate limiting, health checks, Testcontainers integration tests — are all present. The issues below are mostly the gaps you'd expect to find when an Intercom-style product moves past a single node and a trusting client: one real authentication hole in the widget identity model, a handful of authorization/consistency gaps, and a set of single-instance assumptions that should at least be written down.

Findings are ordered by severity within each section. File references are clickable.

---

## 1. Security

### 1.1 HIGH — Visitor identity takeover via client-supplied `externalId`

`POST /api/v1/widget/session` is anonymous and looks up the visitor by whatever the client sends:

- `src/NetIntercom.Application/Widget/StartConversation.cs:51-52` — `v.ExternalId == request.ExternalId || v.AnonymousId == request.AnonymousId`
- The handler then issues a visitor JWT for the matched row and returns the **full conversation history**.

Nothing proves the caller *is* `externalId: "user-123"`. Any visitor (or curl) can claim another user's external ID, receive that visitor's token, read their transcript, and overwrite their `Name`/`Email`/`AttributesJson` (lines 60–63 update unconditionally). The downstream ownership checks everywhere else are solid, but they all trust this token — so this is the front door.

This is exactly why Intercom requires *identity verification*: an HMAC of the external ID computed server-side by the host site with a shared secret, sent alongside the ID. **Recommendation:** add a `userHash` field to the session payload, verify `HMAC-SHA256(externalId, workspaceSecret)`, and refuse to match on `externalId` without it. `anonymousId` matching is acceptable as-is (client-generated 128-bit value, effectively a bearer secret).

### 1.2 MEDIUM — 30-day visitor JWTs with no revocation

`src/NetIntercom.Infrastructure/Auth/TokenService.cs:74` — visitor tokens live 30 days and there is no revocation mechanism. Combined with 1.1, a hijacked identity stays hijacked for a month even after the hole is closed. Consider short-lived visitor tokens (the widget calls `/session` on every load anyway, so re-issuing is free) or a token-version claim on the Visitor row.

### 1.3 MEDIUM — Refresh token rotation lacks reuse detection

`src/NetIntercom.Application/Auth/Refresh.cs` rotates correctly (presented token revoked, new pair issued) but a *revoked* token presented again just gets a 401. Reuse of a rotated token is the canonical signal that the token was stolen; the standard response is to revoke the whole token family for that agent. Right now an attacker who exfiltrates the refresh token from `localStorage` (see 1.4) keeps a valid session indefinitely as long as they rotate before the legitimate client does.

### 1.4 LOW — Agent refresh token in `localStorage`

`frontend/inbox/src/api.ts:6,13` — access token in `sessionStorage`, refresh token in `localStorage`. Any XSS in the inbox app yields a long-lived credential. The conventional fix is an httpOnly cookie for the refresh token (the API and inbox are same-site via the Vite proxy, so this is feasible). Acceptable for a learning project, but worth a comment in the code acknowledging the tradeoff.

### 1.5 LOW — `VisitorHub.Typing` skips the ownership check

`src/NetIntercom.Api/Realtime/VisitorHub.cs:48-58` — `JoinConversation` verifies the conversation belongs to the visitor, but `Typing` does not. Any authenticated visitor can emit typing indicators (with their display name) into any conversation's agent view by guessing/replaying a conversation ID. Cosmetic impact, one-line fix: reuse the same `AnyAsync` ownership check, or track joined conversation IDs in `Context.Items`.

### 1.6 LOW — Widget CORS allows any origin *with credentials*

`src/NetIntercom.Api/Program.cs:106-110` — `SetIsOriginAllowed(_ => true)` + `AllowCredentials()`. Since the widget authenticates with bearer tokens rather than cookies, this isn't currently exploitable, but "reflect any origin and allow credentials" is the configuration browsers specifically forbid with `*` for a reason. Drop `AllowCredentials()` from the widget policy — nothing on that path uses cookies.

### 1.7 LOW — Rate limiting keys on `RemoteIpAddress` with no forwarded-headers support

`src/NetIntercom.Api/Program.cs:117-122` — behind any reverse proxy every request shares one partition (`unknown` or the proxy IP), turning the limiter into a global throttle and a self-DoS. There's no `UseForwardedHeaders()` anywhere. Fine on localhost; breaks on first real deployment. Also note 60 req/min/IP on the anonymous `/widget/session` endpoint still allows unbounded `Visitor` row creation over time — no cleanup job exists.

### 1.8 LOW — Canned-reply authorization is looser than it looks

`src/NetIntercom.Application/CannedReplies/CannedReplies.cs:78-81` — the edit guard only protects *personal* replies. Any agent can edit any **team** reply, including teams they don't belong to, and can create replies for arbitrary teams (`SaveCannedReplyCommand.TeamId` is unchecked against membership). Similarly `RenderCannedReplyHandler` renders any reply, including other agents' personal ones. Low stakes, but inconsistent with the listing query, which carefully scopes to own-plus-team.

### 1.9 INFO — Hangfire dashboard is effectively unreachable, which is fine but accidental

`Program.cs:189` protects `/hangfire` with the Admin JWT policy — but a browser visiting the dashboard sends no `Authorization` header, so even admins get 401. It's *secure*, just not usable as documented in the README. Either accept a cookie scheme for it or note that it's intentionally dev-only.

---

## 2. Correctness

### 2.1 HIGH — Snoozed conversations never wake up

`Conversation.Snooze` stores `SnoozedUntilUtc`, but nothing in the codebase ever reads it back to reopen the conversation — no Hangfire job, no check in the list query (verified by grep: the only readers are the DTO and the entity itself). A snoozed conversation reopens only if the visitor messages again or an agent replies. "Snooze until Friday" silently means "snooze forever" for a quiet visitor — the core promise of snooze is broken. Fix: either a recurring job (`WHERE State = Snoozed AND SnoozedUntilUtc <= now`) alongside `SlaBreachJob`, or treat it as a computed state at query time.

### 2.2 MEDIUM — Optimistic concurrency token exists but conflicts aren't handled

`Conversation.RowVersion` maps to xmin (`AppDbContext.cs:79`), so concurrent writes throw `DbUpdateConcurrencyException` — but no handler or the `ApiExceptionHandler` catches it (verified by grep). Two agents assigning/closing the same conversation simultaneously produces a raw 500 instead of a 409 with a retry hint. The infrastructure for the right behavior is half-built; map the exception to 409 Conflict in `ApiExceptionHandler` and surface it in the inbox UI.

### 2.3 MEDIUM — `SlaBreachJob` is not safe to run concurrently

Hangfire can fire a recurring job while the previous run is still executing (or on two servers). Two overlapping runs both load the same `Running` timers; the loser of the race hits `MarkBreached`'s guard (`Sla.cs:44-46`) and throws `DomainException`, failing the whole batch and re-notifying agents on retry. Add `[DisableConcurrentExecution]` to `SlaBreachJob.RunAsync`, or make breaching idempotent (set-based `UPDATE ... WHERE Status = Running`).

### 2.4 MEDIUM — Widget reconnect catch-up uses a stale closure

`frontend/widget/src/Widget.tsx:52-58` — `conn.onreconnected` captures `session.conversation` from the bootstrap effect. For a *new* visitor whose conversation is created mid-session via `send()`, that closure is `null`, so after a reconnect no catch-up fetch happens and missed agent messages never appear until a full reload. Use a ref that tracks the current conversation instead of the bootstrap snapshot. (Also: the catch-up refetches the full transcript rather than using the `?after=` parameter the API was built for.)

### 2.5 MEDIUM — Concurrent 401s race the one-shot token refresh

`frontend/inbox/src/api.ts:38-51` — when the access token expires, every in-flight request 401s and each independently calls `/auth/refresh` with the same refresh token. Rotation (1.3) means the first wins and revokes the token; the rest fail refresh → `setTokens(null)` → hard redirect to `/login`. Users get randomly logged out exactly once per 15-minute token lifetime under concurrent load (an inbox with TanStack Query polling will hit this). Standard fix: share a single in-flight refresh promise.

### 2.6 LOW — Visitor upsert can race into a unique-index violation

`StartConversation.cs:51-57` — check-then-insert on `AnonymousId` with a unique index. Two parallel session calls (widget open in two tabs on first visit) can both miss and one insert fails as an unhandled 500. Catch the violation and re-query, or use an upsert.

### 2.7 LOW — Round-robin assignment races

`ConversationRouter.PickRoundRobinAsync` reads `LastAssignedAtUtc` without locking; two conversations routed simultaneously pick the same agent. Harmless at this scale, but worth a comment — it's the kind of thing that looks like a bug in production metrics.

### 2.8 LOW — `?after=` catch-up can miss same-timestamp messages

Both message catch-up queries filter `CreatedAtUtc > after`. Messages written in the same batch share an `IClock.UtcNow` value, and timestamp ties across batches are possible. A client whose last-seen message shares a timestamp with a missed one will skip it. Tie-break with a monotonic sequence (the `(ConversationId, CreatedAtUtc)` index suggests adding an identity column) or use `>=` plus client-side dedupe (the widget already dedupes by ID; the inbox would need to).

### 2.9 LOW — Message ordering has no deterministic tie-break

Transcript queries `OrderBy(m => m.CreatedAtUtc)` only. Same-timestamp messages (same SaveChanges batch) come back in nondeterministic order. Add `.ThenBy(m => m.Id)` — cheap insurance.

---

## 3. Architecture & scalability

### 3.1 The single-node assumption is load-bearing and undocumented

Three things quietly assume exactly one API instance:

1. **`PresenceTracker`** (`Api/Realtime/PresenceTracker.cs`) is in-process memory. With two nodes, an agent connected to node A is "offline" on node B — and `ConversationRouter` consults presence to assign conversations, so **routing breaks**, not just presence badges.
2. **SignalR groups** have no backplane (no Redis/Azure SignalR), so events fan out only to clients on the node that processed the write.
3. **`PresenceSweeper`'s** `_lastBroadcast` state is per-node.

None of this is wrong for a learning project, but the README sells durability ("survives restarts") without stating the boundary. Document it prominently, and note the migration path (Redis backplane + presence in Redis with TTL keys) — the `IPresenceService` abstraction already makes this swap clean, which is good design.

### 3.2 Realtime delivery is best-effort with no outbox — acceptable, but know the failure mode

Handlers `SaveChangesAsync` then call `IRealtimeNotifier` (e.g. `SendVisitorMessage.cs:107-112`). If the process dies or SignalR throws between the two, the write is durable but no event is delivered, and the documented recovery (`?after=` refetch) only triggers on *reconnect* — a connected client that simply missed the event shows stale data until the user acts. A transactional outbox is overkill here; a cheap mitigation is periodic reconciliation (the inbox already polls via TanStack Query in places). Worth a sentence in the README's realtime section.

### 3.3 In-memory presence/viewer maps grow without bound

`PresenceTracker._agents` entries are never removed (offline agents stay forever), `_viewers` conversation keys are never pruned when empty, and `PresenceSweeper._lastBroadcast` accumulates every agent ever seen. Trivial leak at three seeded agents; a real leak at SaaS scale. Sweep dead entries in `PresenceSweeper`.

### 3.4 Dual presence broadcast paths can disagree

`AgentHub.OnConnectedAsync` broadcasts `presenceChanged: Online` immediately, but doesn't update the sweeper's `_lastBroadcast` map — so the sweeper may rebroadcast the same transition (harmless) or, worse, consider the *previous* state current and suppress a real transition for up to one interval. Pick one owner for presence broadcasts (the sweeper, with a shorter interval, or push the connect event through the sweeper's state).

### 3.5 `ConversationDto` is shared between agent and visitor surfaces

`RealtimeNotifier.ConversationUpdatedAsync` carefully projects a minimal `{id, state, assigneeName}` for the visitor hub — but the widget REST endpoints (`POST /widget/messages`, `POST /widget/session`) return the **full** `ConversationDto`: SLA deadlines and breach status, team name, internal IDs. Nothing catastrophic leaks, but the inconsistency means the careful hub projection is security theater. Define a `WidgetConversationDto` and use it on every visitor-facing path.

### 3.6 `Include(c => c.SlaTimers)` loads full timer history everywhere

Every conversation read (`ListConversations`, `GetConversation`, the shared `LoadAsync`, `CheckSlaBreaches`' `ThenInclude`) loads *all* timers, but the DTO only needs the earliest running one plus a breached flag. A long-lived conversation accumulates timers without bound, and the inbox list multiplies that by page size. Use a filtered include (`.Include(c => c.SlaTimers.Where(t => t.Status == SlaTimerStatus.Running))`) and compute `HasBreachedSla` as a projected subquery — EF Core 8 handles both.

### 3.7 Misc design notes (no action strictly required)

- **`AuthController.Me`** (`AuthController.cs:39-44`) fetches *all* agents to find one — add a `GetAgentQuery(id)`.
- **Search** (`ListConversations.cs:55-62`) does `ToLower().Contains()` over three columns — unindexable. Fine now; `pg_trgm` or `ILIKE` with an index when it matters.
- **`Login`** compares against `request.Email.ToLowerInvariant()` — works only because the seeder lowercases. Normalize on write (or use `citext`) so an admin-created mixed-case email doesn't brick login. No account lockout either; the IP rate limiter is the only brute-force control (and see 1.7).
- **`EnsureCreated` fallback** (`Program.cs:150-157`): once a database is created this way, switching to migrations requires dropping it. The README covers this, but the code path deserves the same warning comment.
- **`StartWidgetSessionCommand.AttributesJson`** has no size limit (jsonb column, validator silent) — a visitor can store megabytes per session call. Cap it in the validator.
- **`OfficeHours.IsOpenAt`** swallows `TimeZoneNotFoundException` by silently treating the team as UTC — a typo'd timezone changes routing behavior with no signal. Log it, or validate `TimeZoneId` at save time (there's currently no admin endpoint for office hours at all — they're seed-only).
- **Reports** date params (`ReportsController.cs`) accept unbounded ranges and `GetOverviewReportQuery` materializes every conversation in range into memory for median math. Fine until it isn't; cap the range.

---

## 4. What's done well (keep these)

- **Domain invariants live in the domain.** `Conversation` owns its state machine and SLA timer lifecycle (`RecordAgentReply`, `RecordVisitorMessage`); handlers orchestrate but don't reimplement rules. The unit tests target exactly this layer.
- **Delivery-only hubs.** Writes go through REST, hubs only fan out, reconnect catch-up reads the DB. This is the correct architecture for chat and it's applied consistently on both hubs.
- **Visitor scoping discipline.** Every widget query/command re-checks `VisitorId` ownership against the token claim (`WidgetQueries.cs:25-30, 61-66`, `VisitorHub.JoinConversation`). The one gap (1.5) stands out precisely because the rest is consistent.
- **Internal-note isolation** is enforced at every boundary: persistence query (`!m.IsInternalNote`), notifier (`RealtimeNotifier.cs:23-26`), SLA logic (`SendAgentMessage` skips `RecordAgentReply` for notes), and preview text.
- **Honest test pyramid**: domain unit tests for the tricky logic (SLA, routing, office hours, placeholders) + a real-PostgreSQL happy-path integration suite via Testcontainers, both wired into CI.
- **Operational hygiene**: RFC 7807 everywhere with correlation IDs threaded into responses and logs, rate limiting on the only anonymous surfaces, health checks, Serilog + OTel, refusal to boot without a signing key.

## 5. Suggested priority order

| # | Item | Severity | Effort |
|---|------|----------|--------|
| 1 | Widget identity verification (HMAC) for `externalId` (1.1) | High | Medium |
| 2 | Snooze auto-reopen job (2.1) | High | Small |
| 3 | Map `DbUpdateConcurrencyException` → 409 (2.2) | Medium | Small |
| 4 | `[DisableConcurrentExecution]` / idempotent breach update (2.3) | Medium | Small |
| 5 | Widget reconnect ref + `?after=` catch-up (2.4) | Medium | Small |
| 6 | Single-flight token refresh in inbox (2.5) | Medium | Small |
| 7 | Refresh-token reuse detection (1.3) | Medium | Medium |
| 8 | `WidgetConversationDto` for visitor-facing endpoints (3.5) | Low | Small |
| 9 | Document the single-node boundary + backplane path (3.1) | Low | Trivial |
| 10 | Filtered SLA-timer includes (3.6) | Low | Small |
