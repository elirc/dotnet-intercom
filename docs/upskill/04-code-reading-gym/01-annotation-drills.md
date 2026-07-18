# Annotation Drills

For each target, produce a six-line annotation **before** reading the answer hints:

1. **Inputs** (including implicit ones: claims, clock, config)
2. **Outputs** (including side channels: events, logs)
3. **Dependencies** (what it needs alive to work)
4. **Invariants** (what must be true before/after)
5. **Side effects** (DB writes, network, mutation of shared state)
6. **Failure modes** (what throws, what silently misbehaves)

Write it as a comment block in a scratch file. Then check against the hints. Don't skip the writing — recognition is not recall.

**Rubric (per drill):** Basic = inputs/outputs/side effects correct. Solid = invariants stated precisely (not "it should work right"). Strong = at least one failure mode the hints list under "most people miss."

---

## Drill 1: [`ConversationRouter.RouteAsync`](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L29-L73)

Hints after you write: implicit inputs = clock (office hours), presence singleton, *unsaved* conversation entity. Invariant: conversation always ends with a TeamId if any team exists (fallback chain L50-51) — but may end with `AssigneeId == null`. Side effects: mutates the conversation **and** `TeamMember.LastAssignedAtUtc` (two aggregates, one transaction, caller saves). Most people miss: it never calls SaveChanges — its effects are hostage to the caller's unit of work; and `SlaPolicyId ??=` means a pre-set policy survives routing.

## Drill 2: [`Conversation.RecordVisitorMessage`](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L103-L127)

Hints: inputs = `nowUtc` only (pure-ish — good); requires `SlaPolicy` *navigation* loaded, not just the FK — `SlaPolicyId` set but navigation null silently starts **no timer**. Invariant: at most one Running timer afterward. Most people miss: the silent-null behavior is a loaded gun for any caller that forgets `.Include(SlaPolicy)` — and exactly one caller needs it ([SendVisitorMessage.cs#L84-L88](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L84-L88) re-fetches the policy for precisely this reason).

## Drill 3: [`AppDbContext.SaveChangesAsync`](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L123-L149)

Hints: inputs = entire ChangeTracker state (the most implicit input in the repo). Invariants: no `ISoftDeletable` row is ever physically deleted; `CreatedAtUtc` only stamped when default (seed data with explicit dates survives). Most people miss: `UpdatedAtUtc` is set for *any* Modified entity including the soft-delete conversion itself; and the audit walk happens before EF generates SQL, so computed values are part of the same transaction.

## Drill 4: [`PresenceTracker.Compute`](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L76-L90)

Hints: inputs = agent's connection set, two timestamps, **`DateTime.UtcNow` via parameter from callers that themselves call UtcNow** (not `IClock` — testability hole). Invariant: status is a pure function of observations + now; no stored status exists to disagree with it. Failure modes: an agent with a connection but no heartbeat for >5min is Away even while actively connected (heartbeats come from mouse/keys only — [realtime.ts#L23-L27](../../../frontend/inbox/src/realtime.ts#L23-L27)); reading a never-seen agent returns Offline (correct by construction). Most people miss: `LastDisconnectUtc` defaults to `default(DateTime)` = year 0001, so the 60s grace check is vacuously *false* for an agent whose connections all dropped before any disconnect was recorded — trace why that can't happen (Disconnected always stamps it) and what code change would break that coupling.

## Drill 5: [`api()` fetch wrapper](../../../frontend/inbox/src/api.ts#L25-L60)

Hints: implicit inputs = module-level `accessToken`/`refreshToken` (shared mutable state across all callers). Outputs: parsed JSON or thrown `Error` with ProblemDetails detail. Side effects: token rotation in storage; `window.location.href` navigation on refresh failure (hard escape from React). Failure modes: the 401-refresh-retry is **not** mutexed — concurrent callers race rotation (see key flow 2); a 204 returns `undefined as T` — type-level lie that's safe only because callers of DELETE/read endpoints don't use the result. Most people miss: `init.headers` spreads *after* the auth header, so a caller can accidentally (or deliberately) override Authorization.

## Drill 6: [`CheckSlaBreachesHandler.Handle`](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L31-L62)

Hints: inputs = clock + every Running timer past due, with four Include chains. Invariant: a timer is marked Breached at most once ever (status guard in [MarkBreached](../../../src/NetIntercom.Domain/Entities/Sla.cs#L42-L51) + the Running filter make re-runs idempotent). Side effects: DB save, then N SignalR broadcasts. Failure modes: notifier failure after save = breach recorded, escalation lost, **and Hangfire marks the job failed and retries — but the retry finds no Running overdue timers, so the lost notifications stay lost**. Most people miss: that retry analysis; also that breaches on *snoozed* conversations notify agents about something invisible in their default Open filter.

## Drill 7: [`Widget.tsx` bootstrap effect](../../../frontend/widget/src/Widget.tsx#L24-L73)

Hints: inputs = `config` (closed over once — the `eslint-disable` on L72 is the tell), cookie identity ([getAnonymousId](../../../frontend/widget/src/api.ts#L28-L37)). Side effects: network session, SignalR connection stored in a ref, cleanup stops it. Invariants: messages list deduped by id; `cancelled` flag prevents setState-after-unmount. Failure modes: any error in the whole async block → one generic `status: "error"` (no retry path — the visitor must reload); a conversation created *after* mount via `send()` joins the hub group at [L86-L89](../../../frontend/widget/src/Widget.tsx#L86-L89), but if `conn.start()` hadn't finished when the user sent, the invoke rejects and is swallowed by `.catch(() => {})`. Most people miss: that swallowed join = realtime silently dead for the rest of the session, fixed only by the read-receipt-triggered refetches… which don't exist in the widget. This is debugging scenario 4.

## Drill 8: [`SaveCannedReplyHandler.Handle`](../../../src/NetIntercom.Application/CannedReplies/CannedReplies.cs#L71-L97)

Hints: inputs include `request.Personal` which *re-homes* the reply (personal ↔ team) on edit. Invariants intended: only the owner edits personal replies (checked, L78-81). Invariants **missing**: nothing verifies the agent belongs to `TeamId` (create or edit), so any agent can create/modify replies for any team — risk R3. Failure modes: editing a *team* reply has no ownership/membership check at all. Most people miss: the validator requires TeamId for non-personal but never validates the team exists — `SaveChangesAsync` throws an FK violation → unmapped exception → 500 instead of 404/422.
