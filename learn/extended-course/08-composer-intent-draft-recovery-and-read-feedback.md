# 08. Composer intent, draft recovery, and read feedback

Read [Widget.tsx](../../frontend/widget/src/Widget.tsx), [widget api.ts](../../frontend/widget/src/api.ts), and the [visitor send handler](../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs). This chapter studies the user-visible lifetime of a message intent. A successful HTTP result, a persisted message, a cleared textarea, and a live echo are different events.

The selected component clears body before awaiting sendMessage. It appends the returned message through identity deduplication and joins a newly returned conversation. There is no explicit pending-send state or catch inside this send function. These facts support concrete recovery exercises without claiming that a browser failure was executed.

## Capture the draft and the operation

send trims body into text and returns for empty text. It then clears the textarea and submits text with the current conversation identity or null. The trimmed captured value belongs to the operation even if the user immediately begins another draft.

Suppose draft A is cleared and sent, then the user types draft B while A remains pending. If A fails, restoring A unconditionally into the field would overwrite B. A proposed recovery design therefore needs submitted draft identity and current draft revision, not merely a catch that calls setBody(text).

Preserve A as a failed or unknown operation with an explicit retry or restore action. Clear the current field only when it still represents the accepted submitted revision. If the user moved on, show A's outcome separately. This pattern preserves intent without freezing the entire composer during network delay.

The current source does not install this behavior. A proposed component fixture should submit A, type B, reject A, and assert that B remains intact while A is recoverable. Checking only an error banner would miss the important state preservation.

## Two first sends can have different conversation consequences

Before the first result updates conversation state, two send invocations can each capture null conversation identity if the user provides separate text quickly enough. Each server command can then follow the new-conversation path. The selected handler has no displayed client message intent key or lookup that joins those concurrent first sends into one operation.

This is a source-derived interleaving to test, not an observed production incident. The component's button disables for empty body or non-ready bootstrap status, not for an in-flight send. Clearing the field prevents an immediate identical click with no new text, but it does not prevent a new typed message while the first request remains pending.

A proposed design can serialize the first-conversation creation, retain a local pending conversation identity, or use a server-supported conversation-start intent. Each option needs failure semantics. Simply disabling all sends may be acceptable for a starter UI, but the acceptance test should prove the control remains understandable and drafts are retained.

Do not treat a UUID added only in the browser as durable idempotency. The server must define how it scopes, records, and replays that identity. Without that contract, a retry header or payload field is merely data.

## HTTP acceptance and response shape

WidgetApi checks response.ok and throws a generic status-bearing Error on failure. For successful POST with status 204, it returns undefined; otherwise it parses JSON. The generic type parameter is a compile-time promise and does not validate the returned body at runtime.

A malformed successful send response can therefore reach component code expecting conversation and message properties. A proposed transport boundary should validate the minimal success shape before changing visible state. Distinguish HTTP rejection from malformed success and network failure, because they imply different recovery and diagnostic information.

Network failure after the server saved is an unknown mutation outcome. It is unsafe to label every rejected fetch as definitely unsent. The application can add a stable intent lookup or reconciliation flow, but until then the UI should preserve uncertainty and avoid silently creating a fresh business intent on each retry.

A fake-fetch lab can establish request construction and error classification. It cannot prove server persistence or React draft ownership. Keep those tests separate so a green helper test does not become an unsupported end-to-end delivery claim.

## Echo and response deduplication

addMessage keeps the existing array if any message has the same identifier; otherwise it appends. This handles the ordinary case where a server response and live event deliver the same persisted message identity. It does not deduplicate two separately persisted messages with different identifiers but identical text.

The helper also preserves the earlier object when the same identity arrives with changed metadata. If a later event includes a read timestamp or corrected projection, ignoring it may leave stale fields. A proposed merge contract should distinguish identity deduplication from object refresh.

Ordering is another independent property. Appending in arrival order can differ from creation order when network delivery is delayed. A product can choose chronological sorting or a stable server sequence, but the contract should address equal timestamps and optimistic pending messages. Do not assume an array with unique identities is necessarily a correctly ordered transcript.

A useful reducer fixture supplies response-first, event-first, duplicated-event, delayed-earlier-message, and same-id-updated-metadata cases. Expected arrays should be written independently, with exact identities and relevant fields. Counting entries alone cannot catch stale metadata or wrong order.

## Read notifications and silent failure

An effect scrolls to the bottom when message count, open state, conversation, or API dependency changes. If the panel is open and a conversation exists, it calls markRead and suppresses rejection with an empty catch. This is current source behavior; the course does not claim an executed read-receipt outage.

The server marks all unread public agent messages for that conversation, not an explicit viewport subset. A proposed UI promise such as read when visible requires a more precise contract and component evidence. Merely opening the panel may not mean every message was seen, especially after pagination is introduced.

The messagesRead callback contains a comment rather than updating ticks. The rendered empty span for a visitor message with null readByVisitorAtUtc is not an implemented agent-read receipt display. Moreover, visitor-read and agent-read fields have different audiences and meanings. A future UI should use the correct field rather than infer direction from a generic read label.

Silent catches can be a deliberate choice for noncritical ephemeral events, but they remove feedback for durable read-state synchronization. Decide which failures deserve visible retry, bounded telemetry, or quiet best effort. Treat typing failure differently from accepted message uncertainty rather than using one blanket error policy.

## Typing lifetime

onTyping sends true, clears the previous timeout, then schedules false after two seconds. The callback uses the conversation captured for that event invocation. A future conversation-switch feature would need to ensure the stop signal goes to the intended conversation and does not leave another typing indicator active.

The selected component does not show a dedicated cleanup effect for this timeout. A proposed unmount test should verify no late callback sends through an obsolete connection. This is distinct from hub connection cleanup and should have its own assertion.

Typing is ephemeral advisory state. It should not block a message from being persisted or be treated as proof of ownership. The server hub's Typing authorization boundary needs independent review, even if the client only calls it for its visible conversation.

## Independent exercises

Exercise NI08-A: submit A, edit B, then fail A. Define operation identity, draft revision, and a non-destructive recovery UI. Include the unknown-outcome case.

Exercise NI08-B: schedule two first sends before either response, then compare text deduplication with persisted message identity. Propose a server-backed first-conversation intent contract.

Exercise NI08-C: design transcript merge cases and read/typing feedback categories. State which tests belong to helper, component, service, and persistence boundaries.

Answers are in [widget workflow solutions](solutions-widget-workflows.md).
