SEALED: attempt first. Don't read this until you've written your own answer to every exercise in `learn/00-onramp.md`.

# On-ramp answers

Line numbers are for `main`. I verified them by reading the source in B08, but ran nothing. For exercise 7, the proof is your own test run.

## 1. Conversation state machine
| Step | State | Timers | Running timer's `BreachAtUtc` | Why (`Conversation.cs`) |
|---|---|---|---|---|
| 1 `RecordVisitorMessage(t0)` | Open | 1 Running (FirstResponse) | t0+5 | `:103-127`: `FirstAgentReplyAtUtc` is null, so FirstResponse, 5 min |
| 2 `RecordAgentReply(t0+3)` | Open | 1 Met | — | `:87-100`: stamps `FirstAgentReplyAtUtc = t0+3`, `MarkMet` |
| 3 `RecordVisitorMessage(t0+4)` | Open | 1 Met, 1 Running (NextResponse) | t0+14 | first reply exists, so NextResponse, 10 min |
| 4 `RecordVisitorMessage(t0+6)` | Open | unchanged | t0+14 | `:115-119`: a running timer exists, so it doesn't stack |
| 5 `Snooze(t0+60, t0+7)` | Snoozed | unchanged | t0+14 | `:63-77`: the timer keeps running while snoozed |
| 6 `RecordAgentReply(t0+8)` | **Open** | 2 Met | — | `:95-99`: a reply to a snoozed conversation reopens it. `FirstAgentReplyAtUtc` stays t0+3 (`??=`) |

Nothing throws.

Two things worth noticing:
- **The `SlaPolicy` navigation must be loaded.** `RecordVisitorMessage` checks the navigation `SlaPolicy`, not `SlaPolicyId` (`:110`). If a caller forgot `.Include(c => c.SlaPolicy)`, no timer would ever start, silently. That's why `SendVisitorMessage.cs:59` includes it and `:84-88` loads it explicitly for new conversations.
- **`SlaTimer.MarkMet` never checks `BreachAtUtc`** (`Sla.cs:31-40`). A reply at t0+20 would still count as "Met" if the sweep hadn't run yet. That's the latent bug behind incident 003's "40% jump".

## 2. `OfficeHours.IsOpenAt`
- a) **true.** `:16-19`: no windows means always open.
- b) **true.** `FindSystemTimeZoneById` throws `TimeZoneNotFoundException`, the code falls back to UTC silently (`:26-29`), and 12:00 UTC Tuesday is inside the window. The danger is that a typo in the time zone gives *plausible* wrong answers and nothing logs it. (An `InvalidTimeZoneException` isn't caught at all.)
- c) **false**, at every instant. `time >= 22:00 && time < 06:00` can never be true (`:33`). An overnight window has to be stored as two windows (Fri 22:00–24:00, Sat 00:00–06:00) or the check has to handle `End < Start`.
  - Effect: the night team is always "outside office hours", so `ConversationRouter` logs at Information and falls back (`ConversationRouter.cs:43-47`) to the fallback team (`:50-51`).
  - Who notices: the night-shift lead ("our queue is always empty") and the fallback team, which is overloaded at night. There's no alert, just an Information log. Ticket J3 (office-hours endpoint) should validate or split such windows.

## 3. `PlaceholderRenderer.Render`
- a) `"Hi Pat!"`. The regex tolerates whitespace inside the braces: `\{\{\s*([\w.]+)\s*\}\}`.
- b) `"I'm {{agent.name}}"`. An empty value leaves the token intact (`:16-18`).
- c) `"Ref {{visitor-name}}"`. `-` isn't in `[\w.]`, so the regex doesn't match at all and the text is untouched.
- d) `"PatPat"`.

Render is called by `RenderCannedReplyHandler` (`src/NetIntercom.Application/CannedReplies/CannedReplies.cs:143`), which returns the text **to the agent**. The agent then sends it. A customer sees raw braces (b, c) only if the agent doesn't read the composer before hitting send.

Leaving tokens intact is a good choice when a human previews the text: a visible `{{agent.name}}` is better than a silent `"I'm "`. It's a bad choice for any automated send (auto-replies, S3 pager messages), where you want to fail or fall back loudly. Note that `visitor.name` is nullable (`Visitor.Name`), so anonymous visitors will hit this often.

## 4. Visitor's first message, end to end
1. `WidgetController.Send` (`WidgetController.cs:39-46`)
2. `ISender.Send` → `ValidationBehavior` → `LoggingBehavior` (`Application/Common/Behaviors.cs:7, 34`)
3. `SendVisitorMessageHandler.Handle` (`SendVisitorMessage.cs:47`): loads the visitor (`:49`), creates a new `Conversation` (`:72-82`), then:
   - `ConversationRouter.RouteAsync` (`:83`): the rules query, office hours, fallback team, `PickRoundRobinAsync` using in-memory presence (`ConversationRouter.cs:31-86`)
   - loads `SlaPolicy` (`:84-88`)
   - adds the `Message` (`:91-100`)
   - `RecordVisitorMessage` (`:102`)
   - `SaveChangesAsync` (`:107`)
4. `RealtimeNotifier.MessageCreatedAsync` (`:111`) sends to group `agents` and group `vconv:{id}`.
5. `ConversationUpdatedAsync` (`:112`).

