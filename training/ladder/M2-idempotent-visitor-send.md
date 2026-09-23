# M2: Visitors' messages appear twice on flaky mobile networks

**Rung:** mid · **Est.:** 1 day · **Skills:** idempotency, unique constraints, backward-compatible API change, retries

## Context
`POST /api/v1/widget/messages` (`Api/Controllers/WidgetController.cs:39-46` → `SendVisitorMessageHandler`, `Application/Widget/SendVisitorMessage.cs:47-115`) inserts a new `Message` on every call. Nothing identifies "the same message sent again". The widget clears the textbox *before* the request (`frontend/widget/src/Widget.tsx:80-90`) and has no error handling. Mobile browsers and corporate proxies retry POSTs that time out after the server has committed.

## Ticket
> Customers on phones sometimes see their message twice, and agents see two copies with two SLA timers. Sometimes the message vanishes from the box and never arrives. Make sends safe to retry.

## Constraints
- **Customer sites cache the widget bundle for up to 7 days.** For a week, old widgets that send no idempotency key must keep working exactly as today.
- New widgets send a client-generated `clientMessageId` (UUID). Retrying with the same id returns the **original** `{conversation, message}` with 200 and never creates a second row, a second SLA timer or a second realtime event.
- A second conversation must never be created for a retried *first* message. Here `conversationId` is still null on the retry.

## Definition of done
- Schema change with a uniqueness guarantee enforced by **PostgreSQL**, not only by a pre-check.
- Handler handles the concurrent-duplicate case (two requests in flight at once), not just the sequential one.
- Widget: keeps the text until the send succeeds, retries with the same `clientMessageId`, and shows a failed state after N attempts.
- Tests: a sequential duplicate, a concurrent duplicate (two `Task`s against Testcontainers Postgres), and a legacy request with no key.

## Explain before touching
1. List every side effect of one successful send: rows, `Conversation` fields, SLA timer, routing, round-robin cursor, SignalR events. Which of them are safe to repeat?
2. Where does the uniqueness constraint go: `(ConversationId, ClientMessageId)` or `(VisitorId, ClientMessageId)`? Use the "first message creates the conversation" case to decide.
3. Check-then-insert vs insert-then-catch-unique-violation: which one survives two concurrent requests? What does Npgsql throw, and how do you detect it without string matching?
4. The notifier runs after `SaveChangesAsync` (`SendVisitorMessage.cs:107-112`). On a replayed request, do you re-send the realtime event? Why or why not?
5. How long do you keep idempotency keys, and what does keeping them cost?

<details><summary>Hints</summary>

- `PostgresException.SqlState == "23505"` (`PostgresErrorCodes.UniqueViolation`).
- The docs in `docs/upskill/06-contribution-practice/02-mid-level-feature-tickets.md` (M4) sketch this too. Do your own design first, then compare.
</details>

Sealed answer: [`../_answers/ladder-M2.md`](../_answers/ladder-M2.md)
