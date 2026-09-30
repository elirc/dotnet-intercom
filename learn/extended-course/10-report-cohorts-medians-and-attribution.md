# 10. Report cohorts, medians, and attribution

Read [Reports.cs](../../src/NetIntercom.Application/Reports/Reports.cs) and revisit [Conversation.cs](../../src/NetIntercom.Domain/Entities/Conversation.cs). A reporting endpoint can combine several valid calculations that refer to different populations. The learner's task is to identify each population and avoid presenting them as one universal cohort.

This lesson supplies independent arithmetic fixtures. It does not claim that the EF query translated or ran against a database. Numerical expectations should be written before executing any future query harness.

## The overview cohort starts with creation time

The overview loads conversations created at or after FromUtc and strictly before ToUtc. This half-open interval includes the start boundary and excludes the end boundary. It does not select conversations merely because they received a message or closed during that window.

NewConversations is the number of selected conversations. ClosedConversations counts selected rows with nonnull ClosedAtUtc. OpenConversations counts selected rows whose current State is Open. These are current-record measurements over a creation cohort, not an event count of every opening or closing during the period.

A conversation created before FromUtc and closed during the period does not enter this cohort. A conversation created during the period and closed afterward can enter with a nonnull close timestamp when the report is run later. The query does not bound ClosedAtUtc to ToUtc. This affects how historical reports change over time.

A user expecting an immutable report as of the period end needs a different snapshot or event-time contract. Do not rename the current count without explaining its actual definition. A dashboard caption should say which time determines membership and which current fields determine outcomes.

## Reopening changes current close evidence

Conversation.Reopen clears ClosedAtUtc. Therefore a conversation that closed and later reopened can stop contributing to ClosedConversations and resolution duration under this current-record report. The code does not retain a separate close-event history in this entity field.

This does not mean reopening erases all possible evidence elsewhere; it means this report's selected fields no longer represent the earlier close. A historical resolution metric would need event or episode data and a policy for first close, last close, or final resolution.

Snoozed conversations are neither Open under the exact state count nor necessarily Closed by timestamp. Consequently NewConversations need not equal OpenConversations plus ClosedConversations. A worksheet should include a snoozed row so this non-exhaustive partition becomes visible.

Avoid treating such a difference as arithmetic corruption before checking definitions. Sometimes counts are deliberately overlapping or incomplete categories. The product problem may be unclear labels rather than an incorrect sum, but that conclusion still requires agreement about intended meaning.

## First response and resolution samples

The handler computes first-response minutes only for selected conversations with a nonnull first-reply timestamp. It subtracts creation time. Conversations without a reply are absent from the median sample rather than represented as zero or as elapsed waiting time.

Resolution durations similarly include only rows with nonnull ClosedAtUtc. A report can therefore show a low median while many conversations remain unanswered or unresolved. Present the sample size or companion pending count if users need to interpret the result responsibly.

The source does not clamp negative durations. A synthetic row with reply before creation contributes a negative value. That is an important data-quality fixture, not evidence that the application necessarily produces such rows. Decide whether the report should reject, flag, exclude, or faithfully show inconsistent records under a proposed policy.

The Median helper sorts the supplied List in place. For odd count it returns the middle element; for even count it averages the two central values; for empty input it returns null. A caller that needs original ordering should pass a copy. The report's local lists make that mutation harmless to the shown calculation, but the helper's public behavior is still worth characterizing.

## Worked overview dataset

Use a window from Monday midnight through Tuesday midnight. C1 is created Monday at 09:00, first replied at 09:04, and closed at 09:20. C2 is created Monday at 10:00, first replied at 10:12, and remains Open. C3 is created Monday at 11:00, has no reply, and is Snoozed. C4 is created Sunday at 23:50 and closed Monday at 00:20.

The overview creation cohort includes C1, C2, and C3, not C4. NewConversations is three. ClosedConversations is one, OpenConversations is one, and the snoozed record explains the remaining row. First-response samples are four and twelve minutes, so median is eight. Resolution samples contain twenty only, so median resolution is twenty.

