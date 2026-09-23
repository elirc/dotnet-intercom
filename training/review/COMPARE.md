# Compare your review with the model reviews

Fill this in after `MY_REVIEW.md`. Compare against **two** references:
(A) `training/_answers/review.md`, and (B) `docs/architecture-review.md` (written earlier by a different reviewer).
They disagree in places. B has at least one claim that the code doesn't support (A names it). Deciding who is right is part of the exercise.

## 1. Scoreboard
| | Critical | High | Medium | Low |
|---|---|---|---|---|
| Found by me | | | | |
| In A | | | | |
| Found by me and in A | | | | |

## 2. Findings I missed (list the A ids, e.g. R3, R11)
| id | Why I missed it (didn't read the file / read it but didn't see the failure / saw it but misjudged severity / didn't know the concept) |
|---|---|
| | |

## 3. Findings I had that A or B don't have
Are they real? Prove each one with a file:line and an interleaving, or withdraw it.

## 4. Where A and B disagree
Pick one disagreement. Read the code and write down who is right, with evidence.

## 5. The pattern behind my misses
Circle the ones that apply, then write one sentence on what you'll do differently next review:
- I read happy paths and didn't ask "what if this runs twice, or on two nodes, or after a reconnect?"
- I trusted comments, names or the README instead of the code
- I didn't trace data to **every** consumer (e.g. a DTO reaching both agent and visitor)
- I didn't open config files or treat `appsettings.json` as production config
- I reviewed backend only, or frontend only
- I didn't check whether tests actually assert the thing their name claims

## 6. Two habits for the next review
1.
2.
