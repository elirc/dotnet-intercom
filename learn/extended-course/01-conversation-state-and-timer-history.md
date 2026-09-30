# 01. Conversation state and timer history

Begin with [Conversation.cs](../../src/NetIntercom.Domain/Entities/Conversation.cs), [Sla.cs](../../src/NetIntercom.Domain/Entities/Sla.cs), and the existing [conversation tests](../../tests/NetIntercom.UnitTests/ConversationTests.cs). This lesson concerns current domain behavior. It does not assume that a historical incident branch is checked out, and it does not claim that the database or HTTP host has been executed.

The central question is not simply whether a conversation is Open, Snoozed, or Closed. It is how that state interacts with first-reply evidence and the history of response timers. A conversation can have several completed timers and one running timer. Looking only at the conversation state loses information needed to explain deadlines, reports, and concurrent processing.

## Separate the state dimensions

Write a worksheet with five columns: conversation state, first agent reply timestamp, snooze deadline, close timestamp, and timer history. Timer history should record kind, status, deadline, and completion timestamp for each timer. Do not compress it into a single current deadline because completed timers remain relevant after another starts.

The constructor defaults the conversation to Open. RecordVisitorMessage reopens a non-open conversation before checking for a policy or running timer. Without a policy, it then returns without starting a timer. With a running timer, it returns without adding another. Otherwise it selects FirstResponse when FirstAgentReplyAtUtc is absent and NextResponse when that timestamp already exists.

RecordAgentReply stamps FirstAgentReplyAtUtc only if it has not been set. It marks every currently running timer met and reopens a snoozed conversation. It does not, by itself, reopen a closed conversation. That distinction is visible in the method and should not be inferred from the broader phrase agent replied. A handler may impose additional rules, but those require a separate source trace.

Close rejects a conversation already Closed. Otherwise it sets Closed, records the close time, clears snooze, and cancels running timers. Snooze rejects Closed and rejects a deadline that is not strictly in the future. It changes state and snooze deadline but does not cancel or pause timers. Reopen clears close and snooze timestamps and sets Open without creating a new timer.

## Worked timeline with independent expected values

Use a synthetic policy with a five-minute first response and a ten-minute next response. Let t0 be a fixed UTC instant. At t0, a visitor message starts one FirstResponse timer due at t0 plus five. The conversation remains Open and has no first reply timestamp.

At t0 plus three, an agent reply marks that timer Met with completion at plus three and sets first reply to plus three. At plus four, another visitor message creates a NextResponse timer due at plus fourteen. At plus six, another visitor message finds a running timer and does not move its deadline. The customer has sent two messages since the last reply, but the response window still starts with the first of them.

At plus seven, snoozing until plus sixty changes state to Snoozed and sets the snooze deadline. The next-response timer is still Running and due at plus fourteen. At plus eight, an agent reply marks it Met and reopens the conversation, clearing the snooze deadline. FirstAgentReplyAtUtc remains plus three because later replies do not overwrite the first response evidence.

The trace has two timers, both Met, and no running timer. No call throws. A learner who predicts timer suspension at snooze has imported a product expectation that is not in this implementation. That expectation may be worth discussing, but the characterization must first describe the actual method.

## What close and reopen preserve

Start another fixture with a running first-response timer. Close at plus two. The timer becomes Cancelled with completion at plus two. Reopen at plus three without a visitor message. The conversation becomes Open, but there is still no new running timer. Reopen changes conversation state; it does not itself signal a new waiting period.

Now record a visitor message at plus four. Because no first agent reply has occurred, the new timer is FirstResponse again. Its deadline is plus nine. The cancelled timer remains in history. Counting all FirstResponse timers as completed customer responses would therefore be wrong: kind and status answer different questions.

If an agent replied before close, the next visitor message after reopen instead creates NextResponse. Reopening does not clear FirstAgentReplyAtUtc. This makes first response a conversation-lifetime fact under the current domain model, not a fact reset by every open-close cycle.

A proposed product rule could define a new conversation for some reopen scenarios or measure each episode separately. That would require explicit episode identity and reporting semantics. Do not alter the meaning of FirstAgentReplyAtUtc in a test merely because a new episode metric sounds useful.

## Rejection must preserve all relevant fields

A second Close call throws before assigning fields. Snooze with a past or equal deadline also throws before state mutation. Assign on Closed throws before changing assignee or team. These orderings support a useful local invariant: rejected commands should preserve the previous state.

Test more than the exception type. Capture state, timestamps, assignee, team, and timer values before the rejected operation and compare afterward. An implementation can throw the correct exception after changing one field. A test that checks only the exception would miss that partial rejection.

The local method's behavior does not establish database rollback. If a handler changes another entity before calling a method that throws, that other change has a separate persistence story. Later lessons will connect domain rejection to unit-of-work boundaries. Here the test should accurately name itself as a domain snapshot test.

Assign has another subtlety: a null agent clears AssigneeId, while a null team leaves TeamId unchanged. The parameters do not share the same null semantics. A proposed API must make that distinction visible or introduce a clearer patch representation. Treating null uniformly because both properties are nullable would misread the actual command.

## Timer methods are local guards

SlaTimer.MarkMet and MarkBreached require Running and throw otherwise. Cancel returns silently when the timer is not Running. Therefore repeated cancellation and repeated completion have different behavior. This can be intentional: close can safely cancel whatever remains running, while two distinct completion claims should not silently agree.

These guards act on the object's current in-memory status. Two database contexts can each load a Running timer and each pass its own guard. The existence of a guard therefore does not prove that only one process can persist a transition. The timer class has no displayed concurrency token; the conversation's RowVersion property is a separate mapped entity concern.

Do not overstate the opposite conclusion either. Reading this class alone does not establish every database behavior or every possible handler transaction. The correct source-level conclusion is that the timer method itself supplies no cross-context arbitration. To claim an end-to-end race outcome, inspect mapping and execute an appropriate competing-context fixture.

## Independent exercises

Exercise NI01-A: extend the worked timeline by closing at plus nine, reopening at plus ten, and receiving a visitor message at plus eleven. Give every timer's kind and status, the new deadline, and the unchanged first-reply timestamp.

Exercise NI01-B: construct a complete before/after snapshot for a rejected Snooze whose deadline equals now. Then compare null agent and null team assignment semantics on an open conversation.

Exercise NI01-C: create two independent Running timer snapshots, mark one Met and the other Breached, and explain exactly what this demonstrates and what real database evidence is still missing.

Write predictions before reading [foundation solutions](solutions-foundation.md). These exercises are reasoning tasks at the domain boundary; no production behavior is changed by the course.
