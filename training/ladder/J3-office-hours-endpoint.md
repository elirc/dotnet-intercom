# J3: Admin endpoint to edit a team's office hours

**Rung:** junior · **Est.:** 3 h · **Skills:** REST design, authorization policies, validating time data, tests

## Context
Office hours only exist in the seeder (`Infrastructure/Persistence/DbSeeder.cs:26-36`). They're stored as JSON in `Team.OfficeHoursJson` (`Domain/Entities/Team.cs:13`) and read by `ConversationRouter.RouteAsync` (`Application/Routing/ConversationRouter.cs:40-48`). `OfficeHours.IsOpenAt` **silently treats an unknown time zone as UTC** (`Domain/Entities/OfficeHours.cs:21-29`).

## Ticket
> Billing moved to Manila time (`Asia/Manila`) and now works Saturday mornings too. Today that takes a developer editing the seeder. Give admins an API to set a team's office hours.

## Constraints
- `PUT /api/v1/teams/{id}/office-hours`, **Admin policy only**. Body: `{ timeZoneId, windows: [{ day, start, end }] }`.
- Reject an unknown `timeZoneId` (400). Reject `start >= end` (400). Overnight windows (22:00–06:00) are **not** supported in this ticket, and the validation message must say so.
- Empty `windows` means "always open". `IsOpenAt` already works that way, so keep it.
- 404 for an unknown team. 403 for a non-admin agent.

## Definition of done
- Command + validator + handler in `Application/Teams/`, a controller action, and integration tests for 200/400/403/404.
- A unit test proving the router falls back to the fallback team when the new hours say "closed". Use a fake `IClock`.

## Explain before touching
1. Which controller should own this route, and which authorization attribute makes it admin-only? Compare with `DirectoryController.SaveRoutingRule`.
2. `TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila")` works on Linux CI. Does it work on this Windows machine with .NET 8? What does that mean for validation and for tests?
3. What happens to conversations **already queued** when office hours change? Does anything re-route them? (Read `RouteAsync`: when is it called?)
4. Should the JSON column become real columns or tables? Give one argument each way.
5. What must the router test control so its result doesn't depend on when CI runs? Does the existing integration test depend on wall-clock time?

<details><summary>Hints</summary>

- On Windows, .NET 6+ converts IANA IDs via ICU when it's available. Check on your own machine instead of assuming.
- `HappyPathTests.VisitorToAgentConversationFlow` sends "billing invoice" and only asserts that `teamName` is not null. Why?
</details>

Sealed answer: [`../_answers/ladder-J3.md`](../_answers/ladder-J3.md)
