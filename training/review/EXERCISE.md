# Blind code review: NetIntercom (60 minutes)

## Rules
- **Don't open** `docs/architecture-review.md`, `docs/upskill/08-reference/risk-register.md` or `docs/upskill/03-architecture-and-patterns/06-architecture-critique.md` before you finish. They are model reviews of this repo, and you'll compare against them afterwards.
- Review `main` (`git log` shows one commit). Ignore the `training/incidents/*` branches.
- Time box: 60 minutes. Stop when the timer ends, even if you're mid-thought. Missing things under time pressure is part of the data.

## Scope, in suggested reading order (~3,500 lines total)
1. `README.md`: what the system *claims*. Write down every claim you'll verify.
2. `src/NetIntercom.Api/Program.cs`, `appsettings*.json`: configuration, auth, CORS, Hangfire, startup.
3. `src/NetIntercom.Application/Widget/*`, then `Api/Controllers/WidgetController.cs`: the anonymous, internet-facing surface.
4. `src/NetIntercom.Api/Realtime/*`: hubs, presence, notifier.
5. `src/NetIntercom.Application/{Conversations,Sla,Routing,Auth}/*` and `src/NetIntercom.Domain/Entities/{Conversation,Sla}.cs`.
6. `src/NetIntercom.Infrastructure/Persistence/AppDbContext.cs`.
7. `frontend/widget/src/Widget.tsx`, `frontend/inbox/src/{api.ts,realtime.ts,pages/Inbox.tsx}`.
8. `tests/**`: for each README claim, is there a test that would fail if it broke?

## Output (write it in `training/review/MY_REVIEW.md`)
For each finding:
```
### [SEVERITY] one-line title
Where: file:line
What happens: the concrete failure, with an example input or interleaving
Why it matters in production: who notices, how bad
Fix sketch: one or two lines
AI-smell? yes/no (if yes, which pattern)
```
Severities: **Critical** (data exposure, auth bypass, data loss) · **High** (wrong results or outages under realistic conditions) · **Medium** (breaks at scale or under concurrency, or costly to operate) · **Low** (maintainability, hygiene).

Look in particular for:
- places where a README claim isn't backed by code or a test
- what happens across a **reconnect**, a **retry**, a **second replica**, or **two agents at once**
- configuration that is unsafe outside Development
- tests that pass while the feature they name is broken

When you're done, fill in [`COMPARE.md`](COMPARE.md). The model review is [`../_answers/review.md`](../_answers/review.md).