Now reopen C1 after the reporting window and run the current-record report again. ClosedAtUtc becomes null and State becomes Open. New remains three, Closed becomes zero, Open becomes two, and median resolution becomes null if no other selected row is closed. First-response median remains eight because reopening does not clear FirstAgentReplyAtUtc.

The changed report is predictable from current fields. A requirement that last week's report never changes would need an as-of model or immutable event projection. It cannot be guaranteed by simply choosing a past FromUtc and ToUtc.

## Agent activity uses message time

AgentStats counts public agent messages whose own CreatedAtUtc falls inside the requested window. It groups by agent identity and display name, counts messages, and counts distinct conversation identifiers. Those conversations need not have been created inside the overview cohort.

An agent replying Monday to a conversation created Sunday contributes to Monday message activity even though that conversation is absent from NewConversations. This is a valid mixed-window design if labelled clearly. It becomes misleading when the dashboard implies every agent activity row is a subdivision of the new-conversation count.

Internal notes are excluded from this activity query. An agent who writes only notes can be operationally busy yet have no counted public messages. A proposed workload report may include notes separately, but it should not silently alter the existing customer-response metric.

The grouping includes display name as well as identity. Actual behavior under name changes depends on how current navigation data is joined and grouped. Do not assume the report preserves historical display names from message time; the shown query uses the related agent's current display name.

## First-reply attribution uses yet another cohort

The firstReplies query selects public agent messages belonging to conversations created in the requested window. It groups by conversation and chooses the earliest message by CreatedAtUtc. It does not require that message itself to fall before ToUtc.

Per-agent first-response medians are then built from those selected first replies. Finally, the returned AgentStats rows come from the earlier message-window activity query, and each row looks up its median. An agent with a first reply after the window may influence the dictionary but not appear in the returned activity rows if they sent no in-window messages.

Conversely, an agent active in the window on older conversations may appear with a null first-response median because none of their selected first replies belongs to the creation cohort. Null here does not mean zero response time or no work. It means no sample under that particular attribution definition.

Tie timestamps among first public replies have no displayed secondary order. If two agents reply at the same instant, deterministic attribution requires an explicit tie-breaker or policy. A test dataset should include the tie if attribution is important to performance evaluation.

## SLA report population

The SLA report selects timers whose StartedAtUtc is inside the half-open window and whose status is Met or Breached. Running and Cancelled timers are excluded. It groups by the conversation's current team and timer kind, not a team snapshot stored when the timer began.

A reassigned conversation can therefore move historical timer outcomes between team groups in this report. That may or may not match the desired accountability model. A proposed historical team-at-start metric requires stored attribution or events; it cannot be inferred from the current TeamId alone.

MetPercent is rounded to one decimal place using Met divided by Met plus Breached. The source contains a zero-denominator fallback, but groups produced from the shown filtered query ordinarily contain at least one met or breached timer. An empty overall population yields no rows rather than a synthetic one-hundred-percent row for every team.

Use a fixture with two Met, one Breached, one Running, and one Cancelled timer for a team and kind. The denominator is three and the displayed percentage is 66.7. Counting all five timers would produce a different metric, forty percent, which is not this implementation.

## Independent exercises

Exercise NI10-A: calculate the overview dataset before and after reopening C1. Explain why creation cohort, current state, and resolution sample are different dimensions.

Exercise NI10-B: add an agent reply to an old conversation, a first reply after ToUtc, and an internal note. Predict activity-row membership and first-response median availability separately.

Exercise NI10-C: compute SLA percentage with running and cancelled timers, then reassign the conversation's team. Propose a historical-attribution contract and identify the new evidence it needs.

Answers are in [query and reporting solutions](solutions-query-and-reporting.md). Keep arithmetic oracles separate from unexecuted provider translation and performance claims.
