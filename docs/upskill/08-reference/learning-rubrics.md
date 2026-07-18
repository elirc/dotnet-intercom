# Learning Rubrics

Observable behaviors, not feelings. You're at a level when you *do* these things on this codebase without prompting — grade yourself by evidence (a PR, a review, an explanation), not by "I think I get it."

## Junior — "I can make it work safely"

You can:
- [ ] Run the build + unit tests and read the results ([fast track](../00-fast-track.md)).
- [ ] Trace one feature end to end and name the layers ([key flows](../01-codebase-cartography/05-key-flows.md)).
- [ ] Find where a thing lives using the [system map](../01-codebase-cartography/01-system-map.md) + [glossary](../01-codebase-cartography/03-domain-glossary.md).
- [ ] Make a small change following an existing pattern, with a test ([good-first tickets](../06-contribution-practice/01-good-first-tickets.md)).
- [ ] Explain why a DTO differs from an entity, and why validation runs in a behavior.
- [ ] Write a PR with what/why/how-tested.

**You're past junior when:** you stop asking "where do I put this?" because the vertical-slice pattern answers it for you, and you instinctively add a test.

## Mid-level — "I can design a cross-layer change and reason about correctness"

You can:
- [ ] Implement a feature touching schema + API + UI + tests, with a migration plan ([mid tickets](../06-contribution-practice/02-mid-level-feature-tickets.md)).
- [ ] Spot the four repo failure-classes in review: trust boundary, ownership, concurrency, time/failure ([review mindset](../07-career-and-collaboration/01-code-review-mindset.md)).
- [ ] Debug with the loop (reproduce→narrow→hypothesize→test→root-cause→regress) and use correlation ids / EF logging ([debugging](../05-quality-engineering/03-systematic-debugging.md)).
- [ ] Place a test at the right layer and explain why EF logic is integration-tested, not unit-mocked.
- [ ] Read a query and predict its SQL shape and index usage ([data model](../03-architecture-and-patterns/02-data-model-and-persistence.md)).
- [ ] Find an IDOR/ownership gap and write the fix + test.
- [ ] Write a PR that names its scoped-out follow-ups.

**You're past mid when:** you catch the *second-time/same-time/on-failure* bug in review before it ships, and your design notes answer the open question instead of dodging it.

## Senior — "I can own consequences"

You can:
- [ ] Critique the architecture with evidence and tier risks by launch-impact ([critique](../03-architecture-and-patterns/06-architecture-critique.md)).
- [ ] Argue both sides of a design decision and state what each commits the team to ([comparison cards](../03-architecture-and-patterns/07-comparative-implementations.md)).
- [ ] Write a migration plan that's safe under a rolling deploy, with rollback ([senior projects](../06-contribution-practice/03-senior-build-projects.md)).
- [ ] Identify what makes the system single-node and sequence the work to fix it.
- [ ] Reason about a failure's blast radius and the observability gap that would hide it ([observability](../05-quality-engineering/06-observability-and-operations.md)).
- [ ] Refuse a premature optimization with a measurement argument ([performance](../05-quality-engineering/04-performance-thinking.md), [review kata 4](../04-code-reading-gym/04-review-katas.md)).
- [ ] Disagree with a reviewer well, and push back on a *requirement* when it's wrong.
- [ ] Write an RFC whose "alternatives considered" survives "why not X?"

**You're operating as senior when:** you name the switch signal for a decision rather than declaring one option universally right, and you optimize for the next reader and the on-call engineer, not just the compiler.

## Self-assessment protocol

1. Don't self-rate on a checkbox you haven't *demonstrated* this month. "I could" ≠ "I did."
2. Re-grade monthly. The fastest growth signal: items that move from "with the hint" to "unprompted."
3. The honest test for each level: could you produce the artifact (PR, review, RFC) and defend it to someone who'd own the consequence? If you'd hedge, you're not there yet — and that's fine, that's the next rep.

## Mapping levels to this suite

| Level | Spend most time in |
| --- | --- |
| Junior | 00, 01, 02, 04/01, 06/01 |
| Mid | 03/01-05, 04, 05, 06/02, 07/01-02 |
| Senior | 03/06-07, 05/04+06, 06/03-04, 07/03-04, 08/risk-register |
