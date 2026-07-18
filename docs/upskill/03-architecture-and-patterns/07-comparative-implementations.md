# Comparative Implementations

The core senior skill: choosing between viable designs and knowing what each commits you to. Five decision points this repo actually embodies one side of. Card 5 is real-vs-real (both sides exist in this repo). Card 3 carries the **migration drill**.

Every "alternative" code block that isn't anchored is labeled illustrative.

---

## Comparison 1: MediatR CQRS-lite vs controllers calling services directly

**The decision this repo made:** every use case is a MediatR request; controllers are one-line dispatchers — [`ConversationsController.Send`](../../../src/NetIntercom.Api/Controllers/ConversationsController.cs#L50-L52) → [`SendAgentMessageHandler`](../../../src/NetIntercom.Application/Conversations/SendAgentMessage.cs#L23-L73), with validation/logging as [pipeline behaviors](../../../src/NetIntercom.Application/Common/Behaviors.cs#L7-L56).

**The alternative:** a plain `ConversationService` injected into the controller.

```csharp
// Illustrative: not from this repo
public class ConversationsController : ControllerBase
{
    private readonly ConversationService _service;
    [HttpPost("{id:guid}/messages")]
    public async Task<MessageDto> Send(Guid id, SendMessageRequest req)
    {
        // validation via [ApiController] attributes or manual call
        return await _service.SendAgentMessageAsync(id, CurrentAgentId, req.Body, req.IsInternalNote);
    }
}
```

**Side-by-side:**

| Dimension | MediatR slices (this repo) | Direct services |
| --- | --- | --- |
| Complexity / ceremony | Record + handler + registration per use case; indirection when navigating (F12 lands on `Send`, not the handler) | Fewer moving parts; call graph visible to the IDE |
| Testability | Handlers test in isolation; pipeline behaviors test once for all requests | Services test fine too; cross-cutting concerns need base classes or decorators per service |
| Performance characteristics | Reflection-based dispatch + behavior chain per request — microseconds, irrelevant here | Direct calls; trivially faster, equally irrelevant |
| Coupling & blast radius | Slices don't reference each other; deleting a feature = deleting files | Services accrete methods and get shared; deleting a feature means surgery |
| Failure modes unique to it | "Handler not found" at runtime if registration misses an assembly; cross-cutting magic surprises newcomers (where did validation run?) | God-services; duplicated validation; one service's transaction habits leak into another's |
| Migration cost (in THIS repo) | — | Mechanical but wide: ~20 handlers → ~6 services, rewire validation (behaviors have no home → FluentValidation auto-validation or manual), retest |

**What each choice commits you to / who pays:** MediatR commits every new contributor to learning the dispatch convention and the team to keeping slices thin (the pattern can't stop a 400-line handler). Direct services commit the team to manually re-implementing the pipeline guarantees (validation-always-runs, uniform logging) and to policing service growth in review. Juniors pay the MediatR learning tax once; the team pays the service-discipline tax forever.

**Verdict for this repo:** **right call.** The repo's value is its uniformity — every one of ~20 use cases reads identically, and the validation guarantee is structural. At 6 controllers it's near the break-even point, but the SLA/routing logic benefits from being host-agnostic (the Hangfire job sends the same commands — [SlaBreachJob](../../../src/NetIntercom.Infrastructure/BackgroundJobs/SlaBreachJob.cs#L10-L17)).
**Switch signal:** if the app shrank to a handful of CRUD endpoints, or the team kept writing handlers that just forward to other handlers (a sign the slices don't match real use cases).
**Drill:** implement (scratch branch or paper) `GET /api/v1/conversations/{id}` both ways. Count files touched, lines, and what you had to remember manually in the service version.
**Self-grade:** Basic = can state both shapes. Solid = names the validation guarantee as the load-bearing difference. Strong = identifies where this repo would feel pain first if it switched (the background-job entry point).

---

## Comparison 2: Handlers query `IAppDbContext` directly vs a repository layer

**The decision this repo made:** no repositories — handlers compose LINQ against [`IAppDbContext`](../../../src/NetIntercom.Application/Common/Interfaces.cs#L8-L24)'s `DbSet`s directly, e.g. the filter/sort/paginate pipeline in [`ListConversationsHandler`](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L26-L77).

**The alternative:** `IConversationRepository` hiding EF behind intention-named methods.

```csharp
// Illustrative: not from this repo
public interface IConversationRepository
{
    Task<Conversation?> GetWithDetailsAsync(Guid id, CancellationToken ct);
    Task<PaginatedList<Conversation>> SearchAsync(ConversationFilter filter, CancellationToken ct);
    void Add(Conversation conversation);
}
```

**Side-by-side:**

| Dimension | Direct DbContext (this repo) | Repository layer |
| --- | --- | --- |
| Complexity / ceremony | None extra; LINQ at point of use | One interface + implementation per aggregate; a method per query shape |
| Testability | Unit-testing handlers requires faking `DbSet` (painful) — which is why this repo's tests are domain-level + integration-level instead | Handlers mock cleanly; the repository itself still needs integration tests |
| Performance characteristics | Query shape visible and tunable at the call site (`Include`, projection, `AsNoTracking`) | Methods grow flags (`includeMessages: true`) or over-fetch for the worst caller |
| Coupling & blast radius | EF is a hard dependency of Application; changing ORMs = rewrite handlers | ORM swap contained (in theory; in practice IQueryable leaks anyway) |
| Failure modes unique to it | Include-contract drift (Pattern 14): five hand-copied include chains; one missing `.Include(SlaTimers)` = silently wrong DTO | Repository method explosion; "generic repository" abomination; hidden N+1 inside repo methods |
| Migration cost (in THIS repo) | — | Medium: ~15 distinct query shapes to name and move; the test strategy (integration-heavy) wouldn't improve, so you pay cost for unrealized benefit |

**What each choice commits you to / who pays:** direct DbContext commits you to integration tests as the primary safety net (this repo accepted that — see [ApiFactory](../../../tests/NetIntercom.IntegrationTests/ApiFactory.cs#L10-L39)) and to reviewers who can read EF query shapes. Repositories commit you to curating a query vocabulary forever; the person who pays is whoever needs a query that's *almost* an existing method.

**Verdict for this repo:** **right call,** with one caveat — the repeated include-graph (Pattern 14) is exactly the slice of repository value worth having, and the repo only half-built it ([LoadAsync](../../../src/NetIntercom.Application/Conversations/ManageConversation.cs#L58-L65)). Extract the loader, skip the layer.
**Switch signal:** a second persistence target (search index, read replica with different shapes), or unit tests start mattering more than integration tests for delivery speed.
**Drill:** write `IConversationRepository` covering just the five existing conversation loads. Now add ticket M5's keyset pagination to it. Did the interface help or fight you?
**Self-grade:** Basic = recites testability trade-off. Solid = points at the include-drift as this repo's actual cost. Strong = proposes the loader-not-layer middle path unprompted and can say why "swap the ORM" is a fake benefit here.

---

## Comparison 3: JWT + rotated refresh tokens vs cookie session (BFF) — **includes the migration drill**

**The decision this repo made:** stateless 15-min JWTs with role claims + rotated, hashed refresh tokens — [`TokenService.CreateAgentTokens`](../../../src/NetIntercom.Infrastructure/Auth/TokenService.cs#L43-L64), rotation in [`RefreshTokenCommandHandler`](../../../src/NetIntercom.Application/Auth/Refresh.cs#L31-L56), client storage in [`setTokens`](../../../frontend/inbox/src/api.ts#L8-L18) (access → sessionStorage, refresh → **localStorage**), SignalR via query-string token ([Program.cs#L60-L69](../../../src/NetIntercom.Api/Program.cs#L60-L69)).

**The alternative:** HttpOnly SameSite cookie session (classic or BFF).

```csharp
// Illustrative: not from this repo
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o => { o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax;
                      o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = true; });
// + antiforgery for unsafe methods
```

**Side-by-side:**

| Dimension | JWT + refresh (this repo) | Cookie session |
| --- | --- | --- |
| Complexity / ceremony | Token service, rotation handler, client refresh dance ([api.ts#L38-L51](../../../frontend/inbox/src/api.ts#L38-L51)), hub query-string plumbing | Framework does nearly everything; you add antiforgery + CORS-credentials config |
| Testability | Easy: mint a token, set a header — integration tests do exactly this ([HappyPathTests#L49-L50](../../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs#L49-L50)) | Test clients must run the login flow and carry cookie jars |
| Performance characteristics | No session lookup per request; refresh round-trips every 15 min | Cookie validation is in-memory (claims in the ticket) or a store hit if server-side sessions |
| Coupling & blast radius | Any client type (widget! cross-origin embeds) can authenticate the same way | Cookies bind you to browser same-site rules; the third-party-embedded widget **cannot** use them (third-party cookie death) |
| Failure modes unique to it | XSS → 14-day refresh token exfiltrated from localStorage; refresh race logouts; token in query string can hit proxy logs | CSRF (mitigated, never eliminated); session fixation; sticky-session or shared-store needs at scale-out |
| Migration cost (in THIS repo) | — | Medium for the inbox; **impossible for the widget** — visitors are cross-origin third parties, so you'd run a split model |

**What each choice commits you to / who pays:** JWTs commit you to *client-side credential custody* — every XSS becomes a credential-theft incident, and revocation is something you build (this repo only built it for refresh tokens; access tokens and 30-day visitor tokens are irrevocable — [CreateVisitorToken](../../../src/NetIntercom.Infrastructure/Auth/TokenService.cs#L66-L75)). Cookies commit you to CSRF discipline and same-origin topology; the ops team pays at scale-out, the security team sleeps better.

**Verdict for this repo:** **defensible, and forced for the widget** — a third-party embedded client realistically needs bearer tokens. For the *inbox* (first-party, single origin) a cookie/BFF would be strictly safer; the repo chose uniformity over inbox-side safety. The genuinely questionable part is the refresh token in `localStorage` rather than memory + silent re-login.
**Switch signal:** a real security review, multi-tab logout requirements, or an XSS near-miss → move the inbox to cookies (or at minimum move the refresh token out of localStorage).

### Migration drill (required — do this one)

On paper or a scratch branch, write the step-by-step plan to move **the inbox only** to cookie auth while the widget keeps visitor JWTs:

1. Inventory every place agent-auth is assumed: header injection ([api.ts#L25-L34](../../../frontend/inbox/src/api.ts#L25-L34)), refresh dance, sessionStorage bootstrap ([auth.tsx#L14-L17](../../../frontend/inbox/src/auth.tsx#L14-L17)), hub `accessTokenFactory` ([realtime.ts#L8-L16](../../../frontend/inbox/src/realtime.ts#L8-L16)), CORS `AllowCredentials` ([Program.cs#L101-L105](../../../src/NetIntercom.Api/Program.cs#L101-L105)).
2. Server: add cookie scheme alongside JWT; policy "Agent" must accept either during transition (multi-scheme `AddAuthenticationSchemes`). Antiforgery for non-GET.
3. SignalR: cookies flow on the WebSocket handshake automatically for same-origin — delete the query-string path *for agents only*; keep it for `/hubs/visitor`.
4. Tests: which integration tests break? (Every one that sets a Bearer header for an agent.) Write the cookie-login test helper first.
5. Rollout: dual-accept window → switch inbox client → revoke all refresh tokens → remove JWT issuance for agents.
6. Rollback: feature-flag the client auth mode; server keeps dual-accept until the flag is deleted.

**Self-grade:** Basic = lists client storage changes and antiforgery. Solid = catches the SignalR handshake difference and the dual-scheme transition. Strong = catches that the *widget must not change*, the refresh-token revocation step at cutover, and defines the rollback flag precisely.

---

## Comparison 4: DB-polled SLA timers (Hangfire cron) vs scheduled-per-timer delayed jobs

**The decision this repo made:** SLA timers are **rows** ([`SlaTimer`](../../../src/NetIntercom.Domain/Entities/Sla.cs#L12-L63)) with a `BreachAtUtc`; one cron job per minute scans for overdue ones — [`CheckSlaBreachesHandler`](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L31-L62), registered `Cron.Minutely` ([Program.cs#L190](../../../src/NetIntercom.Api/Program.cs#L190)), with a supporting index ([AppDbContext#L95-L99](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L95-L99)).

**The alternative:** schedule one delayed job per timer at creation.

```csharp
// Illustrative: not from this repo
var jobId = BackgroundJob.Schedule<SlaBreachJob>(j => j.BreachOne(timer.Id), timer.BreachAtUtc);
timer.ScheduledJobId = jobId;           // must persist to cancel later
// on agent reply: BackgroundJob.Delete(timer.ScheduledJobId);
```

**Side-by-side:**

| Dimension | Poll-the-table (this repo) | Per-timer delayed jobs |
| --- | --- | --- |
| Complexity / ceremony | One job, one query, one index; timer lifecycle is pure domain state | Job ids stored on timers; *every* state change (met/cancelled) must also cancel a job — two systems to keep consistent |
| Testability | Handler is a normal command — testable with a DB and a fake clock | Needs Hangfire test harness or wrapping the scheduler behind a port |
| Performance characteristics | One indexed scan/minute regardless of timer count; breach detection latency ≤ ~60s | Per-timer precision (seconds); Hangfire queue churn proportional to message volume; mass-cancellations are chatty |
| Coupling & blast radius | Scheduler can be swapped (it only provides "tick") | Domain transitions coupled to scheduler API; a missed cancel = ghost breach |
| Failure modes unique to it | Breach storms after downtime are *batched* (good); per-minute granularity vs the seconds-precision UI countdown (cosmetic lie) | Orphaned jobs firing on already-met timers (must re-check state in the job anyway — so you end up writing the poll-job's guard regardless) |
| Migration cost (in THIS repo) | — | Touch `RecordVisitorMessage`/`RecordAgentReply`/`Close` paths or wrap them; add job-id column + migration; new failure modes for the same user-visible result |

**What each choice commits you to / who pays:** polling commits you to a detection-latency floor and to that one query staying indexed as volume grows — ops pays, predictably and boringly. Per-timer scheduling commits you to distributed-consistency between rows and queue — developers pay, in rare, confusing incidents.

**Verdict for this repo:** **right call.** The kill shot: a delayed job must *re-validate* timer state when it fires (it may have been met since), which means you write the polling handler's logic anyway — the alternative adds the queue without removing the check. Minute-granularity is fine for response-time SLAs.
**Switch signal:** SLA precision requirements under ~10s, or timer volume where the minutely scan + Includes gets heavy (then first fix the Includes, which load four navigations per breached timer just to build a DTO — [CheckSlaBreaches.cs#L34-L40](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L34-L40)).
**Drill:** the API is down 07:00–09:00; 40 timers breach during the outage. Write the exact behavior at 09:00 for this repo's design vs the alternative (batch of 40 marked in one sweep + 40 notifications vs 40 queued jobs replaying). Which do agents prefer? Which does the DB prefer?
**Self-grade:** Basic = states both mechanisms. Solid = articulates the re-validation argument. Strong = adds the downtime-recovery comparison and the "fix the Includes first" scaling order.

---

## Comparison 5: TanStack Query vs hand-rolled fetch state — **real vs real, both in this repo**

**The decision this repo made:** *both*, deliberately split by app. The inbox uses TanStack Query with event-driven invalidation — [`InboxPage`](../../../frontend/inbox/src/pages/Inbox.tsx#L26-L33) (`useQuery` + 30s `refetchInterval`), hub events → [`invalidateQueries`](../../../frontend/inbox/src/pages/Inbox.tsx#L40-L52). The widget hand-rolls `useState` + fetch — [`Widget.tsx` bootstrap effect](../../../frontend/widget/src/Widget.tsx#L24-L73), manual dedupe in [`addMessage`](../../../frontend/widget/src/Widget.tsx#L19-L21).

**The alternative:** each app's approach *is* the other's alternative. No illustrative code needed.

**Side-by-side:**

| Dimension | TanStack Query (inbox) | Hand-rolled (widget) |
| --- | --- | --- |
| Complexity / ceremony | Dependency + mental model (keys, staleness, invalidation); declarative after that | Zero deps; every cache rule written by hand (visible at [Widget.tsx#L52-L58](../../../frontend/widget/src/Widget.tsx#L52-L58) reconnect refetch) |
| Testability | Queries mock at fetch layer; cache behavior is library-tested | All state transitions are yours to test (and currently untested) |
| Performance characteristics | Dedupe, stale-while-revalidate, focus refetch for free | Exactly the requests you wrote, nothing more — matters in an embed that must stay tiny |
| Coupling & blast radius | Invalidation keys are stringly-typed contracts (`["conversations"]`, `["messages", id]`) — a typo'd key = silently stale UI | State lives in one component; growth (multiple conversations, attachments) turns it into a bespoke buggy cache |
| Failure modes unique to it | Over-invalidation → request storms (note `invalidateQueries()` with **no filter** on reconnect, [Inbox.tsx#L73-L76](../../../frontend/inbox/src/pages/Inbox.tsx#L73-L76) — refetches *everything*) | Stale closures, missed dedupe, no retry/backoff — the `// eslint-disable-next-line react-hooks/exhaustive-deps` at [Widget.tsx#L72](../../../frontend/widget/src/Widget.tsx#L72) is the warning label |
| Migration cost | — | Widget → TanStack: +~11KB gzip to a bundle whose whole point is being a single small IIFE ([vite.config.ts](../../../frontend/widget/vite.config.ts#L6-L18)) |

**What each choice commits you to / who pays:** TanStack commits you to modeling *all* server state as keyed queries and policing key discipline in review — the next feature dev pays a learning tax, then profits. Hand-rolling commits you to owning cache correctness forever — fine while the widget has one conversation and four event types, a trap beyond that.

**Verdict for this repo:** **right call on both sides, for different reasons** — bundle size is a real constraint for an embeddable script; cache richness is a real need for a three-pane inbox. This split is worth internalizing: *the same team, same week, correctly chose opposite sides of the same trade-off because the constraints differed.*
**Switch signal:** widget grows offline support, multiple conversations, or attachments → adopt a (lighter) query lib or extract the inbox's patterns. Inbox: if the unfiltered reconnect invalidation causes visible storms at scale, tighten keys before reaching for anything bigger.
**Drill:** find every query key in the inbox (`grep -n "queryKey" frontend/inbox/src`). Draw the invalidation graph: which hub event invalidates which keys. Then answer: which key is invalidated by an event that can never change its data? (Check `presenceChanged` → `["agents"]` against what the agents query actually returns.)
**Self-grade:** Basic = can describe both implementations. Solid = explains *why* the constraints differ (bundle vs cache richness). Strong = critiques the unfiltered invalidation and proposes the precise narrower keys, plus when the widget's approach stops scaling.
