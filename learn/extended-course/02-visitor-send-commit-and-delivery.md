# 02. Visitor send, persistence, and delivery evidence

Read [SendVisitorMessage.cs](../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs), [RealtimeNotifier.cs](../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs), and [WidgetQueries.cs](../../src/NetIntercom.Application/Widget/WidgetQueries.cs). This lesson follows one accepted visitor message across persistence and delivery. The trace is current-source reasoning, not an executed HTTP or SignalR test.

A user sees one Send button, but the handler performs several operations with different failure consequences. It finds the visitor, optionally finds a conversation, possibly routes a new conversation, creates a message, updates domain state and summaries, saves, sends notifications, and returns a result. The boundary between saving and notifying determines what a failed response can mean.

## Identify the command before tracing it

The command carries VisitorId, optional ConversationId, Body, SourceUrl, and StartNew. The validator requires nonempty body, a maximum of ten thousand characters, and a nonempty visitor identifier. Validation does not by itself prove that the visitor exists or owns a supplied conversation; those checks happen in the handler's queries.

The handler first finds the visitor or throws NotFound. If a conversation identifier is supplied, it queries by both that identifier and the found visitor's identity. An inaccessible or missing conversation therefore follows the same selected NotFound path. This is a different exception category from the transcript query's explicit Forbidden result after a failed ownership check.

When the supplied conversation is Closed and StartNew is true, the handler discards that loaded conversation reference for this operation and creates a new one. When StartNew is false, the existing closed conversation remains selected and RecordVisitorMessage later reopens it. A boolean changes identity behavior, not merely presentation. Tests should assert whether the original conversation is preserved and which conversation owns the new message.

## Constructing the new conversation

A new conversation receives visitor identity and navigation, source URL, a captured timestamp, and a subject truncated from the body. It is added to the context before routing. The router can assign team, agent, or policy according to its own logic, which is a separate boundary to inspect rather than infer from the method name.

If routing sets a policy identifier, the handler loads that policy into the conversation before recording the visitor message. This navigation value matters because RecordVisitorMessage checks SlaPolicy itself. A policy identifier without the loaded policy object would not satisfy that method's current condition.

The handler captures now once and uses it for message creation, domain timer behavior, conversation last-message time, and visitor last-seen time. This makes the accepted operation's timestamps coherent in the selected path. It does not mean every related system uses that same instant; routing and notification have their own dependencies and behavior.

The new message records the conversation reference and identifier, visitor sender type, visitor identity, body, and captured time. The conversation's preview is a separate truncated representation. A test should compare the full message body and preview independently rather than assume truncation applies to both.

## The save boundary

SaveChangesAsync occurs after the message and related tracked changes are prepared. Only after it returns does the handler create DTOs and invoke MessageCreatedAsync followed by ConversationUpdatedAsync. It then returns the tuple. There is no durable notification-intent record visible in this handler.

Consider a failure before SaveChangesAsync. Depending on the failure point, tracked objects may have changed in the current context, but the selected call has not yet established a successful save. A real integration test is needed to determine the persistence outcome and transaction behavior. Do not turn a tracked in-memory object into proof of a committed database record.

Consider a successful save followed by failure in the first notification. The stored message may already exist, while the handler has not returned its normal result. Consider a successful message notification followed by failure in conversation notification: one delivery attempt may have occurred and the other not. A single failed request cannot distinguish these cases without additional evidence.

The source therefore supports a precise design question: how should a client recover an unknown send outcome without duplicating a message? It does not support claiming that the current handler has durable idempotency merely because the database generates identifiers. The command shown here contains no client message intent identifier or replay lookup.

## Notification fan-out order

MessageCreatedAsync first sends the complete message DTO to the agents group. It sends to the visitor conversation group only when IsInternalNote is false. This guard is a current confidentiality boundary in the notifier. Removing it in a hypothetical mutant should be detected by a negative delivery test with an internal note.

ConversationUpdatedAsync sends the complete conversation DTO to agents but sends a smaller anonymous projection to visitors: identifier, state, and assignee name. A test should assert the actual visitor payload shape. Checking only that a notification happened can miss a sensitive property added to that projection.

SlaBreachedAsync targets agents, while MessagesReadAsync sends its receipt payload to agents and the visitor conversation group. These methods have different audiences and content. A universal mock expectation that every event broadcasts to every group would encode the wrong contract.

The await order describes invocation sequencing, not guaranteed human receipt. A successful SendAsync call does not prove a disconnected visitor saw the event, retained it, or rendered it. The application has a transcript query for recovery, which is why persistence and live delivery should remain distinct concepts throughout the course.

## Transcript recovery and its boundary

GetVisitorMessages first checks that the conversation belongs to the visitor. It then filters messages by conversation, excludes internal notes, and applies a strict CreatedAtUtc greater-than condition when AfterUtc is supplied. Results are ordered by creation time and projected to message DTOs.

The strict timestamp boundary deserves a deliberate tie fixture. If two messages share one timestamp and the client remembers that timestamp after receiving only one, a later query with after equal to that value excludes both. This is a source-derived concern, not an executed browser incident. A proposed stable cursor could include timestamp and message identity, but its exact ordering and comparison must be implemented consistently.

No page-size bound appears in this selected query. A full transcript can therefore be a different performance concern from reconnect correctness. Adding a limit alone without a continuation contract risks silently losing history. Design the page envelope, stable ordering, and catch-up loop together rather than treating Take as a complete pagination feature.

## Read receipts do not mean every message was displayed

MarkReadByVisitor checks ownership, then selects unread public agent messages. It uses one captured time, changes their visitor-read timestamps, saves, and notifies. If there are no qualifying messages it returns without save or notification. Repeating the command therefore has no new effect for messages already marked read under this path.

The handler's scope is all qualifying messages in the conversation, not an explicit list of message identities the client reports as visible. A product promise such as read exactly through this displayed message would require a more specific command contract. Do not infer that guarantee from the name read receipt.

The exercise should distinguish server acceptance of a read command from actual human reading. Software can record a client signal; it cannot prove attention. Precise wording makes reports more honest and prevents a timestamp from acquiring a stronger meaning than the interaction supports.

## Independent exercises

Exercise NI02-A: draw the send sequence for a closed conversation with StartNew false and then true. Identify the conversation receiving the new message and the point at which persistence is attempted.

Exercise NI02-B: create a three-row failure matrix for before save, after save before first notification, and after first notification before second notification. State what is known, unknown, and unsafe to assume about retry.

Exercise NI02-C: use two messages with equal CreatedAtUtc to predict strict timestamp catch-up. Propose a cursor contract and a read-receipt contract without claiming either is implemented.

Compare with [foundation solutions](solutions-foundation.md) after writing the trace. Later labs will establish selected executable boundaries separately from this source inspection.
