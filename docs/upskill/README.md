# NetIntercom Training Lab

A learning curriculum built **on top of this specific repository**. It is not a generic .NET course: every concept is taught through a file you can open, every exercise targets code that actually exists, and every claim about the codebase is anchored to a symbol and line range that was read when these docs were written.

## What this repo is (and isn't)

NetIntercom is an Intercom-style two-sided live chat: an embeddable **visitor widget** (`frontend/widget`, a single IIFE bundle) talks to a .NET 8 API which routes conversations to support teams; agents work them in a React **inbox** (`frontend/inbox`). The backend is a 4-project layered solution (Api → Application → Domain ← Infrastructure) using MediatR, FluentValidation, EF Core on PostgreSQL, SignalR for realtime, Hangfire for the SLA-breach job, and JWT auth with rotated refresh tokens. The interesting domain machinery — SLA timers, keyword routing with office hours, round-robin assignment, presence with grace periods — lives in small, readable classes and is covered by 27 unit tests plus a Testcontainers happy-path suite.

**Provenance verdict: this is a synthetic, AI-generated repository** (no git history, all files written the same day, perfectly uniform style — full evidence in [verification-log.md](08-reference/verification-log.md)). That changes how you should use it: it is a *training lab*, not a living open-source project. Modules 06 and 07 therefore simulate the contributor workflow — you role-play the contributor, the docs encode what a maintainer would demand. It also means the repo looks cleaner than it is: synthetic codebases reliably hide missing ownership checks, races, and untested error paths, and this one is no exception — the [risk register](08-reference/risk-register.md) and [architecture critique](03-architecture-and-patterns/06-architecture-critique.md) name the ones found with evidence.

## The mindset ladder this curriculum climbs

- **Junior** asks: *how do I make it work?*
- **Mid-level** asks: *is this the right pattern? will it stay correct?*
- **Senior** asks: *what does this choice commit us to, who pays for it, and how do we reduce the risk?*

Module 03/07 ([comparative implementations](03-architecture-and-patterns/07-comparative-implementations.md)) exists specifically to train the third question — it takes five real decisions in this repo and forces you to argue both sides.

## Conventions (read before anything else)

1. **Anchors.** Codebase claims look like [`RouteAsync`](../../src/NetIntercom.Application/Routing/ConversationRouter.cs#L29-L73) — symbol name + line range. There is **no pinned commit** because this repo has no git history (see log). Line numbers refer to the files as of 2026-06-09. **Do this first:** `git init && git add -A && git commit -m "training-lab baseline"` inside the repo, so anchors stay diff-able as you experiment.
2. **Verified vs inferred.** Commands marked *verified* were actually run while writing these docs, with output recorded in the [verification log](08-reference/verification-log.md). Commands marked *inferred* were not (usually because Docker was down). Treat inferred commands as probably-right, not gospel.
3. **Illustrative code.** Any code block labeled `// Illustrative: not from this repo` was invented to make a contrast. Unlabeled excerpts are real.
4. **Drills and self-grading.** Most pages end with a drill and a Basic/Solid/Strong rubric. The drills are the curriculum; reading without doing them produces familiarity, not skill.
5. **Verification notes.** Pages that make risky claims carry a short "Verification notes" section separating *confirmed by reading/running* from *hypothesis*.

## Suite map

| Module | What it gives you |
| --- | --- |
| [00-fast-track.md](00-fast-track.md) | One weekend: run it, trace two flows, make one safe change |
| [01-codebase-cartography/](01-codebase-cartography/README.md) | System map, reading order, glossary, runtime map, **5 key end-to-end flows** |
| [02-stack-and-language-mastery/](02-stack-and-language-mastery/README.md) | CLR/async/EF/ASP.NET/React mental models, each pointed at a real file here |
| [03-architecture-and-patterns/](03-architecture-and-patterns/README.md) | Boundaries, persistence, auth, reliability, **pattern catalog**, critique, **comparison cards** |
| [04-code-reading-gym/](04-code-reading-gym/README.md) | Annotation drills, trace tables, fake-code contrasts, review katas |
| [05-quality-engineering/](05-quality-engineering/README.md) | Testing strategy and recipes, systematic debugging, performance, security, observability |
| [06-contribution-practice/](06-contribution-practice/README.md) | Tickets (junior → senior), build projects, refactor katas — simulated OSS workflow |
| [07-career-and-collaboration/](07-career-and-collaboration/README.md) | Review mindset, PRs/RFCs, maintainer communication, **interview prep from this repo** |
| [08-reference/](08-reference/verification-log.md) | Command cheatsheet, risk register, rubrics, verification log |

## How to use it

**One weekend.** Do [00-fast-track.md](00-fast-track.md) only. Stop when you've made the small change and can give the teach-back.

**Two weeks (junior, some stack familiarity).** Days 1–2 fast track. Days 3–5: cartography (read in the prescribed [file order](01-codebase-cartography/02-file-reading-order.md), do flows 1–3). Days 6–8: module 02 alongside the gym's annotation drills. Days 9–12: two [good-first tickets](06-contribution-practice/01-good-first-tickets.md) end to end, with tests. Days 13–14: testing strategy + write two of the [test recipes](05-quality-engineering/02-writing-tests-here.md).

**Eight weeks (junior → mid trajectory).** Weeks 1–2 as above. Week 3: module 03 (boundaries, persistence, auth) + pattern catalog. Week 4: full code-reading gym including review katas. Week 5: quality engineering, all five debugging scenarios. Week 6: one [mid-level ticket](06-contribution-practice/02-mid-level-feature-tickets.md) with design note. Week 7: comparison cards + the migration drill; architecture critique with your own additions. Week 8: module 07, including a full mock interview from [interview prep](07-career-and-collaboration/04-interview-prep-from-this-repo.md).

**Ongoing (mentor mode).** Use tickets as pairing exercises; use review katas as calibration for real code review; re-grade yourself against [learning-rubrics.md](08-reference/learning-rubrics.md) monthly.

**Recommended entry points by profile**

| You are | Start at | Then |
| --- | --- | --- |
| Brand-new junior | 00 fast track | 01 cartography → 02 stack → 06 good-first tickets |
| Junior, knows C#/React basics | 00 fast track (skim) | 01 key flows → 04 gym → 06 tickets |
| Mid-level, new to this repo | 01 system map + key flows | 03 patterns + critique → 05 quality → 06 mid tickets |
| Senior doing an architecture review | 03/06 critique + 03/07 comparisons | 08 risk register → verify or kill each hypothesis |

## Ground rules

- Don't change product code while studying modules 00–05; experiments go on scratch branches.
- When docs and code disagree, the code wins — then fix the doc as an exercise in maintainer communication.
- "It compiles" is not done. Every ticket in module 06 defines its own done.
