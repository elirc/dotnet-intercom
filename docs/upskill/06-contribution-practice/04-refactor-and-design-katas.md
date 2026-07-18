# Refactor and Design Katas

Self-contained exercises, each self-graded. Do them on a scratch branch; the point is the reasoning, not a merge.

---

## Kata 1: Fix a boundary leak

**Task:** close encapsulation leak #2 from [boundaries](../03-architecture-and-patterns/01-boundaries-and-layers.md). Make `Conversation.TeamId` and `AssigneeId` `private set`; add `AssignToTeam(teamId)` / clear methods; update every handler/router that currently sets them directly ([ConversationRouter#L59-66](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L59-L66), [SendVisitorMessage](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs)).
**Self-grade:** Basic = compiles, tests pass. Solid = no behavior change, the new methods enforce the same closed-conversation guard as `Assign` ([Conversation#L36-48](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L36-L48)). Strong = you measured the blast radius (count of changed files) and made a *recommendation* on whether it's worth it, not just did it.

## Kata 2: Add an outbox (design only)

**Task:** sketch Project S2's outbox on paper — the table, where the insert hooks into [SaveChangesAsync](../../../src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs#L123-L149), the dispatcher, and how per-conversation message ordering is preserved.
**Self-grade:** Basic = table + dispatcher named. Solid = the insert is in the same transaction as the domain write; consumers are idempotent. Strong = you addressed ordering and the dual-write→cutover→rollback path.

## Kata 3: Split a module

**Task:** [`TeamsAndAdmin.cs`](../../../src/NetIntercom.Application/Teams/TeamsAndAdmin.cs) bundles teams, agents, SLA policies, and routing rules into one file. Split it into cohesive slices ([Pattern 1](../03-architecture-and-patterns/05-pattern-catalog.md) says one use-case-area per file).
**Self-grade:** Basic = files split, build green. Solid = the split is by *cohesion* (routing rules together, SLA policies together), not arbitrary. Strong = you noticed which handlers share the `IPresenceService` dependency and grouped sensibly, and you left a one-line comment explaining the boundary.

## Kata 4: Type-safety improvement

**Task:** the C# DTOs and [types.ts](../../../frontend/inbox/src/types.ts) are hand-synced ([type-system doc](../02-stack-and-language-mastery/03-type-system-and-contracts.md)). Pick the highest-value safety improvement: (a) generate TS from the OpenAPI Swagger doc, or (b) add a compile-time check that the shapes match. Sketch the chosen approach and justify it against the current scale.
**Self-grade:** Basic = picks an approach. Solid = justifies it (codegen's maintenance cost vs the drift risk at ~8 DTOs). Strong = names the switch signal — at what DTO count or team size hand-sync stops being acceptable — rather than reflexively adding tooling.

## Kata 5: Design a migration (offset → keyset)

**Task:** Ticket M5 as a design doc. Write the migration plan to move the inbox list from offset to cursor pagination *without breaking the running inbox client*, including the dual-support window.
**Self-grade:** Basic = describes the cursor encoding. Solid = the API supports both `page` and cursor during transition; the index is reused. Strong = client and server cutover are sequenced so neither breaks the other mid-deploy, with a rollback that doesn't lose the cursor contract.

## Kata 6: Reduce an N+1 / heavy query

**Task:** the breach job loads four navigation graphs per overdue timer just to build a notification DTO ([CheckSlaBreaches#L34-40](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L34-L40)). Rewrite it to project only the fields `slaBreached` needs ([RealtimeNotifier.SlaBreachedAsync](../../../src/NetIntercom.Api/Realtime/RealtimeNotifier.cs#L40-L41) + the `ConversationDto` fields actually used by the client).
**Self-grade:** Basic = fewer Includes. Solid = a projection that materializes only needed columns, behavior unchanged, test still green. Strong = you measured (EF query log before/after) rather than assuming, and you noted whether this is even worth doing at current volume.

## Kata 7: Write an RFC

**Task:** write a 1–2 page RFC for **fixing identity spoofing (R1)** — host-signed visitor identity. Use the [RFC template](../07-career-and-collaboration/02-writing-prs-and-rfcs.md). It must cover: the threat, the proposed HMAC-signed `externalId` scheme, the API/contract change, backward compatibility for unsigned (anonymous-only) sessions, and the rollout.
**Self-grade:** Basic = problem + proposal stated. Solid = backward compatibility and rollout addressed; the contract change is precise. Strong = you considered alternatives (full OIDC, per-tenant secrets vs shared) and said why the chosen one fits *this* repo, plus the migration for existing visitors.

## Kata 8: Review a flawed PR

**Task:** do [review kata 5](../04-code-reading-gym/04-review-katas.md) ("internal-note count in widget") as a written review, then go further: the author pushes back ("the PM asked for it"). Write your second response — escalating to a requirement clarification, not a code argument.
**Self-grade:** Basic = identified the privacy violation. Solid = the review comment is kind and actionable. Strong = your second response redirects to *what the PM actually needs* and offers an agent-side alternative, modeling [respectful disagreement](../07-career-and-collaboration/03-maintainer-communication.md) without caving on the privacy line.

---

**Meta-rubric:** across all eight, Strong consistently means you (1) measured or scoped rather than assumed, (2) named a switch signal or trade-off rather than declaring one option universally right, and (3) considered the change's effect on a *reader* and an *operator*, not just the compiler.
