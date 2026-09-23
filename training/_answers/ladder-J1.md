# Answer: J1 concurrent close → 409

## Reference analysis (compare with your "explain before touching")
1. **Layer.** Api. `DbUpdateConcurrencyException` is an EF/persistence exception. Mapping it to HTTP belongs next to the other mappings in `ApiExceptionHandler`. Application could wrap it in a `ConflictException` (a cleaner boundary, since Application already references EF Core through `IAppDbContext` returning `DbSet<>`). Either is defensible. Converting it in the Domain is wrong: the Domain never sees persistence.
2. **Trace.** Thrown inside `SaveChangesAsync` in the handler (`ManageConversation.cs:52` for assign, `:115` for state). It propagates through `ValidationBehavior` (untouched), then `LoggingBehavior` catches it and logs **Error** "Request … failed" (`Behaviors.cs:50-54`) and rethrows. Then `ApiExceptionHandler`. So even after your mapping, you get an Error-level log unless you also exclude it in `LoggingBehavior`, or give expected exceptions their own level. Most people miss this.
3. **False conflicts.** `xmin` bumps on *any* update to the row: a visitor message (`SendVisitorMessage.cs:102-105` sets `LastMessageAtUtc`/preview and may reopen), an agent reply from another agent (`SendAgentMessage.cs:62-64`), and auto-assignment by the router. An agent closing a conversation while the visitor types gets a 409 even though nothing *semantically* conflicted. That's acceptable, and arguably correct: the agent should see the new message before closing. But the UX text must say "the conversation changed", not "someone else closed it".
4. **409 vs 412.** 409 fits because the client sent no precondition. 412 needs `If-Match: "<rowversion>"` from the client, with the server comparing it. That's a bigger contract change: the DTO would need to expose `RowVersion`/ETag. Mention it as the follow-up.
5. **Test.** Only a real Postgres (Testcontainers, already in `ApiFactory`) produces `xmin` semantics. A mocked `IAppDbContext` can't throw the real exception from a real race, so it only tests your `catch`.

## Reference implementation sketch
- `ApiExceptionHandler`: add `DbUpdateConcurrencyException => Problem(409, "Conflict", "The conversation was changed by someone else. Reload and try again.")`. Log Warning for 4xx and Error only for the 500 branch.
- `LoggingBehavior`: `catch (Exception ex) when (ex is not ValidationException and not DbUpdateConcurrencyException)`. Or better, log 4xx-class exceptions at Information/Warning.
- Test: resolve two scopes from `_factory.Services`, load the same `Conversation` in both, `Close` in B and save, `Snooze` in A and save → assert it throws. For the HTTP-level test, the deterministic route is a small test-only endpoint or the scope approach. Two parallel HTTP calls are **flaky** (they usually serialize), so don't rely on them.
- Inbox: `changeState.onError` → show `error.message` and `invalidateQueries(["conversations"])`.

## Rubric
- Basic: returns 409 and has any test.
- Solid: + correct layer, correlationId kept, Warning level, deterministic test.
- Strong: + names the false-conflict writers and the 409-vs-412/ETag follow-up.
