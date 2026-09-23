# NetIntercom apprenticeship lab (B01, tier LAB)

A practice lab built on this repo's real code: a two-sided live chat with SignalR, Hangfire SLA sweeps, in-memory presence, an embeddable widget and an agent inbox. **You do the thinking.** Every solution is sealed in `_answers/`. Open an answer only after you've written your own.

> Existing learning material in this repo: `docs/upskill/` (47-file curriculum, good) and `docs/architecture-review.md` (a model review). Treat the review as an **answer key**: don't read it before `review/EXERCISE.md`. One of its claims (§2.3) is wrong, and `_answers/review.md` R5 explains why.

## Contents
| Folder | What | Files |
|---|---|---|
| `review/` | 60-min blind review + compare template | `EXERCISE.md`, `COMPARE.md` |
| `incidents/` | 5 production incidents, each with a bug committed on its own branch | `001`–`005` |
| `ladder/` | Tickets: junior J1–J3, mid M1–M4, senior S1–S3 | 10 files |
| `navigation/` | Architecture with evidence, onboarding, trade-offs, a change request | 4 files |
| `agentic/` | Spec → implementer agent → adversarial reviewer → your explanation | `WORKFLOW.md`, `VERIFY_AI_OUTPUT.md` |
| `interview/` | Stories, 20 questions, system design, mock defense, flashcards | 5 files |
| `_answers/` | Sealed solutions and rubrics, one per exercise | 20 files |

## Incident branches (check out with a worktree, never on your main tree)
```
git worktree add ../ni-inc-001 training/incidents/001-widget-note-leak
```
| # | Branch | Symptom |
|---|---|---|
| 001 | `training/incidents/001-widget-note-leak` | Customer saw agent-only text next to the chat bubble |
| 002 | `training/incidents/002-unassigned-queue-spike` | New chats pile up unassigned while agents show online |
| 003 | `training/incidents/003-sla-escalation-storm` | Duplicate SLA-breach toasts, more every day |
| 004 | `training/incidents/004-widget-transcript-exposure` | Pen test: a visitor reads another customer's support replies |
| 005 | `training/incidents/005-inbox-list-latency` | Inbox p95 from 180 ms to 3.7 s, DB CPU pinned |

Each branch is one plausible PR on top of `main` (f2198dd). All five compile, and the 27 existing unit tests pass on every branch. That's part of the lesson.

## Suggested order (≈ 38–46 h)
1. `navigation/ARCHITECTURE.md` + `ONBOARDING.md`: read with the code open (2 h).
2. `review/EXERCISE.md` → `COMPARE.md` (2 h).
3. Ladder junior J1 → J2 → J3 (8 h).
4. Incidents 004, 001, then 005 (security first, then data exposure, then performance) (4 h).
5. Ladder mid M3 → M4 → M1 (2 days).
6. `agentic/WORKFLOW.md` on M2 (half a day).
7. Incidents 002, 003 (2 h). Then `navigation/TRADEOFFS.md` and `CHANGE_REQUEST.md` (3 h).
8. Senior S1 → S3 → S2 design docs (3 days, can be spread out).
9. Interview kit last: `TECHNICAL_QUESTIONS` → `FLASHCARDS` → `PROJECT_STORIES` (fill from what you did) → `SYSTEM_DESIGN` → `MOCK_DEFENSE` (4 h).

## Running things
Unit tests: `dotnet test tests/NetIntercom.UnitTests`. This machine only has the .NET 10 SDK and no .NET 8 runtime, so set `DOTNET_ROLL_FORWARD=Major` to run the net8 tests. Integration tests need Docker (Testcontainers Postgres).
