# Writing PRs and RFCs

## What a good PR says

A reviewer should understand your change without reading the diff first. Structure:

- **What** — one sentence. "Add a snooze-wake job that reopens conversations at `SnoozedUntilUtc`."
- **Why** — the problem, linked to evidence. "Snoozed conversations currently never auto-reopen (risk R9); a snooze with no follow-up visitor message is lost."
- **How** — the approach + the one interesting decision. "New Hangfire recurring job → `WakeSnoozedConversationsCommand`, mirroring `SlaBreachJob`. Decided timers *pause* while snoozed (see note)."
- **How tested** — concretely. "Unit test with a fake clock; verified idempotent re-run. Integration: snooze→advance clock→assert reopened. `dotnet test` green."
- **Risks & rollback** — "Additive job; rollback = remove the `RecurringJob.AddOrUpdate`. Watch: mass-wake could storm agents."
- **Follow-ups** — explicitly scoped-out items. "Does *not* address the snooze/SLA-breach interaction beyond pausing; R9's notification piece is separate."

The scoped-out section is what marks a mid/senior PR — it proves you saw the bigger picture and chose a boundary on purpose.

## Commit messages

This repo has no history to match (synthetic), so set your own bar: imperative subject ≤ ~70 chars, a body explaining *why* when non-obvious.

```
Add team-existence check to SaveCannedReply

Team canned replies with a non-existent TeamId hit an FK violation and
surfaced as a 500. Throw NotFoundException so the API returns 404, matching
SaveRoutingRuleHandler. Closes the G8 inconsistency.
```

Don't: "fix stuff", "wip", "address review" (say what you addressed).

## Size and reviewability

- One logical change per PR. The [tickets](../06-contribution-practice/01-good-first-tickets.md) are deliberately sized for this.
- If a refactor and a feature are tangled, split them — refactor PR first (no behavior change, easy to approve), feature PR second.
- Ticket G3 (extract the loader) before M1 (tags) is exactly this discipline: land the safe refactor, then build on it.

## RFC template (repo-tailored)

Use for anything cross-cutting — the [senior projects](../06-contribution-practice/03-senior-build-projects.md), or kata 7 (fixing R1).

```markdown
# RFC: <title>

## Problem
What's broken/missing, with evidence (anchors, risk-register id, repro).

## Goals / Non-goals
What this does and explicitly does not solve.

## Proposal
The design. Diagrams/ASCII if it clarifies. Name the affected modules.

## Alternatives considered
At least two, with why-not. (Reviewers trust a proposal more when it shows the
roads not taken — see the comparison cards for the tone.)

## Impact
- API/contract changes (and backward compatibility)
- Schema changes (migration plan — remember this repo uses EnsureCreated today)
- Security implications
- Performance implications

## Rollout & rollback
Flag? Dual-write? Canary? How to revert without data loss.

## Test plan
What proves it works; which acceptance test is currently absent/failing.

## Open questions
The decisions you want input on.
```

## Worked mini-RFC (skeleton for R1)

> **Problem:** `StartWidgetSessionHandler` trusts `externalId` from an anonymous request body to resume identity ([StartConversation#L51-52](../../../src/NetIntercom.Application/Widget/StartConversation.cs#L51-L52)) — account/history takeover (R1).
> **Proposal:** host sites pass an HMAC-signed `externalId` (signed with a per-tenant secret); the server verifies before trusting it. Unsigned sessions are anonymous-only.
> **Alternatives:** full OIDC (too heavy for an embed); shared global secret (weaker tenant isolation).
> **Impact:** widget `session` request gains a signature field; existing anonymous flows unchanged (backward compatible).
> **Rollout:** verification behind a flag, warn-only → enforce.

**Drill:** finish that RFC to 1–2 pages (kata 7). **Self-grade:** Basic = PR has what/why/how-tested. Solid = risks + rollback + a scoped-out follow-up. Strong = your RFC's "alternatives considered" would survive a senior asking "why not X?" for the obvious X.
