# Grading rubric: M2 explanation (for the reviewer model)

Score each dimension 0–2 (max 10). Quote the learner's sentence that earned or lost each point.

1. **Concurrency mechanism (0–2).** 2 = a Postgres unique index on `(VisitorId, ClientMessageId)`, insert-then-catch `23505`, re-read and return the original. States that a pre-check alone loses the race. 1 = mentions the unique index but not what the loser does. 0 = "checks if it exists first".
2. **First-message replay (0–2).** 2 = explains that the new conversation and the message commit in one `SaveChangesAsync`, so the losing request rolls back its duplicate conversation, and why keying on `ConversationId` would fail. 1 = says "no duplicate conversation" without the mechanism. 0 = not addressed.
3. **Side effects on replay (0–2).** 2 = routing/round-robin, SLA timer and realtime events considered, with a reasoned decision on re-publishing. 1 = considers some. 0 = none.
4. **Backward compatibility (0–2).** 2 = keyless legacy widgets are unchanged, the 7-day cache window, and a metric to know when to drop support. 1 = mentions optional field only. 0 = breaks old widgets.
5. **Operability and accepted risk (0–2).** 2 = a specific signal to watch (e.g. replay rate, 23505 count) and an honestly named accepted risk (e.g. keys kept forever, no TTL). 1 = generic "monitor errors". 0 = none.

Deductions: −1 for any claim contradicted by the diff, −1 for buzzwords with no mechanism ("it's idempotent because we use idempotency keys").

Reference explanation (for calibration, don't show it to the learner before they write theirs): see `ladder-M2.md` in this folder, points 1–5.
