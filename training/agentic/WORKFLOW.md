# Agentic exercise: ship ladder M2 (idempotent visitor send) through an implement → adversarial-review loop

**Ticket:** [`../ladder/M2-idempotent-visitor-send.md`](../ladder/M2-idempotent-visitor-send.md). **Est.:** half a day. **You** write the spec, direct the agents, verify, and explain. The agents type.

## Step 0: set up (10 min)
```
git -C <repo> worktree add ../netintercom-m2 -b training/m2-idempotent-send main
```
Docker running (the integration tests use Testcontainers). Baseline: `dotnet test tests/NetIntercom.UnitTests` is green.

## Step 1: your spec and design note (45 min, no agent)
Write `training/agentic/M2-SPEC.md` (≤ 1 page):
- The behavior contract: what the same `clientMessageId` returns (status and body), what a keyless request does, and what happens under two concurrent requests.
- Schema: column, nullability, unique index (which columns and why), and how it gets deployed (this repo has no migrations: decide).
- Every side effect of `SendVisitorMessageHandler` (`Application/Widget/SendVisitorMessage.cs:47-115`), and whether a replay repeats it.
- Test list: names and assertions.
- Out of scope: say explicitly what the agent must not touch.

## Step 2: implementer agent
Prompt skeleton (paste your spec below it):
```
You are implementing a spec in the NetIntercom repo (.NET 8, EF Core/Npgsql, MediatR, React widget).
Work only in this worktree. Follow the spec exactly; if the spec is ambiguous, STOP and list questions
instead of guessing. Do not refactor unrelated code. Do not add packages. Run
`dotnet build` and `dotnet test tests/NetIntercom.UnitTests` and paste the real output.
List every file you changed and why. Mark anything you could not verify as UNVERIFIED.
<SPEC>
```

## Step 3: adversarial reviewer (a *different* model if you have one, or a fresh session)
```
You are reviewing a diff against a spec. Report ONLY: correctness, concurrency, security,
data integrity, API backward compatibility, test adequacy, failure handling.
No style or naming comments. For every finding: file:line, a concrete failing scenario
(inputs + interleaving), severity, and the smallest fix. Explicitly check:
1. Two identical requests in flight at once: one row, one conversation, one SLA timer?
2. First message (conversationId null) retried: is a second Conversation possible?
3. After a unique-violation, is the DbContext change tracker cleaned before re-querying?
4. Keyless (legacy widget) requests: byte-for-byte unchanged behavior?
5. Do the tests actually run concurrently against Postgres, or just sequentially?
6. Any fake/placeholder behaviour, swallowed exceptions, or TODOs presented as done?
<SPEC> <DIFF>
```

## Step 4: revision
Give the implementer the reviewer's findings **that you agree with**. Write down which ones you rejected and why. That list is part of your grade.

## Step 5: verify it yourself
Go through [`VERIFY_AI_OUTPUT.md`](VERIFY_AI_OUTPUT.md) line by line. No box gets ticked on the agent's word.

## Step 6: your explanation (graded)
Write `training/agentic/M2-EXPLANATION.md` without looking at the diff (max 400 words): the mechanism that guarantees one row under concurrency, what happens on the replayed first message, what the widget does differently, what you would monitor after release, and the one risk you accepted. Give it to the reviewer model with the rubric in [`../_answers/agentic-grading.md`](../_answers/agentic-grading.md) and ask for a score with reasons.
