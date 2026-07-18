# Review Katas

Each kata is a simulated PR against this repo. Read the diff summary, write your review (**Blocking / Important / Optional** findings) *before* reading the expected findings. Score: did you catch the blocking issue? Did you avoid nitpicking past it?

The diffs are illustrative; the code they resemble is real and anchored.

---

## Kata 1: "Add endpoint to fetch any conversation for the widget"

**Author intent:** let the widget deep-link to a conversation by id.
**Fake diff summary:** new `GET /api/v1/widget/conversations/{id}` → `GetWidgetConversationHandler` that does `_db.Conversations.Include(...).FirstOrDefaultAsync(c => c.Id == id)` and returns `ConversationDto.From(...)`.
**Resembles:** [GetConversationHandler](../../../src/NetIntercom.Application/Conversations/GetConversation.cs#L12-L30) (agent version) but on the widget controller.

**Expected findings:**
- **Blocking:** no `VisitorId` ownership scope — any visitor reads any conversation (IDOR). Must mirror [WidgetQueries ownership check](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L25-L30).
- **Blocking:** `ConversationDto.From` leaks `AssigneeName`, `SourceUrl`, full visitor PII to a visitor — wrong shape; use a trimmed payload like [RealtimeNotifier's visitor view](../../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs#L32-L37).
- **Important:** missing `[Authorize(Policy="Visitor")]`.
- **Optional:** add the cross-tenant integration test.

**Good review comment example:**
> Blocking: this returns any conversation by id with no visitor scoping — that's an IDOR. Copy the `AnyAsync(c.Id == id && c.VisitorId == _currentUser.VisitorId)` guard from `GetVisitorMessagesHandler` and return 403 on miss. Also, `ConversationDto` exposes the assignee name and source URL to the visitor; we should return the trimmed shape we already send over the hub.

---

## Kata 2: "Fix flaky logout by retrying refresh"

**Author intent:** stop random logouts.
**Fake diff summary:** in `api.ts`, wrap the refresh in a `for` loop that retries 3 times on failure before redirecting to `/login`.
**Resembles:** [api.ts#L38-L51](../../../frontend/inbox/src/api.ts#L38-L51).

**Expected findings:**
- **Blocking:** this treats the symptom, not the cause. The real issue is *concurrent* refreshes racing rotation ([Refresh.cs#L43](../../../src/NetIntercom.Application/Auth/Refresh.cs#L43)) — retrying a revoked token 3× still fails 3×. The fix is a single shared in-flight refresh promise.
- **Important:** retry-on-401-refresh can hammer the auth rate limiter (10/min) and lock the user out harder.
- **Optional:** exponential backoff if any retry survives review.

**Good review comment:**
> This will retry a token that rotation already revoked, so it can't succeed — the root cause is that we fire N refreshes for N concurrent 401s. Let's dedupe to one in-flight refresh (store the promise; concurrent callers await it). Happy to pair on it. I'd hold this PR until we fix the race rather than add retries on top.

---

## Kata 3: "Make canned reply shortcuts unique"

**Author intent:** prevent two replies sharing a shortcut.
**Fake diff summary:** change [`HasIndex(r => r.Shortcut)`](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L112) to `.IsUnique()`.
**Resembles:** the CannedReply mapping ([AppDbContext#L108-L114](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L108-L114)).

**Expected findings:**
- **Blocking:** CannedReply is soft-deletable ([query filter](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L113)); a plain unique index counts soft-deleted rows, so re-creating a shortcut after deleting it throws. Needs a *filtered* unique index `WHERE "IsDeleted" = false`.
- **Blocking:** uniqueness scope is undefined — globally unique, or per team / per agent? A personal `/brb` and a team `/brb` are both legitimate. The requirement is missing.
- **Important:** no migration (repo uses EnsureCreated — this won't even apply to an existing DB).
- **Optional:** validation message for the conflict at the API layer (currently a 500 from a DB violation).

**Good review comment:**
> Two blockers: (1) shortcuts are soft-deletable, so a global unique index will reject re-creating a previously deleted shortcut — we need a partial index filtered on `IsDeleted = false`; (2) what's the uniqueness scope? Personal and team replies can legitimately share `/brb`. Can we nail the requirement before the index? Also we'll need a real migration; EnsureCreated won't touch an existing DB.

---

## Kata 4: "Performance: cache the routing rules"

**Author intent:** stop loading all routing rules on every visitor message.
**Fake diff summary:** add a `static List<RoutingRule> _cache` in `ConversationRouter`, populated on first use, used instead of the DB query.
**Resembles:** [ConversationRouter.RouteAsync#L31-L37](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L31-L37).

**Expected findings:**
- **Blocking:** `static` mutable cache in a scoped service = shared across all requests with no invalidation — editing a rule via [SaveRoutingRule](../../../src/NetIntercom.Application/Teams/TeamsAndAdmin.cs#L133-L168) never takes effect until restart. Correctness regression for a micro-optimization.
- **Important:** thread-safety of the static list under concurrent population.
- **Important:** is this even a bottleneck? No measurement cited — premature optimization. Rules are a handful of rows behind an index.
- **Optional:** if caching is justified, use `IMemoryCache` with invalidation on rule save.

**Good review comment:**
> Hold on — do we have a measurement that loading rules is actually hot? It's a tiny indexed table read. A static cache here introduces a real correctness bug (rule edits won't apply until restart) and a thread-safety question, to save microseconds we haven't shown matter. If we do cache, it should be `IMemoryCache` invalidated in `SaveRoutingRuleHandler`. I'd rather not.

---

## Kata 5: "Add internal-note count to the widget conversation"

**Author intent:** show visitors how many notes agents left (?!).
**Fake diff summary:** widget `WidgetConversation` gains `internalNoteCount`, populated by counting `Messages.Where(m => m.IsInternalNote)`.
**Resembles:** the widget DTO boundary ([widget api.ts#L12-L16](../../../frontend/widget/src/api.ts#L12-L16)).

**Expected findings:**
- **Blocking:** this exposes the *existence and volume* of internal notes to visitors — a privacy violation that defeats the entire internal-note design (enforced in three places — see [key flow 3](../01-codebase-cartography/05-key-flows.md)). Reject outright.
- **Important:** the author may have misunderstood the requirement — clarify what they actually need (agent-side count?).
- This kata tests whether you push back on a *requirement*, not just code. Sometimes the right review is "this shouldn't exist."

**Good review comment:**
> I don't think we can ship this — internal notes are deliberately invisible to visitors (it's enforced at the notifier, the query filter, and the widget). Even a count leaks that agents are discussing them privately. Can you say more about the goal? If it's an agent-side metric, that belongs on the inbox/reports side, never the widget payload.

---

**Self-grade across katas:** Basic = caught the blocking issue in 3/5. Solid = caught all blocking issues and worded one comment kindly + actionably. Strong = in katas 2, 4, and 5 you pushed back on the *approach or requirement*, not just the code, and you cited the real anchor the fix should mirror.