- a) **`SendVisitorMessage.cs:107`.** It's one `SaveChangesAsync`, so the conversation, the message, the timer and the round-robin `LastAssignedAtUtc` commit together.
- b) **No.** The widget joins `vconv:{id}` only after the HTTP response comes back (`Widget.tsx:88`), so the push to the visitor group reaches nobody. The widget shows its own message from the HTTP response (`addMessage(result.message)`, `Widget.tsx:85`). That's fine here, but it's the pattern that makes "the push is delivery only, the DB is the truth" load-bearing.
- c) **The two SignalR pushes are lost.** The data is safe. The inbox recovers on its 30 s poll (`Inbox.tsx:32`) or on the next event. That's acceptable for a UI hint. For S3's external pager, a lost notification is a missed page, so you'd need a transactional outbox ([01](../../../opusorganize/apprenticeship/curriculum/01-transactional-outbox.md)).

## 5. 403 vs 404
- a) `GetVisitorMessagesHandler` throws `ForbiddenException`, which becomes **403** (`ApiExceptionHandler.cs:29`). `SendVisitorMessageHandler` folds ownership into the lookup (`c.VisitorId == visitor.Id`, `:61`) and throws `NotFoundException`, which becomes **404** (`:62`, `ApiExceptionHandler.cs:28`).
- b) Authorization answers "may **this caller** read **this conversation**?" A filter on messages answers a different question, "which rows match this predicate?", and returns `[]` for a foreign conversation, which looks the same as "empty conversation". An explicit check can't be quietly weakened by someone rewriting the query. Incident 004 is exactly that: a filter that looked stricter but wasn't equivalent.
- c) Either is defensible if it's reasoned:
  - 404 doesn't confirm that the id exists (anti-enumeration).
  - 403 gives you a clean signal to alert on.
  - With random GUIDs, enumeration risk is low.
  - The **inconsistency** does matter. INC-004's "403s dropped to 0" alert covers only one route, and the negative-test matrix has to encode per-endpoint expectations. A good answer picks one convention and puts it in the matrix.

## 6. Index per query
- a) `(State, LastMessageAtUtc)` (`AppDbContext.cs:72`). A btree can be scanned backwards for `DESC`. If there's no `State` filter, the index helps much less.
- b) `(Status, BreachAtUtc)` (`:97`). That's an equality then a range, which is the right column order.
- c) `(ConversationId, CreatedAtUtc)` (`:88`).
- d) **None.** `ToLower().Contains(term)` becomes `lower(x) LIKE '%term%'`, a sequential scan, across a join to `Visitors`. It needs a `pg_trgm` expression index ([10](../../../opusorganize/apprenticeship/curriculum/10-query-plans-and-indexes.md)).

Line 79: `RowVersion` maps to Postgres's `xmin` system column as an optimistic concurrency token. EF adds `WHERE xmin = @original` to `UPDATE "Conversations"`. If 0 rows are affected, it throws `DbUpdateConcurrencyException`. Ticket J1 turns that into a 409.

**`SlaTimer` has no token.** Two SLA sweeps, or a sweep and an agent reply, can both update the same timer, and the last writer wins silently. That's incident 003, and it's why `docs/architecture-review.md` §2.3 is wrong.

## 7. Reference characterization test
```csharp
[Fact]
public void Characterization_OvernightWindow_IsNeverOpen_CurrentBehavior()
{
    // Pins TODAY's behavior: a window whose End < Start never matches (OfficeHours.cs:33).
    // If you intentionally support overnight windows, change this test in the same PR.
    var hours = new OfficeHours
    {
        TimeZoneId = "UTC",
        Windows = { new() { Day = DayOfWeek.Friday, Start = new TimeOnly(22, 0), End = new TimeOnly(6, 0) } }
    };
    // 2026-06-12 is a Friday (the existing tests use Tue 2026-06-09).
    hours.IsOpenAt(new DateTime(2026, 6, 12, 23, 30, 0, DateTimeKind.Utc)).Should().BeFalse();
}
```
Grading:
- The name says *current* behavior, and the comment points at the line.
- The date really is a Friday.
- The test asserts `false` (what the code does), **not** `true` (what the business wants). A characterization test that asserts the desired behavior is a bug report, not a characterization.

The expected count is 27 + 1 = 28 (B01 verified 27/27 on `main`).

## 8. Widget reconnect
- a) Only `"visitors"` (`VisitorHub.cs:31`). A reconnect gets a **new connection id**, and group membership belongs to the old one. `vconv:{id}` is gone.
- b) **null.** The callback closed over the bootstrap `session` object (`Widget.tsx:52-58`), and `send()` never updates `session`. So there's no catch-up at all for this visitor.
- c) After any Wi-Fi blip or laptop sleep, the agent's replies stop appearing until the page is reloaded. Returning visitors get one refetch on reconnect and then nothing more. See `training/_answers/ladder-M3.md` for the full analysis and fix.

## 9. Presence
- a) **Online.**
- b) **Away.** Idle for more than 5 min (`:89`).
- c) **Online.** It's within the 60 s disconnect grace (`:84`), and the activity was less than 5 min ago.
- d) **Offline.**
- e) **Offline, as far as replica 2 is concerned.** `_agents` is a per-process dictionary, so the router on replica 2 sees nobody online and queues the conversation unassigned. That's ticket S1's problem.

Incident 002 is about case **c**: the grace period. What breaks it is in the incident answer.
