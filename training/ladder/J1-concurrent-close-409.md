# J1: Two agents close the same conversation. Return 409, not 500

**Rung:** junior · **Est.:** 2–3 h · **Skills:** API design (status codes), EF Core concurrency, integration tests

## Context
`Conversation.RowVersion` is mapped to PostgreSQL `xmin` (`src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs:79`), so EF Core already throws `DbUpdateConcurrencyException` when two writers race. Nothing catches it. `ApiExceptionHandler.TryHandleAsync` (`src/NetIntercom.Api/Infrastructure/ApiExceptionHandler.cs:18-33`) maps only validation, not-found, forbidden, unauthorized and domain errors. Everything else becomes a 500 and gets logged as `Unhandled exception`.

## Ticket (from the support lead)
> Sara and Alex both hit **Close** on the same chat. Alex got a red "An unexpected error occurred" and filed a bug. It should tell the second person that someone else changed the conversation, and show them where it stands now. Snooze and Assign do the same thing.

## Constraints
- No new NuGet packages. No change to the `Conversation` entity or its table.
- The problem-details body keeps the `correlationId` extension that every other error has.
- No server-side retries. The second agent decides what to do next.

## Definition of done
- A conflict on `PUT /conversations/{id}/state` or `PUT /conversations/{id}/assignee` returns **409** with an RFC 7807 body (`title`, `detail`, `correlationId`).
- 409s are logged at Warning, not Error.
- An integration test in `tests/NetIntercom.IntegrationTests` shows two writers on the same row: one succeeds, one gets a 409. Use either two `DbContext` scopes or two HTTP calls with a deliberately stale entity, and say which you chose and why.
- The inbox shows the server's `detail` message and refetches the list. Check what `frontend/inbox/src/pages/Inbox.tsx:220-224` does with `changeState` errors today.

## Explain before touching (write the answers first)
1. Which layer should know about `DbUpdateConcurrencyException`: Domain, Application or Api? Why?
2. Trace the request: controller → MediatR pipeline (`Application/Common/Behaviors.cs`) → handler → `SaveChangesAsync`. Where is the exception thrown, and which behavior logs it on the way out?
3. `xmin` changes on *every* update of the row. Name two **non-conflicting** writes in this codebase that will now reach an agent as a 409. (Search for writers of `Conversation`.)
4. Which fits better here, 409 or 412? What would the client have to send for 412 to make sense?
5. Which test proves the behavior? Why is a unit test with a mocked `IAppDbContext` useless for this?

<details><summary>Hints</summary>

- `LoggingBehavior` catches everything except `ValidationException` and logs it at Error (`Behaviors.cs:50-54`). Your 409 will go through it.
- `SendVisitorMessageHandler` updates `LastMessageAtUtc` on the same row.
- For a deterministic race: load the entity in scope A, update it through scope B, then save A.
</details>

Sealed answer: [`../_answers/ladder-J1.md`](../_answers/ladder-J1.md)
