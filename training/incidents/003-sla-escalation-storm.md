# INC-003: Duplicate SLA-breach escalations, getting worse every day

**Branch:** `training/incidents/003-sla-escalation-storm` · **Severity:** SEV-3 → SEV-2 · **Time box:** 60 min

## Ticket (Thu 09:15 UTC)
> **From Priya:** Every breached chat now pops the red "SLA breached" toast 3 or 4 times, and yesterday it was 2–3 times. Agents are muting the inbox. Also, Support's breach rate in the SLA report jumped about 40% this week with no change in staffing. And on one chat where Sara replied right at the deadline, the toast said "breached" but the SLA timer in the API says `Met`.

## What on-call sees
- Topology since Monday: **3 API replicas** (Azure Container Apps). Rolling deploys: Mon, Tue, Wed (×2).
- Deploy Mon: #240 `ops(hangfire): per-replica recurring job id for the SLA sweep`, #241 `infra: 3 replicas`.
- Hangfire tables (`hangfire.set` where `key = 'recurring-jobs'`), queried Thu 09:20:
```
 value
-------------------------------------------
 sla-breach-check
 sla-breach-check:netintercom-api--7f9c2-5d8b
 sla-breach-check:netintercom-api--7f9c2-9k2x
 sla-breach-check:netintercom-api--7f9c2-q1w4
 sla-breach-check:netintercom-api--8a1d0-2mzp
 ... (17 rows)
```
- API logs for one minute, all replicas (log shipper adds `host`):
```
[09:14:00.021 WRN] SLA breach: conversation 5e7a…, FirstResponse timer due 2026-09-18 09:13:41Z (team Support, assignee Sara Support) host=…-5d8b
[09:14:00.034 WRN] SLA breach: conversation 5e7a…, FirstResponse timer due 2026-09-18 09:13:41Z (team Support, assignee Sara Support) host=…-q1w4
[09:14:00.036 WRN] SLA breach: conversation 5e7a…, FirstResponse timer due 2026-09-18 09:13:41Z (team Support, assignee Sara Support) host=…-q1w4
[09:14:00.052 INF] Handled CheckSlaBreachesCommand   (×17 this minute, no failures)
```
- Hangfire "Succeeded" is at ≈17 per minute. "Failed" is at 0. The domain guard `SlaTimer.MarkBreached` ("Only a running SLA timer can breach") has never fired.
- `SELECT status, count(*) FROM "SlaTimers" WHERE "CompletedAtUtc" > now() - interval '1 day' GROUP BY 1;` shows `Breached` up 40% week over week with the same volume of conversations.

## Suspect PR #240 (as merged)
```diff
--- a/src/NetIntercom.Api/Program.cs
     app.MapHangfireDashboard("/hangfire").RequireAuthorization("Admin");
-    RecurringJob.AddOrUpdate<SlaBreachJob>("sla-breach-check", j => j.RunAsync(), Cron.Minutely);
+
+    // Register the SLA sweep per instance so the dashboard shows which replica owns
+    // which schedule (ops asked for this after the move to 3 replicas).
+    var recurringJobId = $"sla-breach-check:{Environment.MachineName}";
+    RecurringJob.AddOrUpdate<SlaBreachJob>(recurringJobId, j => j.RunAsync(), Cron.Minutely);
```

## Your tasks
1. Explain why the duplicates **grow over time**. Why 17 rows? What will the number be next Friday?
2. Three sweeps "breached" the same timer and all three saved successfully. Why didn't the domain guard in `MarkBreached` stop the second and third? What would have stopped them?
3. Explain Priya's second complaint: the 40% jump, and the toast/timer disagreement. Is it caused by this PR, or did the PR make something visible? (Read `CheckSlaBreachesHandler`, `SendAgentMessageHandler` and `SlaTimer.MarkMet` together.)
4. Fix it, clean up production, and make the job safe to run concurrently.
5. What would have caught this before production?

Sealed answer: [`../_answers/incident-003.md`](../_answers/incident-003.md)
