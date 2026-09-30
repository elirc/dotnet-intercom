# 11. SLA sweep overlap and notification intent

Read [CheckSlaBreaches.cs](../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs), [Sla.cs](../../src/NetIntercom.Domain/Entities/Sla.cs), [AppDbContext.cs](../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs), and [RealtimeNotifier.cs](../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs). This lesson separates finding overdue work, accepting a transition, and delivering its notification.

The current domain runner proves that one timer object rejects MarkBreached after leaving Running. It does not prove that overlapping sweeps in separate contexts cannot both act on snapshots that were Running when read. The handler and mapping are needed to understand that larger boundary.

## One sweep's sequence

The handler captures now once. It queries timers with Running status and BreachAtUtc less than or equal to now, including conversation, visitor, team, assignee, and timer relationships needed for DTO construction. It materializes the list before iterating.

For every selected timer, it calls MarkBreached with the captured time and writes a warning log. If the list is nonempty, it saves once and then sends one SLA notification for each selected timer. It returns the selected count.

The warning occurs before SaveChangesAsync. A log saying SLA breach is therefore not independently proof that persistence succeeded. If saving fails, the warning may already exist. A proposed operational ledger should distinguish attempted transition from committed transition rather than count every warning as an accepted breach.

The handler sends notifications only after a successful save call. That ordering avoids notifying from this path before the save returns, but it leaves a crash window after persistence and before delivery. A later sweep queries Running timers, so an already Breached timer is not naturally selected again for that missing notification.

## Exact deadline behavior

The query includes a timer at exactly now because it uses less-than-or-equal. The domain MarkBreached method itself does not compare deadline with time; it checks status only. Calling it directly before the deadline can change the object to Breached.

This is another division of responsibility. Eligibility by time belongs to the selected sweep query, while local transition legality belongs to the entity. A pure entity test should not claim the entity enforces the deadline unless the implementation is changed to do so.

A fixture should include one timer before, one exactly at, and one after the captured time. It should also include a Met timer whose deadline is old. The latter must be excluded by status, preventing a test from confusing overdue time with eligible state.

Capturing now once makes a sweep internally consistent even when query and save take time. A timer becoming overdue while the query is running can wait until a later sweep under this design. That is a scheduling-latency property, not necessarily a missed transition.

## Two sweeps from one snapshot

Let contexts A and B both load timer T as Running and overdue. Each materialized object passes MarkBreached locally. Without a storage-level comparison on timer state, both may attempt to save and notify. The shown SlaTimer mapping has an index on status and deadline but no displayed concurrency token analogous to Conversation.RowVersion.

An index accelerates selection; it does not necessarily arbitrate competing updates. Conversation's token protects conversation updates under its mapping, but changing a timer does not automatically become a guarded conversation update merely because the relationship was included.

The exact real outcome requires provider and context execution. State the source finding narrowly: the selected timer entity and mapping do not show an explicit timer concurrency token or conditional update in this handler. The proposed test should verify accepted transitions and notifications across two actual contexts.

Run the schedule with barriers after both queries and before saves. Assert the intended winner count, final timer status, completion timestamp, and accepted notification intent count. A test that calls the sweep twice sequentially only proves the second observes the already changed status; it does not exercise the overlapping-read window.

## Reply versus sweep

A public agent reply loads running timers and marks them Met. A sweep loads overdue running timers and marks them Breached. If both work from separate snapshots, the two operations compete over the same timer outcome.

The product must define the policy at the deadline. Does response time at the same instant satisfy the SLA? Does the order of accepted database transitions decide? Should a late reply preserve Breached while recording response completion separately? The current methods expose states, but a robust concurrent workflow needs a clear agreed rule.

A proposed conditional transition can update only when status remains Running and return the affected identity or count. The losing operation must then decide how to proceed without inventing success. A reply can still be stored even when its timer is already breached, under a deliberate business contract.

Do not automatically retry the entire agent-message command after a timer conflict unless message idempotency is defined. Retrying can create another message. Conflict recovery must distinguish the message effect from the timer effect and preserve the user's original intent.

## Notification intent versus delivery

If the timer transition and a durable notification-intent record commit together, a separate worker can retry delivery after a crash. This is a proposed outbox design, not present in the selected handler. It changes the recovery model from rediscover overdue timers to process accepted undelivered intents.

Give the intent a stable identity derived from the accepted timer transition or event, not a fresh identifier every retry. Record attempt state separately from accepted business state. A provider or SignalR send can succeed while local completion recording fails, so duplicate delivery or unknown outcome must be handled explicitly.

For the current realtime channel, a reconnecting client can reconcile durable state through queries, but that is not the same as guaranteed receipt of every SLA event. For a future external pager, losing or duplicating a notification may have stronger consequences. The design should state the channel's guarantee rather than promise exactly once without support.

An outbox row does not itself ensure authorized payload content. The notifier must still target the intended audience and limit fields. Reliability and confidentiality are separate acceptance criteria even when they share one event record.

## Operational recovery worksheet

Create rows for failure before query, after materialization, after local MarkBreached, during save, after save before first notification, and midway through notification fan-out. For each, record known persisted state, possible emitted warnings, possible deliveries, and safe next action.

At midway fan-out, the selected timer list can contain several conversations and kinds. Retrying the whole handler may select none because all were already saved as Breached. Therefore retrying a job invocation is not equivalent to replaying its missing notifications. This is the concrete recovery gap the proposed intent ledger addresses.

An operator should see enough evidence to reconcile without reading private message bodies. Timer identity, kind, deadline, accepted transition time, and delivery category are usually more relevant than conversation content. Keep synthetic data in the lab and use bounded diagnostic fields in the proposal.

## Independent exercises

Exercise NI11-A: classify exact-deadline eligibility and distinguish query guard from entity guard. Explain why a pre-save warning is not a commit receipt.

Exercise NI11-B: design two-sweep and reply-versus-sweep barrier schedules with complete state and notification oracles. State the desired deadline policy explicitly.

Exercise NI11-C: complete the crash worksheet and propose one atomic timer-transition plus notification-intent contract. Identify which guarantee still depends on the delivery channel.

Review [concurrency workflow solutions](solutions-concurrency-workflows.md) after completing the schedules.
