# Answer: INC-002 unassigned queue spike (presence grace broken)

**Branch:** `training/incidents/002-unassigned-queue-spike` · **Commit:** 5cc8d8d · BUILD: verified (0 errors). No unit tests cover `PresenceTracker` (the unit-test project doesn't reference `NetIntercom.Api`).

## Root cause
`PresenceTracker.Compute` implements the 60 s disconnect grace **from the agent's state object**: "not connected, but `LastDisconnectUtc` less than 60 s ago → still Online" (`PresenceTracker.cs:83-89`). The PR deletes that state object the moment the last connection closes, so there's no `LastDisconnectUtc` left to read and `Compute` returns `Offline` immediately (`:78-81`). The grace period is dead code now.

## Sequence
1. The proxy kills Sara's socket at 14:02:10. `AgentHub.OnDisconnectedAsync` → `Disconnected` → connections empty → **entry removed**.
2. SignalR client auto-reconnect: attempt 1 at +0 s often fails because the proxy is still tearing down. Attempt 2 at +2 s, attempt 3 at +10 s.
3. During that 2–12 s gap, `GetOnlineAgentIds()` doesn't include Sara. Agents logged in at the same minute have synchronized 100 s socket lifetimes, so the **whole team** drops out together. A visitor message in that window → `PickRoundRobinAsync` finds nobody → "queued".
4. Nothing ever re-routes a queued conversation (`RouteAsync` runs only on creation, `SendVisitorMessage.cs:83`), so it stays unassigned until a human picks it.
- Also a TOCTOU race: `Disconnected` checks `IsEmpty` and then `TryRemove`. A concurrent `Connected` for the new connection can add conn2 to the *same* state object between those two steps, and then the object is removed. Sara is connected but invisible until her next reconnect.

## The three side symptoms
- **Green dots:** `AgentHub.OnConnectedAsync` broadcasts `Online` on every reconnect (`AgentHub.cs:31`). The sweeper only broadcasts agents that appear in `SnapshotAll()`, and removed agents don't appear, so an `Offline` transition is **never broadcast**. The inbox refetches `/agents` only on `presenceChanged`, which leaves the dots showing the last "Online".
- **Frozen `PresenceSnapshots`:** the sweeper persists only `changed` entries. A removed agent vanishes, then comes back as `Online`, which equals `_lastBroadcast` → "no change" → never persisted.
- **Widget flicker:** `anyOnline` is computed from the snapshot (`PresenceSweeper.cs:76-81`). During synchronized team-wide gaps it flips false→true and broadcasts `teamPresence`.

## Fix
Keep the entry through the grace period and evict it later, in the sweeper, when `Compute` has returned `Offline` for longer than the grace (e.g. more than 10 min since `LastDisconnectUtc` with no connections). Eviction must also be atomic with respect to `Connected`: use `_agents.TryRemove(KeyValuePair.Create(id, state))` (compare-and-remove, .NET 5+) after re-checking `state.Connections.IsEmpty` under a per-state lock, or accept the race knowing `Connected` will recreate the entry. Evict `_lastBroadcast` in the same pass. Pruning empty `_viewers` sets is fine: that part of the PR was correct.

## Follow-ups
- Re-route queued conversations when an agent of the team becomes Online (the sweeper already sees the transition) or with a periodic "assign queued" job. That's a real second bug the incident exposed.
- Make `PresenceTracker` take `IClock` (it uses `DateTime.UtcNow`, `:34, 42, 55, 63, 69`). Without it, a grace-period test needs `Thread.Sleep(60s)`. With it: `Connected`, `Disconnected`, advance 30 s → assert Online; advance 61 s → Offline.
- An alert on "Offline→Online transitions per agent per hour" catches flapping.

## Postmortem notes
A "memory leak fix" deleted the data a time-based rule depended on. The review checklist question it answers: "does anything read this state *after* the event that deletes it?" The unbounded growth was real but small (one entry per agent ever seen, which is thousands at most). The fix traded a slow leak for a correctness bug.
