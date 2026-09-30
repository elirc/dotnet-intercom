# Routing and presence solutions

These answers review source-derived behavior. The isolated domain lab executes office-hours logic but not the EF-backed router or process-local presence service.

## NI04-A: route decisions

A matched open team remains selected and proceeds to member selection. A matched closed team is cleared, then fallback selection runs. An unmatched request goes directly to fallback. If no explicit fallback exists, the earliest-created team is selected. If no team exists, the method returns without team assignment.

Office hours are checked on the matched team before fallback, not again on the eventual fallback team. Every resolved team proceeds to PickRoundRobinAsync. This distinction should appear in the table, because it determines whether an online fallback member can receive the conversation.

The no-team path does not itself throw. The selected send handler can continue saving an unrouted conversation and message. Tests should therefore distinguish an unrouted accepted record from a rejected send rather than assume routing always yields an assignee.

## NI04-B: comment versus control flow

With a closed matched team and an online active member of the fallback team, the implementation selects the fallback team and then selects that member. Assign sets the member as assignee, contradicting the summary's universal unassigned wording. The source trace is enough to identify the comment mismatch; actual database and presence integration remain separate tests.

A proposed deterministic rule can add a stable identity tie-breaker after Priority and after LastAssignedAtUtc. The exact choice should be documented so tests do not accidentally rely on insertion order. This improves determinism but does not by itself guarantee fair selection under concurrent contexts.

A fixture with two matching equal-priority rules and two equally eligible members is necessary to expose these ties. A single-match fixture cannot establish either tie policy, even when its expected assignment is correct.

## NI04-C: policy, commit, and contention

A nonnull SlaPolicyId survives the router's null-coalescing assignment even when TeamId changes. The selected member's assignment timestamp is changed in memory, but the router does not save. The caller owns persistence.

Use a direct router fixture to assert policy retention, a service or adapter fixture to establish the caller's save, and competing contexts to test least-recent selection under contention. Two operations can observe the same oldest assignment timestamp before either update becomes visible.

A deterministic sort is not an atomic reservation. If the requirement is one assignment per turn across replicas, define and test the database-level selection contract. Do not claim that a local ordering expression supplies that guarantee.

## NI05-A: strict boundaries

At exactly sixty seconds after disconnect, the greater-than grace test does not return Offline. The remaining idle test determines Online or Away. At exactly five minutes after activity, the greater-than idle test still permits Online.

An idle agent disconnected thirty seconds ago can be Away. An active agent disconnected ninety seconds ago is Offline because the disconnected branch returns first. Grace delays Offline; it does not force Online and does not reset activity.

Use an explicit observation instant in the proposed fixture. Real sleeps near a boundary introduce scheduling noise and make a precise comparison test unreliable. The current tracker does not inject a clock, so any test seam or refactor must be named as proposed work.

## NI05-B: multiple connections

Two connections share one agent activity timestamp. A heartbeat updates that shared timestamp. Disconnecting one connection leaves the other, so the status remains governed by idleness while connected. The recorded disconnect time does not force Offline when a connection remains.

GetOnlineAgentIds includes Online only. Away agents may remain visible in SnapshotAll but are excluded from the router's online candidate set. A product that wants idle agents eligible needs a different explicit policy; changing a timeout without defining that policy obscures the intended behavior.

An unknown heartbeat does not create a state entry. A connection does. This lifecycle should be part of any client protocol fixture rather than assumed from the existence of a heartbeat method.

## NI05-C: advisory state and real authority

Two separate trackers can disagree because one knows the agent's connection and the other does not. Constructing two agent identities in one tracker does not reproduce that replica boundary. A shared-presence proposal needs a real expiration and consistency contract before routing can rely on it across processes.

Two tabs for one agent create two connection entries, while GetViewers deduplicates by agent identity. Removing one tab should not necessarily remove the agent from the returned viewer list while another remains. Test both connection-level cleanup and agent-level presentation.

Presence and viewer indicators are advisory. They do not lock conversation edits or enforce optimistic concurrency. A separate persistence test must establish command arbitration, and the UI should not imply that an absent viewer indicator proves exclusive access.
