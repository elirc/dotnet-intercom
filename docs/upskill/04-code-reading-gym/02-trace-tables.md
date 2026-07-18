# Trace Tables

Hand-execute the code. Fill the blank table yourself *before* reading the worked answer. The discipline — tracking the *shape* of data and the *risk* at each step — is the transferable skill.

Columns: Step · File/line · Value/state after this step · Owner (which layer) · Transformation · Risk.

---

## Trace A: UI → API — agent sends a reply

**Scenario:** Sara types "On it!" in a conversation and hits Enter.

Blank it out and fill from [Inbox.tsx#L209-L218](../../../frontend/inbox/src/pages/Inbox.tsx#L209-L218) → [ConversationsController#L50-L52](../../../src/NetIntercom.Api/Controllers/ConversationsController.cs#L50-L52) → [SendAgentMessageHandler](../../../src/NetIntercom.Application/Conversations/SendAgentMessage.cs#L36-L73).

Worked answer:

| Step | File/line | State after | Owner | Transformation | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | Inbox.tsx#L209 | `send.mutate()` fires | client | reads `body`, `isNote` from component state | empty body guarded at button, not here |
| 2 | Inbox.tsx#L211 | POST `/conversations/{id}/messages` `{body, isInternalNote}` | client | closure over `conversation.id` | stale `conversation` if not remounted (it is, via `key`) |
| 3 | ConversationsController#L52 | `SendAgentMessageCommand(id, agentId, body, isNote)` | api | agentId from `_currentUser` (claim) | agent id never from body — good |
| 4 | Behaviors.cs#L14-31 | validated | application | body ≤ 10k else throw | — |
| 5 | SendAgentMessage#L38-44 | `Conversation` loaded w/ 4 includes | application | id-only load, **no ownership scope** | R2 — any agent, any conversation |
| 6 | SendAgentMessage#L49-58 | `Message` added to context | application | new entity, `SenderType.Agent` | not yet saved |
| 7 | SendAgentMessage#L60-65 | timers Met, preview updated (public reply) | domain | `RecordAgentReply(now)` | note path skips this entirely |
| 8 | SendAgentMessage#L67 | committed | infra | one transaction | concurrency token may conflict (R13) |
| 9 | SendAgentMessage#L69-71 | DTO built, 2 hub sends | api | `MessageDto.From(message, agent.DisplayName)` | notify-after-commit (R6) |

**Checkpoint:** at which step does the SLA timer change state, and what would change if `isInternalNote` were true? (Step 7; with a note, steps 7 is skipped — no timer touch, no preview, and step 9's visitor send is suppressed in [RealtimeNotifier#L23-L26](../../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs#L23-L26).)

---

## Trace B: Persistence — what SQL does the inbox list emit?

**Scenario:** the inbox loads "Open, mine, sorted by lastMessage."

Trace [ListConversationsHandler.Handle](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L26-L77) and predict the single SQL statement's *shape*.

| Step | File/line | Query state (still deferred!) | Transformation | Risk |
| --- | --- | --- | --- | --- |
| 1 | #L28-33 | base query + 4 Includes | `IQueryable<Conversation>` | collection Include (`SlaTimers`) → row multiplication |
| 2 | #L35-38 | `+ WHERE State = @state` | filter | uses `(State, LastMessageAtUtc)` index |
| 3 | #L45-48 | `+ WHERE AssigneeId = @id` | filter | uses `AssigneeId` index (or composite) |
| 4 | #L64-72 | `+ ORDER BY LastMessageAtUtc DESC` | sort | index-aligned |
| 5 | PaginatedList#L25-26 | **executes**: `COUNT(*)` then `OFFSET/LIMIT` | terminal | two round-trips; offset drift under inserts |

**Checkpoint:** how many SQL queries run total? (Two: the count and the page. Plus the Includes are part of the page query — one statement with joins, not N+1.) Now switch the sort to `slaBreach` ([#L67-70](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L67-L70)) — what's the new risk? (A correlated `Min(BreachAtUtc)` subquery per row, no supporting index.)

---

## Trace C: Auth boundary — visitor token rejected on an agent endpoint

**Scenario:** a holder of a visitor token calls `GET /api/v1/conversations`.

| Step | File/line | State | Owner | Risk |
| --- | --- | --- | --- | --- |
| 1 | Program.cs#L178 | authenticated: token valid, `role=visitor` | api | token is genuine, just wrong role |
| 2 | Program.cs#L179 + ConversationsController#L15 | authorization: policy "Agent" requires `role=agent` | api | visitor lacks it |
| 3 | — | **403 Forbidden** (authenticated, not authorized) | api | not 401 — distinction matters |
| 4 | HappyPathTests#L116 | test asserts exactly this | tests | locked-in behavior |

**Checkpoint:** why 403 and not 401? (401 = "who are you?"; here we know — a valid visitor. 403 = "you, specifically, may not." Getting this distinction right is a common interview probe.) Contrast the anonymous case ([HappyPathTests#L110](../../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs#L110)) → 401.

---

## Trace D: Error path — visitor sends an empty message body

| Step | File/line | State | Owner | Risk |
| --- | --- | --- | --- | --- |
| 1 | WidgetController#L41-46 | `SendVisitorMessageCommand(..., body: "")` | api | bound from body |
| 2 | Behaviors.cs#L17-27 | validator runs; `body NotEmpty` fails → `failures` non-empty | application | aggregates all validator errors |
| 3 | Behaviors.cs#L26 | `throw new ValidationException(failures)` | application | handler never runs |
| 4 | ApiExceptionHandler#L20-27 | mapped to `ValidationProblemDetails`, 400, field dict | api | grouped by property name |
| 5 | ApiExceptionHandler#L42 | `correlationId` attached to the problem | api | from middleware |
| 6 | api.ts#L53-56 (client) | throws `Error(problem.detail ?? title)` | client | surfaced in `send.isError` |

**Checkpoint:** which layers did the request touch and which did it *not* reach? (Reached: api binding, application validation behavior, exception handler. Did **not** reach: the handler, the domain, the DB.) That's the value of edge validation — failures cost nothing downstream.

---

## Trace E: Background — a timer breaches during a snooze

**Scenario:** a conversation with a Running FirstResponse timer is snoozed; the clock passes `BreachAtUtc`; the minutely job runs.

| Step | File/line | State | Owner | Risk |
| --- | --- | --- | --- | --- |
| 1 | Conversation.Snooze#L63-77 | `State = Snoozed`; **timers untouched** | domain | only `Close` cancels timers |
| 2 | (time passes) | timer still Running, now past due | — | invisible in Open inbox filter |
| 3 | CheckSlaBreaches#L34-40 | job loads it — filter ignores `State` | application | snoozed conversation included |
| 4 | Sla.cs#L42-51 | `MarkBreached` (was Running) | domain | succeeds |
| 5 | CheckSlaBreaches#L57 | `slaBreached` pushed to all agents | api | escalation for a hidden conversation |

**Checkpoint:** is this a bug? (Trace says the code does exactly this — confirmed. Whether it's *wrong* is a product decision nobody wrote down: risk R9. A trace tells you *what*; it can't tell you *intended*.) This is the difference between "I read the code" and "I know the requirement."
