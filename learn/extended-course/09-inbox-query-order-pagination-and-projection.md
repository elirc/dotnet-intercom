# 09. Inbox query order, pagination, and projection

Read [ListConversations.cs](../../src/NetIntercom.Application/Conversations/ListConversations.cs), [PaginatedList.cs](../../src/NetIntercom.Application/Common/PaginatedList.cs), [Dtos.cs](../../src/NetIntercom.Application/Dtos.cs), and [AppDbContext.cs](../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs). This lesson develops independent query expectations before proposing performance changes.

The existing training material includes historical latency scenarios. The current list source includes Visitor, Team, Assignee, and SlaTimers, but it does not Include Messages. The current ConversationDto has no unread-count property. Preserve those facts when extending old exercises; do not describe a removed collection include or absent field as a current implementation detail.

## Filters compose rather than override

State, team, and assignee filters are applied when their values are nonnull. Unassigned applies only when true and adds AssigneeId equal to null. If a request supplies a specific assignee and Unassigned true, both predicates apply. The result can be empty because the conditions are contradictory.

A proposed API might reject that combination as invalid, but the current query composition does not replace one with the other. A test should predict the implemented result and separately describe desired request validation. Avoid making a UI checkbox silently clear another filter unless that interaction is specified.

Search runs when the string is not null or whitespace. It lowercases the request but does not trim the term before Contains. It compares lowercased subject, visitor name, and visitor email, using empty strings for null fields. A search containing leading or trailing spaces can therefore differ from its trimmed form.

The meaning of ToLower and Contains in a real provider depends on translation and database behavior. This chapter establishes the expression shape from source. An in-memory LINQ fixture can teach logical composition but cannot establish PostgreSQL collation, index use, or generated SQL performance.

## Sorting is a contract with tie cases

Sort created orders descending by creation time. Sort slaBreach orders by the earliest running timer deadline, substituting DateTime.MaxValue when none exists. Every other value falls back to descending last-message time. Unsupported sort strings do not reject in this switch; they select the default branch.

No secondary ordering appears in these branches. Equal timestamps or equal earliest deadlines therefore need a deliberate tie policy if stable pagination is required. A stable test dataset should include ties rather than accidentally give every conversation a different time.

For SLA sort, completed breached timers do not count as currently running deadlines. A conversation with historical breaches but no running timer receives the no-running fallback position. Another conversation with a future running timer sorts before it. This can be correct for an action queue even though it differs from sorting by historical severity.

Construct three conversations: C1 with a running deadline at noon, C2 with only a breached timer, and C3 with running deadlines at eleven and thirteen. The selected sort places C3 before C1, while C2 receives the maximum fallback. The DTO's HasBreachedSla is a separate historical projection, so it can be true even when ActiveSlaTimer is null.

## Pagination performs two observations

CreateAsync clamps page to at least one and page size into one through one hundred. It executes CountAsync, then Skip and Take followed by ToListAsync. The returned envelope contains the normalized page and size, total count, and items. TotalPages uses ceiling division.

These are two query observations. Without an explicitly established snapshot boundary, records can change between count and page retrieval. The count may describe a slightly different state from the items. This is not necessarily a product defect, but the UI and tests should not assume perfect snapshot consistency without a contract.

Offset pagination also changes under insertion and deletion. If a new recent conversation appears before the second page request, one record can shift from page one to page two and appear twice across the user's journey. A stable ordering helps tie behavior but does not remove offset drift under changing membership.

A proposed keyset cursor can define continuation from the last sort key and identity, but it needs separate designs for last-message and SLA-deadline sorts because those keys can change. Do not sell cursor pagination as a universal snapshot guarantee. It changes traversal semantics and must state what updates mean.

## Large page arithmetic

The offset expression multiplies page minus one by page size using integers. The displayed normalization limits page size but does not impose an upper bound on page. An adversarial integer input can therefore raise an arithmetic-boundary question before a database request is issued or translated.

The appropriate proposed contract can reject excessively large pages or compute safely with a checked range before query construction. The exact HTTP parser and validation behavior need a route-level fixture; this lesson does not claim a particular live response for overflow.

This case illustrates why clamping one parameter is not complete pagination validation. Define absent, zero, negative, excessive, and malformed values separately at the request boundary. Then verify the shared helper's accepted domain independently.

## Projection requires loaded relationships

ConversationDto.From reads Visitor, optional team and assignee names, timestamps, the earliest running timer, and whether any timer is breached. The list's Includes support those navigation reads. Removing all Includes without replacing them with an equivalent server projection can break DTO construction or silently alter data.

A performance repair should preserve the response contract while changing how data is fetched. A direct projection may reduce materialization, but it must reproduce null handling, active timer choice, and breach history. Write an independent DTO fixture before comparing query plans.

The current mapping provides an index on State and LastMessageAtUtc, plus individual team and assignee indexes. That is design evidence, not measured proof that every filter and sort uses an efficient plan. Search expressions and SLA aggregation have different query shapes. Capture actual SQL and plans under a controlled dataset before claiming a latency improvement.

The existing absence of Messages Include is particularly important. A historical incident exercise may propose removing it, but that change has no current target if the source already lacks it. A useful new exercise instead verifies preservation and identifies current materialization costs without inventing an old defect.

## Independent exercises

Exercise NI09-A: predict contradictory assignee/unassigned filters, a spaced search term, and an unknown sort value. Separate current query behavior from proposed input validation.

Exercise NI09-B: order the three SLA fixtures, then add tied deadlines and a new conversation between page requests. Explain which instability comes from missing tie-breakers and which from offset traversal.

Exercise NI09-C: design a response-preserving query optimization test with SQL capture, exact DTO comparison, and bounded data volume. State why mapped indexes and a fast in-memory test are not measured database evidence.

Review [query and reporting solutions](solutions-query-and-reporting.md) after building the expected datasets.
