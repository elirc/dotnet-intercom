# 07. Reconnect, captured state, and live delivery

Read [Widget.tsx](../../frontend/widget/src/Widget.tsx), [VisitorHub.cs](../../src/NetIntercom.Api/Realtime/VisitorHub.cs), and [widget api.ts](../../frontend/widget/src/api.ts). Reconnect correctness requires both restoring live subscription and reconciling missed persisted messages. The current component's callback captures a bootstrap result, which is not the same as the latest conversation state.

This is a source-trace lesson. The course has not executed a browser or a SignalR integration environment. The deterministic schedules below are proposed component-test fixtures.

## Bootstrap captures a session object

The effect calls startSession, sets conversation and messages from its response, and creates the hub connection. Its callbacks close over the local session object. The effect has an empty dependency list, so later conversation state updates do not recreate that captured session value through this effect.

The onreconnected callback checks session.conversation. If it exists, the callback fetches messages for that bootstrap conversation and replaces the message list. If it was null, the callback does nothing. It does not read the current conversation state through a ref or captured mutation variable.

This gives two useful scenarios. A returning visitor whose bootstrap included conversation C1 can refetch C1 after reconnect. A new visitor whose bootstrap had no conversation can send a first message and obtain C2, yet the callback still sees the original null. Updating React state does not rewrite the earlier object captured by the callback.

Do not describe this only as stale cache. It is closure identity: the callback references the session result from one effect invocation. A cache invalidation call elsewhere would not automatically change that captured value. Name the mechanism precisely so the proposed repair targets the right boundary.

## Joining is separate from fetching

After initial connection start, the component invokes JoinConversation when bootstrap includes a conversation. After a send returns a different or first conversation, send invokes JoinConversation for that returned identity. Those paths establish explicit joining attempts.

The onreconnected callback does not invoke JoinConversation. The server's OnConnectedAsync adds the connection to the general visitors group and sends team presence; it does not automatically join the visitor-specific conversation group. Therefore the selected reconnect path lacks an explicit rejoin operation for the conversation group.

A transcript fetch can recover persisted history at one instant without restoring future live delivery. A rejoin can restore future events without recovering events missed before the join. A correct recovery design needs both, with an ordering and merge policy that handles their overlap.

Avoid claiming that withAutomaticReconnect alone restores every application subscription. The concrete source shows where application group joining occurs, and the reconnect callback should be reviewed against that requirement. A real integration test can establish connection and group behavior under the chosen hosting configuration.

## Recovery ordering creates overlap cases

Suppose the client rejoins first and then fetches history. A live message may arrive while the fetch is in flight. If the fetch response represents an earlier snapshot and replaces the whole list, it can erase the newly received message from local state even though the database still contains it.

Suppose it fetches first and joins afterward. A message committed between those actions may miss live delivery and be absent from the completed fetch. A second reconciliation or a stable cursor protocol can close that gap. Neither ordering is automatically sufficient without an overlap strategy.

The existing addMessage helper deduplicates by message identifier when appending live or send-response messages. The reconnect callback uses setMessages(latest), which is a different update operation. A repair should consider how fetched and live messages merge, not merely swap session.conversation for a newer identifier.

A proposed reducer can merge by identity and order by a stable server sequence or timestamp-plus-identity contract. It should preserve updated fields such as read timestamps according to version or response authority. Keeping the first object forever because its id already exists may discard legitimate later metadata changes.

## Controlled component schedule

Use a fake connection with explicit callbacks and deferred API promises. Bootstrap with no conversation. Complete connection start. Send the first message and resolve it with C2 and M1. Assert the join attempt for C2. Then invoke reconnect and observe which conversation the callback requests and whether it rejoins.

A second fixture bootstraps C1, triggers reconnect fetch, emits M2 live while the fetch remains pending, and then resolves the fetch with only M1. The proposed correct merge should retain M2. The current replacement operation is the source feature this schedule investigates.

A third fixture changes the active conversation through an explicit future feature while an older recovery request is pending. The late C1 result must not replace C2's transcript. Capture operation identity and discard or route results according to the submitted conversation rather than the currently displayed one.

These fixtures use controlled ordering instead of sleeps. They establish component behavior under a chosen schedule without needing a real network outage. A separate integration fixture remains necessary for actual server group membership and missed-event behavior.

## Cleanup has its own race

The effect's cleanup sets cancelled and stops connRef.current if present. The async setup checks cancelled after startSession but not at every later await. Connection start can finish after cleanup, and the reference is assigned only after that await returns.

A proposed lifecycle test holds conn.start unresolved, unmounts, then resolves start. Verify that no live orphaned connection remains and no state or join action occurs for the unmounted instance. The selected source suggests a lifecycle window, but this chapter does not claim the leak was observed in a browser.

A robust cleanup design owns the locally created connection as soon as it exists and checks cancellation after asynchronous setup stages. It also handles a failed start distinctly from a successful start followed by unmount. Avoid solving this with arbitrary delays that merely make the window harder to reproduce.

The typing timeout is another resource to inspect: it is cleared on subsequent typing but no dedicated unmount cleanup appears in the selected component. A complete component lifecycle test should cover that timer without confusing it with the hub connection's cleanup responsibilities.

## Recovery messages should be truthful

The component initially sets ready before connection start completes. If later setup throws, it sets error when not cancelled. The UI's status therefore combines session bootstrap and connection setup rather than exposing separate durable-history and live-connection states.

A proposed UI can show history available while reconnecting, with a bounded indication that live updates are delayed. It should preserve the transcript and draft rather than replace all useful content with a generic unavailable message. This is a product acceptance criterion, not a claim about current rendering.

Reconnect success should mean the defined recovery steps completed. If rejoin succeeds but history fetch fails, the client can receive future messages while still missing old ones. Report that partial recovery honestly and provide a retry for reconciliation. A single green connected dot cannot represent all those dimensions.

## Independent exercises

Exercise NI07-A: compare bootstrap-with-C1 and bootstrap-with-null followed by first send. Name the exact value captured by onreconnected and the explicit group join operations in each path.

Exercise NI07-B: construct fetch-versus-live overlap in both orders and design a merge oracle that prevents lost or duplicated messages. Include updated metadata on an existing message identity.

Exercise NI07-C: design an unmount-during-start fixture and a truthful partial-recovery UI contract. Distinguish component evidence from actual SignalR group integration evidence.

Use [widget workflow solutions](solutions-widget-workflows.md) after writing the schedules.
