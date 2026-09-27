# On-ramp: warm-ups before the lab (≈ 2 h)

Do these before `training/`. Every exercise uses real code in this repo, and line numbers are for `main`. Write your answer down **before** checking `learn/_answers/00-onramp.md`. Only exercise 7 needs a terminal. The rest are reading and predicting.

---

### 1. Predict the conversation state machine
File: `src/NetIntercom.Domain/Entities/Conversation.cs`. A conversation has an `SlaPolicy` with `FirstResponseMinutes = 5` and `NextResponseMinutes = 10`. Starting from a new `Conversation`, these calls run in order (times in minutes from t0):
1. `RecordVisitorMessage(t0)`
2. `RecordAgentReply(t0+3)`
3. `RecordVisitorMessage(t0+4)`
4. `RecordVisitorMessage(t0+6)`
5. `Snooze(t0+60, t0+7)`
6. `RecordAgentReply(t0+8)`

After each step, give `State`, the number of timers in `SlaTimers` with each status, and `BreachAtUtc` of any running timer. Which step, if any, throws?

### 2. Predict `OfficeHours.IsOpenAt`
File: `src/NetIntercom.Domain/Entities/OfficeHours.cs:14-34`. What does `IsOpenAt` return for each of these?
- a) `Windows` is empty.
- b) `TimeZoneId = "Europe/Atlantis"` (it doesn't exist), one window Tuesday 09:00–17:00, instant Tuesday 12:00 UTC.
- c) One window **Friday 22:00 – 06:00** (a night shift), instant Friday 23:30 UTC, `TimeZoneId = "UTC"`.

For c), who in the business would notice, and where in `ConversationRouter` (`src/NetIntercom.Application/Routing/ConversationRouter.cs:40-51`) does the conversation go instead?

### 3. Predict `PlaceholderRenderer.Render`
File: `src/NetIntercom.Application/Common/PlaceholderRenderer.cs`. The values are `{ "visitor.name": "Pat", "agent.name": "" }`. Give the output of each template:
- a) `"Hi {{ visitor.name }}!"`
- b) `"I'm {{agent.name}}"`
- c) `"Ref {{visitor-name}}"`
- d) `"{{visitor.name}}{{visitor.name}}"`

Which of these would a customer actually see in the widget? Why might "leave unknown tokens intact" be a deliberate product choice, and when is it a bad one?

### 4. Trace a visitor's first message end to end
Start at `WidgetController.Send` (`src/NetIntercom.Api/Controllers/WidgetController.cs:39-46`) with `ConversationId = null`. List every method called, in order, down to the two SignalR sends. Then answer:
- a) At which line is the database write committed?
- b) `RealtimeNotifier.MessageCreatedAsync` sends to `vconv:{id}` (`src/NetIntercom.Api/Realtime/RealtimeNotifier.cs:25`). For a *brand-new* conversation, is the visitor's connection in that group yet? How does the widget still show its own message (`frontend/widget/src/Widget.tsx:80-90`)?
- c) Which step would be lost if the process crashed right after `SaveChangesAsync`? Does that matter here, and would it matter for S3's external pager?

### 5. 403 or 404? Read two ownership checks
Compare `GetVisitorMessagesHandler` (`src/NetIntercom.Application/Widget/WidgetQueries.cs:25-30`) with the conversation lookup in `SendVisitorMessageHandler` (`src/NetIntercom.Application/Widget/SendVisitorMessage.cs:55-62`).
- a) What does visitor A get back from each when passing visitor B's conversation id?
- b) Explain in plain words why the first handler does a *separate* `AnyAsync` instead of just adding `VisitorId` to the messages query.
- c) Which response is better for a security probe, and does the inconsistency matter?

### 6. Explain the schema: which index serves which query?
File: `src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs:72-97`. For each query below, name the index that should serve it, or say "none":
- a) The inbox list, filtered by `State` and sorted by `LastMessageAtUtc` (`src/NetIntercom.Application/Conversations/ListConversations.cs:35-38, 71`).
- b) The SLA sweep: `Status == Running && BreachAtUtc <= now` (`src/NetIntercom.Application/Sla/CheckSlaBreaches.cs:39`).
- c) The widget transcript: `ConversationId == id ORDER BY CreatedAtUtc` (`WidgetQueries.cs:32-39`).
- d) Inbox search: `Subject.ToLower().Contains(term)` (`ListConversations.cs:55-62`).

Then explain line 79 (`IsRowVersion()` on `RowVersion`). Which entity that is written concurrently has **no** such token? (Hint: incident 003.)

### 7. Write one characterization test (terminal needed)
Read [04 characterization tests](../../opusorganize/apprenticeship/curriculum/04-characterization-tests.md). Add **one** test to `tests/NetIntercom.UnitTests/RoutingAndOfficeHoursTests.cs` that pins the *current* behavior you predicted in exercise 2c, the overnight window. Name it so a future reader knows it describes today's behavior, not the desired one. Run `DOTNET_ROLL_FORWARD=Major dotnet test tests/NetIntercom.UnitTests` (Windows PowerShell: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test tests/NetIntercom.UnitTests`).
Exit: 28 tests, all green. Don't commit it to `main`: use a branch.

### 8. Read the widget's reconnect handler
File: `frontend/widget/src/Widget.tsx:34-63`.
- a) `withAutomaticReconnect()` reconnects after a Wi-Fi blip. Which SignalR groups is the *new* connection in, given `VisitorHub.OnConnectedAsync` (`src/NetIntercom.Api/Realtime/VisitorHub.cs:29-34`)?
- b) What value of `session.conversation` does the `onreconnected` callback see for a visitor who opened the page with no conversation and then sent their first message?
- c) Predict the user-visible symptom. (Ticket M3 goes much deeper. Here, just predict.)

### 9. Predict presence
File: `src/NetIntercom.Api/Realtime/PresenceTracker.cs:15-16, 76-90`. Give the status for agent Sara in each case:
- a) One connection open, last activity 2 min ago.
- b) One connection open, last activity 6 min ago.
- c) No connections, disconnected 30 s ago, last activity 1 min ago.
- d) No connections, disconnected 90 s ago.
- e) Sara is connected to API replica 1. The router runs on replica 2 (`ConversationRouter.cs:81`).

Which of these explains a symptom in incident 002? Don't open the incident answer: just say which case.

---
Sealed answers: [`_answers/00-onramp.md`](_answers/00-onramp.md)
