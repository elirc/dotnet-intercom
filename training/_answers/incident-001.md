# Answer: INC-001 internal note in the widget teaser

**Branch:** `training/incidents/001-widget-note-leak` · **Commit:** 8a0f386 · BUILD: verified (`dotnet build NetIntercom.sln`, 0 errors). Unit tests 27/27 pass on the branch, so the existing suite doesn't catch it.

## Root cause
Two individually reasonable changes combined:
1. `SendAgentMessageHandler` now writes `LastMessagePreview` for **internal notes** too (the preview update moved out of `if (!request.IsInternalNote)`).
2. `LastMessagePreview` was added to the **visitor-hub** payload in `RealtimeNotifier.ConversationUpdatedAsync`, and the widget renders it in the teaser.

Before this PR, the invariant "the preview only ever contains visitor-safe text" held implicitly because only public messages wrote it. Nothing enforced or documented it, and the PR broke it from both ends.

## All exposure paths (the answer must list more than the push)
- **Push:** `conversationUpdated` to `vconv:{id}` right after a note (`SendAgentMessage` calls `ConversationUpdatedAsync(ConversationDto.From(conversation))`).
- **Pull, session:** `POST /widget/session` returns the full `ConversationDto` (`StartConversation.cs:89-93`), which includes `LastMessagePreview`, so on every page load the teaser shows the note. That's how Pat saw it 20 minutes later.
- **Pull, send:** `POST /widget/messages` returns the full `ConversationDto` too (`WidgetController.cs:43-45`).
- Pre-existing and latent: those two REST paths already sent full agent-side DTOs to visitors (SLA deadlines, team name, `docs/architecture-review.md` §3.5). This incident is the moment that "harmless" over-sharing became a leak.

## Diagnostic path a senior takes
Screenshot → which UI element (the teaser, new in #212) → which field feeds it (`lastMessagePreview`) → who writes that field (grep: `SendAgentMessage`, `SendVisitorMessage`) → the note branch now writes it → every visitor-facing path that serializes it (hub payload + both REST DTOs). Confirm by replaying on the branch: send a note, open `/widget/session`.

## Containment
Roll back the deploy (safe: no schema change). A config flag wouldn't help, since there isn't one. Then **scrub the data**: rollback doesn't clear notes already stored in `LastMessagePreview`. Run `UPDATE "Conversations" SET "LastMessagePreview" = <last public message body, truncated>` for conversations whose latest message is a note. Otherwise the old build keeps serving the leaked preview through `/widget/session`.

## Fix
- Keep public activity and internal activity separate: either restore the original rule (notes don't touch the preview), or add `LastInternalActivityAtUtc` for inbox sorting if product really wants notes to bump the list.
- Introduce a `WidgetConversationDto` (id, state, assigneeName, and a *visitor-safe* preview computed from public messages only) and use it on **every** visitor path: the hub and both REST endpoints.
- Regression tests: (1) integration: agent posts a note → `/widget/session` and `/widget/messages` response JSON must not contain the note body **anywhere** (search the raw string, not one field); (2) unit: `SendAgentMessageHandler` with `IsInternalNote = true` leaves `LastMessagePreview` unchanged; (3) a serialization contract test that fails if a visitor DTO gains a property not on an allow-list.

## Exposure scope query
Conversations where a note was the latest message while the visitor loaded the widget or received a push:
```sql
SELECT DISTINCT m."ConversationId", c."VisitorId", m."CreatedAtUtc"
FROM "Messages" m JOIN "Conversations" c ON c."Id" = m."ConversationId"
WHERE m."IsInternalNote" AND m."CreatedAtUtc" >= '<deploy time>';
```
It over-approximates (a public reply may have overwritten the preview before the visitor looked). Cross-reference with `/widget/session` request logs by visitor. The logs lack visitor ids unless you add them. Note that as an observability gap.

## Postmortem skeleton
Impact (N visitors, internal notes containing PII/judgments exposed, ~18 h) · Timeline · Root cause (an implicit invariant on a shared field, enforced nowhere) · Why review missed it (the diff looked UI-only, and the note-branch change was framed as an inbox sort fix) · What went well (rollback was fast) · Actions: separate visitor DTOs (owner, date), a leak-detection integration test, a PR checklist item "does this field reach a visitor surface?", data scrub.
