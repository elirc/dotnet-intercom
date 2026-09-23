# Answer: INC-003 duplicate SLA escalations (per-replica recurring job ids)

**Branch:** `training/incidents/003-sla-escalation-storm` · **Commit:** 0c89976 · BUILD: verified (0 errors).

## Root cause
Hangfire recurring jobs are **global, keyed by id, and persisted in PostgreSQL**. `RecurringJob.AddOrUpdate(id, …)` at startup is an upsert. With one constant id, 3 replicas upsert the *same* job, and Hangfire's scheduler, which takes a distributed lock, enqueues it once per minute. With `$"sla-breach-check:{Environment.MachineName}"`, every replica registers its **own** job. Container Apps/Kubernetes replica names change on every deploy and restart, and **nothing deletes the old ids**, so the job count grows with every deploy: 1 original + 3 per rolling deploy + 1 per crash/restart. At 17 now, it gains about 3 per deploy. Next Friday, after 4–5 more deploys, it will be around 30. Every one of them fires every minute, on whichever Hangfire server dequeues it.

## Why the domain guard didn't fire (task 2)
`MarkBreached` guards the **in-memory** status. Each run has its own `DbContext`. Overlapping runs each load the timer as `Running` (the query filters `Status == Running`, `CheckSlaBreaches.cs:39`), each mark it `Breached` in their own tracker, and each `SaveChangesAsync` issues `UPDATE "SlaTimers" SET "Status"=2, "CompletedAtUtc"=… WHERE "Id"=…` with **no concurrency predicate** (`SlaTimer` has no concurrency token, and only `Conversation.RowVersion` exists: `AppDbContext.cs:79`). All succeed, all notify. Runs that start after an earlier one committed find nothing, which is why the toasts come 3–4 times rather than 17. `docs/architecture-review.md` §2.3 says the loser "throws DomainException". That's not what this code does. Duplicates are silent, which is worse.

## The 40% jump and the toast/timer disagreement (task 3)
- `SlaTimer.MarkMet` doesn't check `BreachAtUtc` (`Sla.cs:31-40`). A reply sent *after* the deadline but *before* the next sweep counts as **Met**. With one sweep a minute, that was up to 60 s of silent grace. With ~17 sweeps a minute, breaches are detected within seconds, so the breach rate went up because measurement got **more accurate**. This latent bug is the real cause of the "jump". Fix: `MarkMet(now)` sets `Breached` if `now > BreachAtUtc` (or records `MetLate`), independent of sweep timing.
- Lost update: the sweep loads the timer as Running, Sara's reply handler loads it as Running and marks it Met, and both save. The last writer wins. The sweep had already sent the "breached" toast, but the row says `Met`. The same missing concurrency token causes it.

## Fix
1. Revert to one constant id: `"sla-breach-check"`.
2. Clean up production: `RecurringJob.RemoveIfExists(id)` for every `sla-breach-check:*` in the `recurring-jobs` set. Do it once from a startup task or the dashboard. The dashboard isn't reachable (`/hangfire` needs a bearer header, review finding), so use a one-off script or an admin endpoint.
3. Make the sweep race-free and idempotent: a set-based transition `UPDATE "SlaTimers" SET "Status"=2, "CompletedAtUtc"=@now WHERE "Status"=0 AND "BreachAtUtc"<=@now RETURNING "Id","ConversationId"` (EF 8: raw SQL, or `ExecuteUpdateAsync` followed by reading the affected rows), and notify **only** for the rows this run transitioned. Alternatively add a concurrency token to `SlaTimer` and catch the conflict per timer.
4. Add `[DisableConcurrentExecution(timeoutInSeconds: 50)]` on `SlaBreachJob.RunAsync` as a belt-and-braces measure. It doesn't replace (3).
5. Reply-vs-sweep: make the reply path's update conditional too (`WHERE Status = Running`).

## What would have caught it
A test that boots two `WebApplicationFactory` hosts with Hangfire against the same Testcontainers DB and asserts one recurring job exists. Or just a review rule: "recurring job ids must be constants". An alert on the size of the `recurring-jobs` set, or on `slaBreached` notifications per breached timer > 1.
