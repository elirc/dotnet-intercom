# Interview Prep from This Repo

Target: **mid-level interviews** (with the senior layer shown so you know where the ceiling is). Every question is answerable with concrete evidence from this codebase — practice citing files, not generalities. "I did X in a chat platform I built: here's the trade-off I hit" beats any textbook answer.

Format per question: what a **junior** answer sounds like → what **mid** adds → what **senior** includes.

---

## A. C#/.NET runtime

**A1. "Walk me through what `await` actually does in one of your handlers."**
Use [`SendVisitorMessageHandler.Handle`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L47-L115).
- *Junior:* "It waits for the DB call without blocking the thread."
- *Mid:* compiler state machine; the thread returns to the pool during `FirstOrDefaultAsync`; continuation resumes on a pool thread (no `SynchronizationContext` in ASP.NET Core, so no `ConfigureAwait` needed); `CancellationToken ct` flows so an aborted request cancels the query.
- *Senior:* why sync-over-async (`.Result`) would deadlock classic ASP.NET but "merely" starves the pool here; when `ValueTask` would matter (hot cache-hit paths, not this); that the real concurrency hazard in this method isn't threading — it's two requests interleaving at the DB (round-robin race).

**A2. "Why does this codebase inject `IClock` instead of using `DateTime.UtcNow`?"**
Evidence: [`IClock`](../../../src/NetIntercom.Application/Common/Interfaces.cs#L26-L29), used everywhere *except* [`PresenceTracker`](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L34) — a real inconsistency you can discuss.
- *Junior:* "So tests can fake the time."
- *Mid:* determinism for SLA math ([ConversationTests](../../../tests/NetIntercom.UnitTests/ConversationTests.cs#L11) pins `Now`); names time as a dependency.
- *Senior:* points out the tracker bypass makes its grace-period logic untestable without real sleeps — and that the cost of fixing it now (trivial) vs after more code depends on it is exactly why seams rot.

**A3. "Records vs classes — when did this codebase use each, and why?"**
Evidence: DTOs/commands are records ([Dtos.cs](../../../src/NetIntercom.Application/Dtos.cs#L6-L94)); entities are classes ([Conversation](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L5-L19)).
- *Junior:* "Records are immutable data holders."
- *Mid:* value equality + `with` for DTOs; entities need identity-based equality and mutable tracked state — EF change tracking and records' value semantics fight each other.
- *Senior:* nullability as API documentation (`Guid? TeamId` means "unrouted is a legal state"); how positional records make wire contracts greppable; the trap of using records for entities with collections (value equality over lists is a lie).

## B. ASP.NET / EF / architecture

**B1. "Where does authorization happen for the widget API?"**
Evidence: role policy at the controller ([WidgetController](../../../src/NetIntercom.Api/Controllers/WidgetController.cs#L40-L41)) + ownership in the query ([WidgetQueries](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L25-L30)).
- *Junior:* "`[Authorize]` on the controller."
- *Mid:* two layers — authentication/role at the edge, resource ownership in the WHERE clause; visitor id comes from the JWT claim, never the body.
- *Senior:* names the gap pattern-by-omission: [`VisitorHub.Typing`](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L48-L58) doesn't re-check ownership; generalizes to "every entry point needs the same predicate — REST, hub method, background job" and proposes centralizing the predicate.

**B2. "You have an N+1 risk — where, and how do you know?"**
Evidence: [`ListConversationsHandler`](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L28-L33) eager-loads four navigations; lazy loading isn't enabled anywhere (no `UseLazyLoadingProxies` in [Infrastructure DI](../../../src/NetIntercom.Infrastructure/DependencyInjection.cs#L29-L31)).
- *Junior:* "Use Include to avoid N+1."
- *Mid:* without Include, `ConversationDto.From` would NRE rather than lazy-load (proxies off) — so this repo's failure mode is *crash, not N+1*, which is arguably better; collection Includes with pagination cause row multiplication, consider `AsSplitQuery`.
- *Senior:* the page-of-25 with 4 includes is a cartesian-ish payload — proposes projection (`Select` straight to DTO) as the real fix, notes the repo already does this in [GetVisitorMessagesHandler](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L32-L39), and frames it: tracked-entities-then-map vs projection is a write-path vs read-path distinction.

**B3. "DI lifetimes: find the bug-shaped decision in `Program.cs`."**
Evidence: [`PresenceSweeper`](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L89-L110) is a singleton hosted service that needs scoped `AppDbContext` — and solves it correctly with `IServiceScopeFactory`.
- *Junior:* explains singleton/scoped/transient definitions.
- *Mid:* the captive-dependency trap (singleton holding a scoped DbContext = one context forever, concurrency exceptions); shows the scope-per-sweep solution in this file.
- *Senior:* `TokenService` as singleton is fine because `JwtOptions` is immutable; what would break if someone made `AppDbContext` transient (SaveChanges sees nothing); why `IRealtimeNotifier` can be singleton (IHubContext is).

**B4. "How does this system survive an API restart mid-conversation?"**
Evidence: Hangfire Postgres storage ([Program.cs#L133-L142](../../../src/NetIntercom.Api/Program.cs#L133-L142)); DB-as-source-of-truth + reconnect catch-up ([Widget.tsx#L52-L58](../../../frontend/widget/src/Widget.tsx#L52-L58)); presence does *not* survive ([PresenceTracker](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs#L25-L28) is in-memory).
- *Junior:* "Data is in Postgres, so it's fine."
- *Mid:* enumerates per-subsystem: messages/timers durable; SLA schedule durable; SignalR connections drop and auto-reconnect; presence resets (everyone Offline until reconnect) — and round-robin depends on presence, so assignment pauses.
- *Senior:* the restart reveals the single-node assumption; scale-out requires a SignalR backplane + shared presence store; orders the work by user impact.

## C. Debugging (scenario questions)

**C1. "Agents report being randomly logged out. Go."**
- *Junior:* checks server logs for 401s.
- *Mid:* reproduces with concurrent requests; finds the refresh race in [`api.ts#L38-L51`](../../../frontend/inbox/src/api.ts#L38-L51) — N parallel 401s → N refreshes → rotation ([Refresh.cs#L43](../../../src/NetIntercom.Application/Auth/Refresh.cs#L43)) revokes all but one.
- *Senior:* proposes single-flight refresh promise client-side now, server grace-window later; adds the regression test; asks "why did access tokens start expiring mid-session — did someone change `AccessTokenMinutes`?" (root cause vs trigger).

**C2. "A visitor says the chat shows 'We're online' at 3am when nobody is."**
- *Junior:* suspects the frontend boolean.
- *Mid:* traces `teamPresence` ([VisitorHub.OnConnectedAsync](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L29-L34) snapshot + [sweeper broadcast](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L76-L81)); finds Away ≠ Offline: an agent who left a tab open is Away after 5 idle minutes, and `anyOnline` checks `== Online`… so actually investigates the *grace*: a disconnect <60s ago still counts (Compute), and the connect-time snapshot can be stale by one sweep.
- *Senior:* distinguishes "bug" from "policy nobody wrote down" — defines what online *should* mean for visitors, then makes code match the definition, then adds the missing tracker tests.

## D. System design (grounded here)

**D1. "Scale this to 50 support teams, 500 agents, 100k conversations/day."**
Walk the bottleneck order: (1) presence/viewers/round-robin are in-process — Redis or DB-backed presence + SignalR backplane; (2) `agents` group broadcasts every message to *every* agent ([RealtimeNotifier](../../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs#L20-L27)) — needs team-scoped groups; (3) inbox list query + unfiltered reconnect invalidation become storms; (4) only then think DB sharding. Mid = finds 1–2; senior = orders all four by blast radius and gives the cheap mitigations first.

**D2. "Design idempotent message sending for the widget."**
Current state: none — a retried POST duplicates ([SendVisitorMessageHandler](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L91-L100)). Mid: client-generated message id (`Guid`), unique index, upsert-or-return-existing. Senior: scoping the key (per conversation), TTL on dedupe memory, what HTTP status the duplicate returns, and whether the SignalR echo already makes clients tolerant (it does — dedupe by id exists client-side).

## E. Code review questions

**E1. "Review this real method: [`StartWidgetSessionHandler.Handle`](../../../src/NetIntercom.Application/Widget/StartConversation.cs#L49-L94)."**
Expected findings, in order of severity: (1) anyone presenting an `externalId` claims that identity — no HMAC verification → account/history takeover (risk R1); (2) unconditional overwrite of name/email from unauthenticated input; (3) the `&&`/`||` precedence in the visitor lookup works but reads like a bug — parenthesize; (4) nit: token issued even when identity claims rejected. Mid finds 2–3; senior finds the *order* and asks for the identity-verification design before approving anything.

## F. Defend-your-design (pulled from the comparison cards — these dominate mid/senior loops)

**F1. "You chose MediatR over plain services for a 6-controller app. Defend it."** (Card 1.) Strong answer: the structural validation guarantee + host-agnostic commands (Hangfire entry point), concedes the indirection tax, names the switch signal (handlers forwarding to handlers).

**F2. "You store SLA deadlines as rows and poll every minute. Why not schedule a job per timer?"** (Card 4.) Strong answer leads with the kill shot — the delayed job must re-validate state when it fires, so you write the polling guard anyway — then batched breach-storms after downtime as the bonus.

**F3. "Your inbox uses TanStack Query but your widget hand-rolls fetch state. Inconsistency?"** (Card 5.) Strong answer: same trade-off, different constraints (bundle size vs cache richness); proves it's a *decision* not drift; names the widget's switch signal.

**F4. "JWT in localStorage. The security reviewer objects. Respond."** (Card 3.) Strong answer: agrees on the inbox, *disagrees with reasons* on the widget (third-party embed can't use cookies); offers the migration plan with dual-scheme transition and token revocation at cutover.

## G. Behavioral (grounded in plausible contributions here)

- "Tell me about a time you found a security issue during unrelated work." → narrate finding R1/R4 while tracing flows: how you verified before raising, how you wrote it up (impact, repro, fix sketch) without drama.
- "A change you shipped that was smaller than asked." → the loader extraction (ticket G3): why centralizing five include-chains beat building the repository layer someone requested.
- "A disagreement with a reviewer." → use the snooze-doesn't-pause-SLA question: it's a *product* decision wearing a bug costume; describe escalating to a definition, not a code argument.
- "Something you'd do differently." → shipping the presence tracker without `IClock`: small shortcut, compounding test debt.

## Practice protocol

1. Pick 3 questions/day; answer out loud, 3 minutes each, citing file names from memory.
2. Re-derive any anchor you cite — if you can't find it in 30s, you didn't learn it.
3. For every F question, argue the *other* side for 2 minutes first. If your defense doesn't survive your own attack, it isn't a defense.
