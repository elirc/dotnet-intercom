# Fake Code Contrasts

Bad-vs-better pairs. The **better** side is how this repo actually does it (anchored); the **bad** side is invented (labeled illustrative) to sharpen the contrast. For each, name the principle before reading the "why."

---

## Contrast 1: DTO coupled to entity shape

**Bad:**
```csharp
// Illustrative: not from this repo
[HttpGet("{id:guid}")]
public Task<Conversation> Get(Guid id) => _db.Conversations.FindAsync(id); // returns the ENTITY
```
**Better (real):** [`GetConversationHandler`](../../../src/NetIntercom.Application/Conversations/GetConversation.cs#L12-L30) returns a `ConversationDto` via [`ConversationDto.From`](../../../src/NetIntercom.Application/Dtos.cs#L67-L77).
**Principle:** the wire contract is separate from the persistence shape. Returning the entity leaks navigation properties, lazy-loading triggers, EF cycles (Conversation→Messages→Conversation), and couples API consumers to your schema — rename a column, break every client.
**Spot it elsewhere:** any endpoint returning a `DbSet` type instead of a DTO.

---

## Contrast 2: Missing permission filter (IDOR)

**Bad:**
```csharp
// Illustrative: not from this repo
public async Task<IReadOnlyList<MessageDto>> Handle(GetVisitorMessagesQuery r, CancellationToken ct) =>
    await _db.Messages.Where(m => m.ConversationId == r.ConversationId)  // no visitor scope!
        .Select(m => MessageDto.From(m, "...")).ToListAsync(ct);
```
**Better (real):** [`GetVisitorMessagesHandler`](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L23-L40) checks `AnyAsync(c.Id == … && c.VisitorId == …)` first and throws `ForbiddenException` otherwise.
**Principle:** resource authorization belongs in the query. The conversation id is attacker-controlled; the visitor id comes from the token. The bad version is a textbook IDOR — any visitor reads any conversation by changing the id.
**Spot it elsewhere:** this is exactly the gap in [`VisitorHub.Typing`](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L48-L58) (risk R4) — the *real* code has this bug in one place. Find it without the hint next time.

---

## Contrast 3: N+1 query

**Bad:**
```csharp
// Illustrative: not from this repo
var conversations = await _db.Conversations.ToListAsync(ct);
foreach (var c in conversations)
    c.TeamName = (await _db.Teams.FindAsync(c.TeamId))?.Name; // one query PER conversation
```
**Better (real):** [`ListConversationsHandler`](../../../src/NetIntercom.Application/Conversations/ListConversations.cs#L28-L33) eager-loads `Team`/`Assignee`/`Visitor`/`SlaTimers` with `.Include`, so one SQL statement returns everything.
**Principle:** fetch related data in one round-trip, not in a loop. The bad version issues 1 + N queries; at 25 conversations that's 26 round-trips. Note this repo's *actual* failure mode if you forgot the Include would be a `NullReferenceException` (lazy loading is off), not silent N+1 — arguably louder and better.
**Spot it elsewhere:** any `await` inside a `foreach` over DB results.

---

## Contrast 4: Stale cache key / invalidation

**Bad:**
```tsx
// Illustrative: not from this repo
const { data } = useQuery({ queryKey: ["conversations"], queryFn: fetchOpen });
// ...elsewhere, after sending a message:
queryClient.invalidateQueries({ queryKey: ["conversation-list"] }); // KEY TYPO — never matches
```
**Better (real):** the inbox consistently keys on `["conversations", filter]` and invalidates `["conversations"]` (prefix match) on every relevant hub event ([Inbox.tsx#L26-L52](../../../frontend/inbox/src/pages/Inbox.tsx#L26-L52)).
**Principle:** query keys are stringly-typed contracts; a typo means silently stale UI with no error. TanStack prefix-matches, so invalidating `["conversations"]` catches every `["conversations", anyFilter]`.
**Spot it elsewhere:** the real subtlety here is *over*-invalidation — [Inbox.tsx#L73-L76](../../../frontend/inbox/src/pages/Inbox.tsx#L73-L76) invalidates *everything* on reconnect. Not a typo bug, a blast-radius one. Which is worse at scale?

---

## Contrast 5: Side effect in an unreliable place

**Bad:**
```csharp
// Illustrative: not from this repo
await _notifier.MessageCreatedAsync(dto, ct); // notify FIRST
await _db.SaveChangesAsync(ct);               // then save — if THIS throws, clients saw a ghost
```
**Better (real):** [`SendVisitorMessageHandler`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L107-L112) saves *then* notifies.
**Principle:** never broadcast state you haven't committed. The bad order shows clients a message that may not exist if the save fails. The repo's order is correct — though it still has the *opposite* milder problem (notify can fail a committed write, R6). Order-of-effects is a design decision, not an accident.
**Spot it elsewhere:** any `notify`/`publish`/`enqueue` that precedes its `commit`.

---

## Contrast 6: Swallowed exception

**Bad (real-ish — the repo actually does this on the client):**
```tsx
await connRef.current?.invoke("JoinConversation", id).catch(() => {}); // failure vanishes
```
This is **real** — [Widget.tsx#L88](../../../frontend/widget/src/Widget.tsx#L88) and several sibling `.catch(() => {})` calls.
**Better:**
```tsx
// Illustrative: not from this repo
await conn.invoke("JoinConversation", id).catch((e) => {
  console.warn("join failed; realtime degraded", e);
  scheduleRejoin(id); // or surface a "reconnecting" state
});
```
**Principle:** swallow only what you can prove is safe, and log even then. The real swallowed join is debugging scenario 4 — a silent failure that kills realtime for the whole session with no trace. Compare the *server* side, which logs before swallowing ([PresenceSweeper#L49-L52](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L49-L52)) — that's the right shape.
**Spot it elsewhere:** every `.catch(() => {})` and every empty C# `catch {}`.

---

## Contrast 7: Casual public-contract change

**Bad:**
```csharp
// Illustrative: not from this repo — "small cleanup", renames a wire field
public record MessageDto(Guid Id, Guid ConversationId, SenderType Kind /*was SenderType*/, ...);
```
**Better:** treat [`MessageDto`](../../../src/NetIntercom.Application/Dtos.cs#L25-L40) and its TS twin [types.ts#L52-L63](../../../frontend/inbox/src/types.ts#L52-L63) as a contract changed only deliberately.
**Principle:** the DTO is consumed by two hand-synced frontends and serialized camelCase. Renaming `senderType`→`kind` silently breaks both clients (TS compiles against `undefined`; the widget's `m.senderType` becomes `undefined` → every message renders as "from visitor"). There's no codegen to catch it.
**Spot it elsewhere:** any rename in `Dtos.cs` without the matching `types.ts` / `widget/src/api.ts` edit.

---

**How to use these:** cover the "Better" column, read only the bad code, and write (1) the principle, (2) where the real repo gets it right, (3) one place the real repo gets it *wrong* (contrasts 2 and 6 have real bugs). Self-grade Strong = you found the real bug before reading the hint.
