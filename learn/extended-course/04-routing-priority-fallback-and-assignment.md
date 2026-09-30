# 04. Routing priority, fallback, and assignment

Read [ConversationRouter.cs](../../src/NetIntercom.Application/Routing/ConversationRouter.cs) and [OfficeHours.cs](../../src/NetIntercom.Domain/Entities/OfficeHours.cs). This lesson is a current-source trace. The domain runner establishes selected office-hours behavior, but it does not execute the EF-backed router.

The router's summary says an unmatched or outside-hours conversation lands in the fallback team's queue unassigned. The implementation has a more specific behavior: after choosing a fallback team, it still calls PickRoundRobinAsync. If that team has an eligible online member, the conversation can be assigned. This is a useful example of a stale or overbroad comment that should not override executable control flow.

## Rule selection is one decision

The router loads enabled rules, includes each team, and orders by Priority. It takes the first rule whose Matches method accepts the first message and source URL. At this point the selected value is a team, not necessarily an agent or a completed routing decision.

A test for priority should ensure two rules both match while their priorities differ. A fixture where only one rule matches cannot detect incorrect priority ordering. If priorities tie, the displayed query does not add a second ordering key. Do not promise a stable tie winner without a specified database order or an explicit tie-breaker.

The rule's Matches implementation is another boundary. This lesson does not infer its matching semantics from the word keyword in a comment. A complete matching exercise should read that method and distinguish substring, case, URL, and empty-input behavior before constructing expected values.

The first message and SourceUrl are inputs to selection. Later visitor messages on an existing conversation do not automatically rerun this router in the selected send handler. Routing a conversation and continuously reclassifying every message are different product behaviors.

## Office hours affect the matched team

If a rule produces a team, the router parses that team's OfficeHoursJson. When a nonnull schedule says closed, the router logs and clears the selected team reference. It then enters fallback selection. An empty or absent schedule has different mechanics: FromJson returns null for blank input, while an OfficeHours object with no windows returns open.

Malformed nonblank JSON can throw during parsing. The shown router does not catch that parse failure. An unknown timezone inside a valid schedule is different: IsOpenAt catches TimeZoneNotFoundException and uses UTC. A test should not collapse malformed configuration and unknown timezone into one generic closed result.

The actual domain lab characterizes empty windows, inclusive start, exclusive end, timezone fallback, and an overnight interval. The router still needs a fixture proving how those results affect the selected team. Function-level schedule evidence does not establish query order, fallback choice, or assignment.

The office-hours check shown here applies to the team from the matching rule. After fallback is selected, the code does not run the same check again on that fallback team. A proposed global office-hours policy must account for this explicitly. Do not assume every resolved team's schedule is checked simply because an office-hours call appears earlier in the method.

## Fallback selection has two stages

When no team remains, the router first asks for a team marked IsFallback. If none exists, it orders teams by CreatedAtUtc and takes the first. If no team exists at all, it logs and returns without routing. These are three distinct outcomes deserving separate fixtures.

The fallback query does not add ordering among multiple teams marked fallback. A configuration rule that requires exactly one fallback would need validation or a database constraint; it is not established by FirstOrDefault. Likewise, equal creation timestamps need a defined tie-breaker if deterministic earliest-team selection is important.

Use synthetic teams Cedar and Birch with deliberately different creation times and fallback flags. Keep the expected team independent of insertion order. Then add two fallback flags as an adversarial configuration case and state whether the intended policy rejects the configuration or defines a deterministic winner.

No-team return is not equivalent to request failure in the selected send flow. The caller continues constructing the message and saving the conversation unless another operation fails. A product can legitimately retain an unrouted queue, but its UI and reporting should not pretend every accepted conversation has a team.

## Team and policy are assigned differently

Once resolved, TeamId is assigned unconditionally. SlaPolicyId uses null-coalescing assignment, preserving an existing policy identifier if one is already present. Therefore routing can change team while retaining a policy that did not come from the newly selected team's default.

For a newly constructed conversation this distinction may be invisible because the policy begins null. An independent direct router fixture with an existing policy makes the behavior observable. This is not a recommendation to reroute arbitrary existing conversations through the method; it is a way to understand the public method's current state contract.

The send handler later loads SlaPolicy when its identifier is nonnull, so the retained identifier can affect timer creation. A test that only checks TeamId misses that downstream consequence. Include both team and policy in the expected result.

If the product wants policy to follow team on rerouting, define what happens to existing running timers. Replacing a policy reference cannot retroactively explain whether old deadlines should change. Configuration, current timers, and future timer defaults are separate decisions.

## Agent selection is availability-sensitive

PickRoundRobinAsync loads active team members, asks the presence service for online agent identifiers, filters membership by that set, and orders by LastAssignedAtUtc with null treated as DateTime.MinValue. It chooses the first remaining member. This approximates least-recently-assigned selection under the current snapshot.

The router then calls conversation.Assign and updates the chosen membership's LastAssignedAtUtc using its clock. It does not call SaveChangesAsync itself. Persistence belongs to the caller's unit of work. A service test should assert tracked changes and invocation discipline; an adapter test should establish the final committed assignment.

Two concurrent routing operations can read the same least-recent member before either persists its updated timestamp. The ordering algorithm alone does not guarantee globally fair distribution under competing contexts. A proposed stronger contract needs an atomic selection or reservation strategy and a realistic contention fixture.

Null timestamps create another tie domain. If several eligible members have never been assigned, no additional tie-breaker appears after LastAssignedAtUtc. A test with only one online member cannot establish fairness or determinism across that tie. Choose fixtures that expose the policy you intend to guarantee.

## Worked fallback trace

Suppose a matching team is closed, fallback team Birch has one active member Bo, and the presence service reports Bo online. The matched team is cleared, Birch is selected, TeamId becomes Birch, and PickRoundRobinAsync returns Bo. Assign sets the assignee to Bo and retains Birch as team. The method updates Bo's assignment timestamp.

The correct prediction is assigned fallback, despite the summary comment's unassigned wording. If no Birch member is online, the method leaves the conversation without a new assignee and logs queued status. Those two fixtures isolate the availability decision after fallback.

Now remove all fallback flags and provide two teams with different creation times. The earlier team becomes the fallback of last resort and still goes through member selection. This is a separate branch from explicit IsFallback selection. A complete test matrix should cover both rather than name every unresolved case fallback and assume they behave identically.

## Independent exercises

Exercise NI04-A: build a route table for matched/open, matched/closed, unmatched with fallback, unmatched without fallback, and no teams. Include whether office hours and member selection run in each case.

Exercise NI04-B: explain the stale unassigned-fallback comment using an online fallback member. Propose a deterministic tie rule for equal priorities and equal assignment timestamps without claiming it exists.

Exercise NI04-C: trace preexisting SlaPolicyId, caller-owned SaveChanges, and two simultaneous least-recent selections. State the smallest separate tests for policy retention, persistence, and fairness.

Answers are in [routing and presence solutions](solutions-routing-and-presence.md). Keep desired changes separate from source characterization.
