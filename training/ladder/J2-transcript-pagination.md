# J2: Paginate the agent transcript endpoint

**Rung:** junior · **Est.:** 3 h · **Skills:** API design, validation, EF query, unit + integration tests

## Context
`GET /api/v1/conversations/{id}/messages` (`Api/Controllers/ConversationsController.cs:44-46` → `GetConversationMessagesHandler`, `Application/Conversations/GetConversation.cs:41-59`) returns **every** message in a conversation, notes included. Its only filter is `after`, which exists for reconnect catch-up. The inbox calls it with no parameters (`frontend/inbox/src/pages/Inbox.tsx:182-185`). One long-running B2B customer conversation already has 3,000+ messages.

## Ticket
> Opening Acme's conversation takes about 4 s and makes the browser tab stutter. Load the latest 50 messages and let me scroll up for older ones.

## Constraints
- **Backward compatible:** the existing `?after=` catch-up (every message newer than X, oldest first) keeps working unchanged for inbox builds already deployed.
- New parameters: `before` (a cursor) and `limit` (default 50, max 200). Invalid values return 400 through FluentValidation, never 500.
- Ordering is deterministic even when two messages share a `CreatedAtUtc`.

## Definition of done
- Validator + handler change + controller params. The response is still an array, oldest-first within the page.
- A unit test for the validator. An integration test that seeds 120 messages, pages back twice, and asserts no duplicates and no gaps.
- One paragraph in the PR on why you didn't use `page/pageSize` offset pagination here.

## Explain before touching
1. Which index serves `WHERE ConversationId = @id AND CreatedAtUtc < @before ORDER BY CreatedAtUtc DESC LIMIT 50`? Find it in `AppDbContext.OnModelCreating`.
2. A timestamp alone as `before` can skip or duplicate messages. Show exactly how, using two messages written in one `SaveChangesAsync` batch.
3. What does "oldest-first within the page" force you to do after a `DESC LIMIT` query?
4. `Include(m => m.Conversation).ThenInclude(c => c.Visitor)` runs on every call. Does each message need it? What would you project instead?
5. Which existing client breaks if the *default* call (no params) changes to "latest 50"? Decide whether that's acceptable.

<details><summary>Hints</summary>

- A composite cursor `(CreatedAtUtc, Id)` fixes ties. Guids aren't time-ordered, but they still give a total order.
- The widget's own catch-up lives in `Application/Widget/WidgetQueries.cs`. It's out of scope here, but note what it would need.
</details>

Sealed answer: [`../_answers/ladder-J2.md`](../_answers/ladder-J2.md)
