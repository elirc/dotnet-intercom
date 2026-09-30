# Foundation solutions

Attempt the six opening exercises before consulting this guide. These answers describe current source and proposed test boundaries. No HTTP, SignalR, or database execution is claimed.

## NI01-A: history after another cycle

At plus nine, closing changes state to Closed and records ClosedAtUtc. Both previous timers are already Met, so cancellation changes neither. At plus ten, Reopen clears ClosedAtUtc and SnoozedUntilUtc, sets Open, and adds no timer. At plus eleven, the visitor message starts a third timer because none is Running.

That third timer is NextResponse and is due at plus twenty-one under the ten-minute policy. FirstAgentReplyAtUtc remains plus three. The final history is one Met FirstResponse, one Met NextResponse, and one Running NextResponse. The cancelled-timer count is zero because no timer was running when this particular close occurred.

A common wrong answer invents a cancelled timer simply because Close was called. Another resets first response on reopen. Both errors come from reasoning from method names rather than filtering conditions. Verify the exact prior statuses before applying the next operation.

## NI01-B: rejection and null semantics

For equal snooze deadline and now, the less-than-or-equal guard throws before changing state or SnoozedUntilUtc. Preserve state, close time, snooze time, first reply, assignee, team, and every timer field in the before snapshot. Comparing all fields detects partial mutation that an exception-only assertion misses.

On an open conversation, Assign with null agent clears AssigneeId. Null team leaves TeamId unchanged because assignment occurs only when that argument is nonnull. A nullable parameter therefore does not have one universal patch meaning. A proposed endpoint may need an explicit clear-team operation if the product requires one.

The domain fixture should not claim database rollback. It proves that this object remained unchanged during rejection. A handler could have modified other tracked objects before invoking the method, and the persistence behavior of those changes requires its own test.

## NI01-C: two snapshots

Each independent timer starts Running. MarkMet can succeed on one and MarkBreached can succeed on the other because each checks only its own status. This establishes that the local methods do not share an arbitration mechanism across object instances.

It does not prove both changes will commit in the actual database. Mapping, concurrency tokens, transaction isolation, and handler behavior can affect persistence. Inspect the timer mapping and run two real contexts with controlled read and save barriers to establish the accepted result.

The test should inspect notification effects as well as stored status. A losing write followed by an inappropriate notification can still violate the workflow contract. Keep local domain validation, database arbitration, and external fan-out as separately asserted boundaries.

## NI02-A: new identity versus reopening

With a supplied Closed conversation and StartNew false, the owned conversation remains selected. RecordVisitorMessage reopens it and may start a timer according to policy and first-reply history. The new message references that original conversation.

With StartNew true, the handler sets its local conversation reference to null after loading and checking ownership. It constructs and routes a new conversation, loads its policy if selected, then adds the message to that new identity. The old closed conversation is not reopened by the subsequent call because it is no longer the selected object.

Both paths prepare message, summary, visitor time, and domain changes before SaveChangesAsync. DTO creation and two notifier calls follow a successful save. A precise fixture asserts original and new conversation identities, not merely that one message exists somewhere after the operation.

## NI02-B: failure categories

Before save, no successful persistence call has yet been observed, although the context can contain modified tracked objects. After save but before notification, persistence may already be complete while live delivery has not begun. After first notification but before the second, one fan-out call may have succeeded while the other did not.

The selected source does not provide a durable client-intent replay lookup. Therefore a client that receives an error cannot assume a fresh retry creates exactly one message. Determine durable state through a controlled adapter read and an explicit recovery contract before recommending automatic new submission.

A mock that throws before save and another that throws after save answer different questions. Record the injection point in each result. The useful conclusion is not simply send can fail, but which accepted effects and observations are possible at that point.

## NI02-C: timestamp ties and read scope

Let M1 and M2 both have timestamp T. If the client receives only M1 and later requests after T, the strict greater-than predicate excludes both M1 and M2. This is a source-derived counterexample to treating timestamp alone as a lossless cursor under ties.

A proposed cursor can include timestamp and a stable tie-breaking identifier, with an ordering and continuation predicate that agree. It also needs a page envelope and client merge policy. Adding a limit without continuation does not solve catch-up and can introduce new loss.

The current read command marks all unread public agent messages in the conversation, not only explicitly displayed identities. A proposed read-through command could carry a stable message cursor and define which messages qualify. Human attention remains outside the software guarantee; the system records a client acknowledgement under its chosen contract.

## NI03-A: characterization and proposed change

Snooze retaining a running timer, an overnight interval returning false, and an empty placeholder value leaving its token intact are suitable examples. A proposed timer-pause feature would need deadline accounting and resume semantics, not merely deleting the current assertion. Overnight support would need both Friday-late and Saturday-early cases. Empty-value substitution needs an explicit product rule about whether blank output or visible unresolved tokens are preferable.

The existing expectations remain accurate characterizations until the implementation changes. Keep the adversarial inputs when converting them into regression tests; otherwise the repaired behavior can regress unnoticed.

## NI03-B: semantic mutation evidence

A mutant that lets Cancel overwrite a breached timer's completion timestamp should fail the status-and-completion preservation assertion. A mutant changing office-hours end exclusion to inclusion should fail the exact-end assertion. Both must compile and reach the selected methods before their failure is meaningful evidence about the oracle.

The baseline runner does not execute these mutants. Record them as proposed until a disposable experiment produces the expected failure. A syntax error or missing source file only tests the harness's ability to report failure, not its sensitivity to the business behavior.

## NI03-C: evidence map

The domain runner establishes selected Conversation and SlaTimer transitions and local rejection behavior. The send handler still needs a service fixture for ordering, captured time, and dependency calls. Persistence requires a real adapter fixture for committed state and concurrency. Notification audience and payload need a hub or notifier fixture. Widget draft, reconnect, and rendering behavior need a component or browser fixture.

A useful next step is not necessarily a full end-to-end test. Choose the smallest boundary that answers the unresolved question, such as a recorded notifier verifying that internal notes never target visitor groups. Keep the claim no broader than the executed path.
