# Change request: "Conversation transferred" system messages plus a transfer notice to the new assignee

## Request (from the product manager)
> When an agent reassigns a chat to another agent or team, both sides should see it:
> - The **visitor** sees a grey system line in the widget: *"You've been transferred to the Billing team."* (No agent names for team transfers. For agent transfers: *"Alex will continue helping you."*)
> - The **new assignee** gets a toast: *"Sara transferred a conversation to you: <subject>"*, even if they're not viewing that conversation.
> - The transcript keeps the system line forever, in the agent view too.
> - Reports must **not** count system lines as agent messages or as SLA replies.

## Your job (before any agent writes code)
Write `training/navigation/MY_CHANGE_PLAN.md` with:
1. **Where each part goes:** list files and modules. For each: new or modified, and why *there* rather than elsewhere.
2. **Data changes:** is `SenderType.System` (`Domain/Enums.cs:14`) enough? Does `Message` need new columns? Which columns become nullable-meaningful? What's the migration story, given `EnsureCreated`?
3. **Realtime:** which events, to which groups, with which payloads? How does the new assignee get a toast when they're not in `conv:{id}`? (Today there is no per-agent group.)
4. **Invariants at risk:** list every query or handler that assumes a message is either Visitor or Agent. Check `SendAgentMessage`, `GetConversationMessagesHandler`'s sender-name projection, `WidgetQueries`, `StartConversation`, `Reports.cs`, `MarkRead*`, `Conversation.RecordVisitorMessage` and `RecordAgentReply`, and both frontends' rendering.
5. **Tests:** name the tests, their layer (unit / integration / frontend), and the assertion that proves each bullet of the request.
6. **What you'd push back on,** and the question you'd ask the PM.

Only after writing it: have an agent implement it (see `training/agentic/WORKFLOW.md`), then compare with [`../_answers/change-request.md`](../_answers/change-request.md).
