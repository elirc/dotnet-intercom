# 05. Presence grace, idleness, and replica scope

Read [PresenceTracker.cs](../../src/NetIntercom.Api/Realtime/PresenceTracker.cs) alongside the router from the preceding lesson. Presence is not a simple connected boolean. The selected implementation combines connection membership, last activity, last disconnect, and process-local storage.

This lesson is source reasoning. No real sockets, multiple replicas, or time-controlled presence harness were executed in the opening domain lab. The tracker calls DateTime.UtcNow directly, unlike the router's injected clock, which affects how a future deterministic test should be designed.

## The status decision order

The tracker stores agent state in a ConcurrentDictionary. Each state contains a concurrent set of connection identifiers and mutable last-activity and last-disconnect timestamps. An unknown agent returns Offline immediately.

For a known agent, Compute checks whether any connection remains. If none remains and time since last disconnect is greater than sixty seconds, it returns Offline. Otherwise it compares time since last activity with five minutes: greater means Away, and the remaining cases mean Online.

The inequalities are strict. Exactly sixty seconds since disconnect does not yet take the Offline branch. Exactly five minutes since activity does not yet become Away. These boundary cases should be explicit in a deterministic fixture, because tests that use only two and ten minutes cannot catch an off-by-one comparison change.

Connection absence during the grace interval therefore does not guarantee Online. If last activity is already old, the status can be Away. Grace delays the Offline classification; it does not reset activity or override idleness. That is an important distinction for a user who closes an idle tab.

## Connection and heartbeat updates

Connected gets or creates the agent state, adds the connection, and sets last activity to current UTC time. Disconnected removes the connection if the agent exists and records current disconnect time. It also removes that connection from viewer collections. Heartbeat updates activity only for an already known agent.

An unknown heartbeat does not create presence. A connection event does. A future client protocol should respect that lifecycle rather than assume sending heartbeats to an arbitrary identity registers it. Authentication and identity derivation occur in the hub and need separate inspection.

With two connections, disconnecting one leaves the agent connected through the other. The disconnect timestamp is still updated, but Compute ignores grace while a connection remains. Activity is shared at agent level, so a heartbeat from one connection affects the aggregate agent status seen by the router.

This is a deliberate data-model choice with consequences. It cannot tell whether each individual tab is active, and it does not model per-device availability preferences. Adding those features would require separate identities and aggregation rules rather than renaming LastActivityUtc.

## A deterministic worksheet

Use an explicit observation instant T in a proposed test seam. Case one has one connection and activity at T minus two minutes: Online. Case two has one connection and activity at T minus six minutes: Away. Case three has no connection, disconnect at T minus thirty seconds, and activity at T minus one minute: Online under grace.

Case four has no connection and disconnect at T minus ninety seconds: Offline regardless of recent activity because the disconnected branch wins. Case five has no connection, disconnect thirty seconds ago, and activity six minutes ago: Away. The fifth case distinguishes grace from an unconditional online extension.

Do not implement these tests with real waits lasting minutes. A proposed refactor can inject time or expose a pure status calculation under an internal test seam. If preserving current source unchanged, a carefully scoped reflection harness could inspect private state, but its fragility should be acknowledged and its method signatures checked. The course has not installed either approach.

## Online selection is narrower than known presence

GetOnlineAgentIds captures one now and returns only agents whose computed status is Online. Away agents remain known and can appear in SnapshotAll, but they do not enter this online set. The router's member selection therefore excludes Away as well as Offline.

A product requirement that available-but-idle agents should receive work would differ from this current contract. It should be discussed explicitly rather than implemented by increasing a timeout until the symptom seems rarer. Timeout tuning, availability preference, and workload capacity answer different questions.

SnapshotAll also captures one observation time, then computes a status for each known agent. GetLastActivity returns DateTime.MinValue for an unknown agent. A consumer should not format that sentinel as a real historical activity timestamp. Its meaning is absence of tracked state, not a person active in year one.

The tracker does not visibly remove old agent entries when they become Offline. That observation suggests a bounded-retention question for long-lived processes, but this lesson does not claim a measured memory leak. A useful investigation would count retained identities under a controlled lifecycle and define the desired cleanup contract.

## Replica-local knowledge

The dictionaries belong to one tracker instance in one process. If an agent's connection is known only to replica one, a separate tracker on replica two can report that agent unknown and Offline. A database PresenceSnapshot mentioned in the class comment does not automatically make this live computation read shared state; the methods shown operate on the local dictionaries.

This matters because routing asks the injected presence service for online identifiers. To establish actual deployment behavior, inspect registration and any shared presence implementation. The source here identifies the limitation of this class, not every possible deployment configuration.

A proposed scale-out design could maintain leased shared presence, aggregate connection counts, or route relevant work to an authoritative owner. Each design needs expiration and failure semantics. Simply adding a distributed message bus does not prove all replicas have a current, consistent availability view.

Use a two-replica model with separate dictionaries to predict the problem before building infrastructure. Then state which real integration fixture would establish the chosen shared-state contract. A single-process test cannot validate cross-replica routing merely by constructing two agent identities.

## Viewer identity and collision indicators

The tracker also keeps conversation viewers by connection identity, storing agent identity and name. GetViewers deduplicates by agent identity. Two tabs for one agent therefore appear as one agent in the returned list even though two connection entries exist.

RemoveViewer removes one connection from one conversation. RemoveAllViewersForConnection returns conversations from which that connection was removed. Disconnected also removes connections from viewer dictionaries. A hub flow that combines these methods must consider order: removing entries before collecting affected conversations can change what notifications are needed. Inspect the caller before making a definitive claim about the live collision indicator.

A presence or viewing indicator is advisory information. It is not a lock preventing concurrent edits. Two agents can both see or modify a conversation unless the command persistence contract separately arbitrates changes. Keep advisory UI and transactional correctness distinct in both tests and user explanations.

## Independent exercises

Exercise NI05-A: predict status at exact grace and idle boundaries, then compare an idle disconnected agent inside grace with an active disconnected agent outside grace.

Exercise NI05-B: trace two connections, one heartbeat, and one disconnect. Explain the aggregate status and the router's exclusion of Away members.

Exercise NI05-C: design a two-replica presence fixture and a two-tab viewer fixture. State which results are advisory and which separate persistence guarantees are required for concurrent conversation commands.

Consult [routing and presence solutions](solutions-routing-and-presence.md) after completing the worksheet.
