# Maintainer Communication

> Simulated: there's no real maintainer here. Practice these as if there were — the habits transfer directly to real OSS and to your team.

## Ask without outsourcing your thinking

The difference between a question that earns help and one that annoys:

**Bad:** "How does routing work?"
**Good:** "I'm tracing routing in `ConversationRouter.RouteAsync`. I see it picks the first rule by priority, falls back outside office hours, then round-robins online members. What I can't tell from the code: is the round-robin race ([#L75-86](../../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L75-L86)) known/acceptable, or should a new contributor fix it? I'd lean toward `SKIP LOCKED` but don't want to gold-plate."

The good version shows you read the code, states what you concluded, and asks the *one* thing you genuinely can't determine. It respects the maintainer's time by doing yours first.

## Repro reports that get acted on

A bug report is a contract: give the maintainer everything to reproduce in under a minute.

```markdown
**What I expected:** Two online Support agents should each get ~half of new conversations.
**What happened:** One agent got all 5.
**Repro:**
1. Seed running, two agents online (admin + sara).
2. Fire 5 widget messages within ~1s (script attached).
3. All 5 assigned to admin.
**Environment:** local, EnsureCreated DB, single API instance.
**Where I think it is:** `PickRoundRobinAsync` reads `LastAssignedAtUtc` before the prior
assignment's SaveChanges commits — read-modify-write race (R5).
**Correlation ids:** abc123, def456 (from the 5 responses).
```

The "where I think it is" line is optional but turns a report into a collaboration.

## Minimal examples

When something's confusing, reduce it. For a domain question, a failing unit test *is* the minimal example:

```csharp
// "Should a snoozed conversation's timer breach? This test currently passes — is that intended?"
[Fact] public void Snoozed_Timer_StillBreaches() { /* ... R9 ... */ }
```

A 15-line test that demonstrates the question beats three paragraphs of prose. It's also half the fix.

## Respectful disagreement

You will disagree with a reviewer or maintainer. Do it well:

1. **Restate their point** so they know you understood. "You're saying cookie auth would be safer for the inbox — agreed for the inbox."
2. **State your constraint.** "But the widget is a third-party embed; cookies don't work cross-origin there, so we'd run two auth models."
3. **Propose a path.** "Could we move the *inbox* to cookies and keep visitor JWTs for the widget? That's comparison card 3's migration plan."
4. **Disagree-and-commit when overruled.** "I still mildly prefer X, but Y is reasonable — I'll go with Y."

Never make it about the person, never relitigate a settled decision, never cave on a *correctness* point (a security gap) just to avoid friction — escalate those calmly with evidence.

## When you're the one being asked

Reviewing a newcomer's PR is maintainer communication too: explain the *why*, link the pattern, offer to pair. "We keep ownership checks in the query — here's why (IDOR), here's an example (`GetVisitorMessagesHandler`). Happy to pair if the EF part is fiddly."

## Templates

**Asking for direction on scope:**
> Working on <ticket>. I can do the minimal fix (<X>) or the fuller version (<Y>) which also addresses <related risk>. The minimal is ~1 file; the fuller is ~4 + a migration. Which fits the project's appetite right now?

**Flagging a risk you found in passing:**
> Heads up — while doing <ticket> I noticed <risk> at <anchor>. Out of scope for this PR; want me to file it separately? I verified it by <evidence>, so it's not a guess.

**Drill:** take risk R4 (VisitorHub.Typing). Write (a) the repro report, (b) the one-question ask to a maintainer about whether to fix it now, and (c) the respectful-disagreement response if they say "leave it, it's low severity." **Self-grade:** Basic = a clear repro. Solid = the ask shows you did your homework first. Strong = your disagreement restates their point, holds the line on it being an auth gap, and still offers disagree-and-commit.
