# 20 technical questions about NetIntercom

Answer out loud first, then check [`../_answers/technical-questions.md`](../_answers/technical-questions.md).

## API design
1. Visitors write through `POST /api/v1/widget/messages`, and the hub only pushes. Why not let visitors send messages over `VisitorHub`? Give two concrete things you'd lose.
2. `POST /widget/messages` returns `{ conversation, message }` with the full `ConversationDto`. What's wrong with that for a visitor-facing API, and what would you return instead?
3. `GET /conversations/{id}/messages?after=` is used for catch-up. What guarantees does a timestamp cursor give, and where does it break in this code?
4. The widget bundle is cached on customer sites for days. How does that change the rules for evolving `/widget/*` compared with `/conversations/*`?

## Database
5. `Conversation.RowVersion` maps to `xmin`. What does it protect today, what doesn't it protect, and what happens when it fires?
6. The inbox sort `slaBreach` uses a correlated `MIN` over `SlaTimers`. Why is it slow at 400k conversations, and what are two fixes with different trade-offs?
7. Two visitors' widgets call `/widget/session` at the same instant with the same `anonymousId` (two tabs, first visit). Walk through what happens.
8. How would you add an index to `Messages` in production, given how this app creates its schema?

## Concurrency and jobs
9. Two Hangfire workers run `CheckSlaBreachesHandler` at the same moment. Exactly what happens to one overdue timer? (Don't take `docs/architecture-review.md` §2.3 at its word.)
10. `ConversationRouter.PickRoundRobinAsync` reads `LastAssignedAtUtc` and picks the minimum. Two conversations arrive together. What happens, does it matter, and how would you make it fair?
11. Why must a Hangfire recurring job id be a constant? What happens with 3 replicas if it isn't?

## Realtime and React
12. After `withAutomaticReconnect()` reconnects, which server-side state about that client is gone? How does that bite this widget?
13. `Inbox.tsx` registers `conn.on(...)` inside a `useEffect` with no cleanup. What happens after navigating Inbox → Reports → Inbox three times?
14. The inbox invalidates `["conversations"]` on every `messageCreated`. What does that cost with 25 agents, and what would you do instead?

## Security
15. Explain the `externalId` identity problem in `StartWidgetSession`, and the HMAC fix. What exactly does the host site compute, and with which secret?
16. `appsettings.json` has `"Seed": { "Enabled": true }`. Why is that a security bug and not just a config nit?
17. Why is the widget CORS policy (`SetIsOriginAllowed(_ => true)` + `AllowCredentials()`) not exploitable *today*, and what change would make it exploitable?

## Testing and operations
18. `HappyPathTests` asserts `teamName` is not null after a "billing invoice" message. Why so weak, and how would you make it deterministic?
19. The README says OpenTelemetry is configured. What would you check to decide whether traces reach anything, and what's missing for a production incident like 002?
20. You're paged for "unassigned ratio 11%". List the first five things you look at, in order, and what each rules in or out.
