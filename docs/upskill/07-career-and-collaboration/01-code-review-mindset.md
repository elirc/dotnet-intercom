# Code Review Mindset

## The five questions, in order

Review in this order — stop wasting attention on style if the thing doesn't work:

1. **Does it work?** Does the code do what the PR says? Run it / read the test.
2. **Is it correct?** Edge cases, concurrency, the second-time and same-time and on-failure paths.
3. **Will it stay correct?** Does it follow the patterns so the next person doesn't break it? Is it tested?
4. **Does it fit?** Right layer, right abstraction, consistent with the codebase's idioms.
5. **Is it kind to maintainers?** Readable, named well, no surprises, no swallowed errors.

Most junior reviews start at 5 and never reach 2. Senior reviews live at 2 and 3.

## What "correct" means *in this repo specifically*

The bugs here cluster in four places (the [critique](../03-architecture-and-patterns/06-architecture-critique.md) names them). Train your eye to ask:
- **Trust boundary:** does this trust a request-body field for identity/authorization? (R1, R3 came from missing this.)
- **Ownership:** does this new data access scope to the caller's resource — in the query? Does a new *hub method* repeat the REST ownership check? (R4 is a hub method that didn't.)
- **Concurrency:** read-modify-write without a lock? Notify before commit? (R5, R6.)
- **Time passing / failure:** what happens the second time, or when the notify fails? (R7, R9.)

## Repo review checklist

Paste into a review; tick what you verified.

- [ ] **Authorization** scoped in the query, not after load; hub methods match their REST siblings.
- [ ] No identity/authz decision reads a request-body field (claims only via [CurrentUser](../../../src/NetIntercom.Api/Infrastructure/CurrentUser.cs)).
- [ ] New command has a FluentValidation validator; existence checks throw `NotFoundException` (not FK-violation 500s).
- [ ] DTO returned matches the audience (visitor payloads don't leak agent/assignee fields).
- [ ] Side effects after commit, never before; notify failures can't fail the write.
- [ ] Follows the vertical-slice + `Dto.From` patterns; include-graph matches what the DTO dereferences.
- [ ] Uses `IClock`, not `DateTime.UtcNow` (the one exception, presence, is a known debt — don't add more).
- [ ] Tests added at the right layer ([testing strategy](../05-quality-engineering/01-testing-strategy.md)); existing 27 unit tests still green.
- [ ] No `.catch(() => {})` / empty `catch {}` that hides a real failure.
- [ ] Wire-contract changes (`Dtos.cs`) mirrored in `types.ts` / widget `api.ts`.

## Example comments (calibrated)

**Blocking, specific, kind:**
> Blocking: `Typing` here doesn't check the conversation belongs to the visitor, unlike `JoinConversation` two methods up. A visitor could spoof typing into any conversation by id. Can you add the same `AnyAsync(c.Id == id && c.VisitorId == VisitorId)` guard? Small change, but it's an auth gap so I can't approve without it.

**Important, with a reason and a path:**
> Important: this notifies before `SaveChangesAsync`, so if the save fails clients have already seen the message. Flip the order (save, then notify) — see how `SendAgentMessageHandler` does it.

**Optional, clearly labeled:**
> Optional/nit: this duplicates the message-append logic from the realtime handler — could extract `appendUnique`. Fine to defer.

**Pushing back on the requirement (sometimes the right review):**
> I don't think we can ship this even if it works — exposing internal-note counts to visitors defeats the note privacy model. Can we clarify the actual goal before iterating on the code?

## Anti-patterns in *giving* review

- **Nitpicking past a blocker** — naming a variable while missing the IDOR. Triage first.
- **"Why didn't you…"** — phrase as a question about the code, not the author.
- **Approving to be nice** — an approval is a claim you'd own this on call.
- **Vague negativity** — "this feels off" helps no one; point at the line and the consequence.

**Drill:** take any [good-first ticket](../06-contribution-practice/01-good-first-tickets.md), implement it slightly wrong (skip a test, or miss the team-existence check), and review your own diff using the checklist. Did the checklist catch your planted flaw?
**Self-grade:** Basic = applies the checklist. Solid = triages (blocking before nits) and writes one kind, actionable blocking comment. Strong = catches a trust-boundary or ownership gap unprompted and, when warranted, questions the requirement rather than the code.
