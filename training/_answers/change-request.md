# Answer: transfer system messages (reference placement)

## Placement
| Part | File | Why here |
|---|---|---|
| Who acted | `Api/Controllers/ConversationsController.cs:56-58` + `AssignConversationCommand` (add `ActorAgentUserId`) | The command doesn't know who transferred. `ICurrentUser` lives in Api, so pass it in like `SendAgentMessageCommand` does. |
| Decide "is this a transfer?" | `Domain/Entities/Conversation.cs` → make `Assign` return a `TransferKind` (None/Team/Agent), or add `Transfer(...)` | Domain rule: no message when nothing changed, when unassigning, or when the router auto-assigns at creation. Unit-testable without a DB. |
| Write the system message | `Application/Conversations/ManageConversation.cs` (`AssignConversationHandler`) | Same transaction as the assignment: one `SaveChangesAsync`. |
| Text | a small `SystemMessageText` helper in Application | Visitor-safe wording (team transfer = no agent name). One place to localize later. |
| Visitor push | `IRealtimeNotifier.MessageCreatedAsync` (already routes non-notes to `vconv:{id}`) | Reuse. A system message is not an internal note. |
| Toast to new assignee | new `IRealtimeNotifier.ConversationTransferredAsync` → `_agentHub.Clients.User(newAssigneeId.ToString())` | `AgentHub` already uses `Context.UserIdentifier` (the `sub` claim), so user targeting works without a new group. It also covers every tab the agent has open. |
| Rendering | `frontend/widget/src/Widget.tsx:124-130`, `frontend/inbox/src/pages/Inbox.tsx:291-305`, both `types` files | Both currently render everything non-Visitor as an agent bubble (widget) and everything non-Agent as a visitor bubble (inbox). |

## Data
`SenderType.System` already exists (`Enums.cs:14`) and is never written today. `Message` needs no new column: `AgentUserId` and `VisitorId` both null. An optional `SystemEventType` column is nice-to-have for localization and filtering. It would need the first migration (see ladder M1 §6). Doing without a column avoids blocking on migrations. Name that trade-off.

## Invariants that break (the core of the exercise)
- **Sender-name projections treat anything non-Agent as the visitor:** `GetConversation.cs:55-57` → a system line is shown as authored by the visitor. `StartConversation.cs:83-85` and `WidgetQueries.cs:38` → "You". Fix all three: `SenderType switch { Agent => …, Visitor => …, System => "System" }`.
- **`LastMessagePreview`/`LastMessageAtUtc`:** decide explicitly. Recommended: don't bump them, so a transfer doesn't reorder the inbox or become the list preview.
- **SLA:** don't call `RecordAgentReply` (a transfer isn't a reply). `RecordVisitorMessage` is unaffected.
- **Reports:** `Reports.cs:45-49, 60-66` filter `SenderType == Agent`, so they're safe. Add a test that proves it anyway.
- **Read receipts:** `MarkReadByVisitor` filters `SenderType == Agent`, so system lines never get marked. That's fine, but make sure the widget doesn't count them as unread.
- **Internal-note filters** (`!m.IsInternalNote`): system messages must have `IsInternalNote = false`, or visitors won't see them.

## Tests
- Unit: `Conversation.Assign` returns None for same agent, Team for a team-only change, Agent for an agent change, and None when unassigning.
- Integration: PUT assignee → the visitor's `/widget/conversations/{id}/messages` contains the system line with the team-only wording, and no agent name for team transfers. The agent transcript contains it with `senderType: System`. `/reports/overview` agent message counts are unchanged.
- Hub: the SignalR client for agent B receives `conversationTransferred` without having joined `conv:{id}`.
- Frontend: render tests for a System message in both apps.

## Push back / questions for the PM
- Should transfers between agents *within the same team* notify the visitor at all? (Noise, and it exposes agent churn.)
- Does unassigning back to the queue count as a transfer?
- Should the router's auto-assign at creation ever produce a line? (Recommended: no.)
- Privacy: is the agent's first name OK to show to visitors? (It already appears as `senderName` on replies, so yes, for consistency.)
