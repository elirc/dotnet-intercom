# M5: "Cache the routing rules". Decide whether to, then do it without serving stale routing

**Rung:** mid · **Est.:** 4–6 h · **Skills:** caching correctness, invalidation across replicas, measurement before optimization, EF tracking
*(Added in B08 to fill the "caching is thin" gap from the B01 report. Concept page: [06 caching](../../../opusorganize/apprenticeship/curriculum/06-caching-correctness.md).)*

## Context
Every new conversation runs `ConversationRouter.RouteAsync` (`src/NetIntercom.Application/Routing/ConversationRouter.cs:29-73`). It loads **all** enabled routing rules with their `Team` (`:31-35`), then the fallback team (`:50-51`), then the team's members (`:77-79`). All of this happens inside the visitor's `POST /widget/messages` request (`SendVisitorMessage.cs:83`). Admins change rules through `SaveRoutingRuleHandler` (`src/NetIntercom.Application/Teams/TeamsAndAdmin.cs:133-169`). Office hours live as JSON on `Team.OfficeHoursJson` and are parsed on every call (`:42`). `ConversationRouter` is registered `Scoped` (`Application/DependencyInjection.cs:17`), and S1 plans for 3 API replicas.

## Ticket
> Marketing is putting the widget on 40 landing pages next month. A teammate's PR description says: "Routing hits the DB 3× per new conversation. Wrap the rules in `IMemoryCache` for 10 minutes, easy win." Review it, decide, and ship the right thing.

## Constraints
- After an admin saves a rule, **every replica** routes by the new rule within a bound you choose and write down.
- No new infrastructure unless you justify it (Redis isn't in `docker-compose.yml`).
- Presence and round-robin state (`TeamMembers.LastAssignedAtUtc`) must **not** be cached. Explain why.

## Definition of done
- **Measure first.** Estimate or measure the cost of the rules query at realistic sizes (how many rules does a support team have?) and compare it with the rest of the request. Write down whether caching is worth it. "No, and here's why" is an acceptable outcome if you back it with numbers.
- If you do cache:
  - Name the source of truth, the key, the staleness bound and the invalidation owner.
  - Show a test proving "save rule → the next route uses the new rule" on one instance.
  - Write down what happens on the other two replicas.
- The cached object must be safe to share across requests: not an EF-tracked entity attached to a disposed `DbContext`.

## Explain before touching
1. Which of [06](../../../opusorganize/apprenticeship/curriculum/06-caching-correctness.md)'s four failure modes does the 10-minute `IMemoryCache` PR risk? Name the specific scenario for each one that applies.
2. The rules are loaded with `.Include(r => r.Team)`, and the team's `OfficeHoursJson` drives routing. If you cache rules, what else did you just cache without meaning to?
3. `IMemoryCache` is per process. With 3 replicas and invalidation in `SaveRoutingRuleHandler`, which replicas see the change and when?
4. Give two ways to make invalidation work across replicas, and the operational cost of each.
5. The inbox already has a caching layer: React Query with `staleTime: 10_000` (`frontend/inbox/src/main.tsx:10`), invalidated on every hub event (`frontend/inbox/src/pages/Inbox.tsx:40-48`). Is that cache "lying" in any of the four ways, or is its problem something else?

<details><summary>Hints</summary>

- A routing table of 10–50 rows on an indexed or small table is typically sub-millisecond. Compare it with the rest of the request: the visitor lookup, the insert, `SaveChangesAsync`, two SignalR sends.
- `AsNoTracking()` plus a small immutable snapshot record is safer to cache than entities.
- A version number stored in the DB and checked per request is a cheap cross-replica invalidation.
</details>

Sealed answer: [`../_answers/ladder-M5.md`](../_answers/ladder-M5.md)
