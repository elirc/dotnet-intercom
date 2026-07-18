# File Reading Order

28 files in an order that compounds. Don't read alphabetically or by folder — read by *dependency of understanding*. For each: what to look for, and the question it should answer.

## Junior path (orient — read these first, in order)

| # | File | Look for | Answers |
| --- | --- | --- | --- |
| 1 | [README.md](../../../README.md) | the "How the interesting parts work" section | What does this product *do*? |
| 2 | [Directory.Build.props](../../../Directory.Build.props) | net8.0, nullable enable | What language/runtime assumptions hold everywhere? |
| 3 | [Enums.cs](../../../src/NetIntercom.Domain/Enums.cs) | the 5 enums | What states can things be in? |
| 4 | [Entity.cs](../../../src/NetIntercom.Domain/Common/Entity.cs) | `Entity`, `AuditableEntity`, `ISoftDeletable` | What does every entity share? |
| 5 | [Conversation.cs](../../../src/NetIntercom.Domain/Entities/Conversation.cs) | private setters + the 5 transition methods | How does a conversation change state legally? |
| 6 | [Sla.cs](../../../src/NetIntercom.Domain/Entities/Sla.cs) | `SlaTimer.Start/MarkMet/MarkBreached/Cancel` | What is an SLA timer's lifecycle? |
| 7 | [Message.cs](../../../src/NetIntercom.Domain/Entities/Message.cs) | `IsInternalNote`, sender fields | How is a note different from a reply? |
| 8 | [Dtos.cs](../../../src/NetIntercom.Application/Dtos.cs) | the `From` factories, computed fields on `ConversationDto` | What does the wire actually carry? |
| 9 | [WidgetController.cs](../../../src/NetIntercom.Api/Controllers/WidgetController.cs) | claim-derived `VisitorId`, attributes | What can a visitor call? |
| 10 | [ConversationsController.cs](../../../src/NetIntercom.Api/Controllers/ConversationsController.cs) | one-line dispatch to MediatR | What can an agent call? |
| 11 | [SendVisitorMessage.cs](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs) | record + validator + handler in one file | What happens on the busiest write path? |
| 12 | [Login.tsx](../../../frontend/inbox/src/pages/Login.tsx) | zod schema, `useAuth().login` | How does an agent get in? |

## Mid-level path (mechanisms)

| # | File | Look for | Answers |
| --- | --- | --- | --- |
| 13 | [Interfaces.cs](../../../src/NetIntercom.Application/Common/Interfaces.cs) | every port on one page | What are the seams? |
| 14 | [Behaviors.cs](../../../src/NetIntercom.Application/Common/Behaviors.cs) | validation + logging decorators | How do cross-cutting concerns run? |
| 15 | [ConversationRouter.cs](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs) | rule match → office hours → fallback → round-robin | How is a conversation assigned? |
| 16 | [AppDbContext.cs](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs) | indexes, `SaveChangesAsync` override, query filter | How is data shaped and audited? |
| 17 | [Program.cs](../../../src/NetIntercom.Api/Program.cs) | auth, CORS, rate limits, pipeline order, Hangfire | How is the app wired together? |
| 18 | [TokenService.cs](../../../src/NetIntercom.Infrastructure/Auth/TokenService.cs) | agent vs visitor claims, refresh hashing | What's in a token? |
| 19 | [Refresh.cs](../../../src/NetIntercom.Application/Auth/Refresh.cs) | rotation (revoke + reissue) | How do sessions stay alive? |
| 20 | [api.ts](../../../frontend/inbox/src/api.ts) | one-shot 401 refresh, token storage | How does the client talk to the API? |
| 21 | [realtime.ts](../../../frontend/inbox/src/realtime.ts) | singleton hub, auto-reconnect, heartbeats | How does realtime connect? |
| 22 | [Inbox.tsx](../../../frontend/inbox/src/pages/Inbox.tsx) | TanStack queries + hub event → invalidate | How does the inbox stay live? |
| 23 | [RealtimeNotifier.cs](../../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs) | who hears each event; note privacy | How are events fanned out? |

## Senior path (judgment)

| # | File | Look for | Answers |
| --- | --- | --- | --- |
| 24 | [PresenceTracker.cs](../../../src/NetIntercom.Api/Realtime/PresenceTracker.cs) | `Compute`, grace periods, concurrent dictionaries | What breaks at 2 instances? |
| 25 | [PresenceSweeper.cs](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs) | scope-per-sweep, diff-and-broadcast | How is a singleton's DB access made safe? |
| 26 | [CheckSlaBreaches.cs](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs) | the Include-heavy scan, notify-after-save | What's the cost and reliability of the job? |
| 27 | [StartConversation.cs](../../../src/NetIntercom.Application/Widget/StartConversation.cs) | `externalId` lookup from the body | Where is the trust boundary? (risk R1) |
| 28 | [HappyPathTests.cs](../../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs) | the whole product as one assertion chain | What is "working" defined as? |

**After file 28** you have the spine of the system. Everything not listed (ManageConversation, Reports, CannedReplies, Teams admin, the widget's `Widget.tsx`) follows the same patterns you've now seen — read them when a ticket sends you there.
