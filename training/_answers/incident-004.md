# Answer: INC-004 widget catch-up IDOR

**Branch:** `training/incidents/004-widget-transcript-exposure` · **Commit:** e813e94 · BUILD: verified (0 errors). Unit tests 27/27 pass on the branch. The happy-path integration test (not run here: Docker) only reads the visitor's *own* conversation, so it would pass too.

## Root cause
Authorization was folded into a data filter that isn't equivalent to it. The old code asked "does this conversation belong to this visitor?" and returned 403 if not. The new predicate asks, **per message**, "is this message by this visitor, OR by an agent?" For a foreign conversation, the visitor's own messages are filtered out, but **every agent reply passes the second disjunct**. The comment's premise, "agent replies are only ever written into a conversation, so they're safe", is true and irrelevant: the question was never whether the message belongs to the conversation, but whether the *caller* may read that conversation.

Why reviewers bought it: the diff *adds* a visitor filter (it looks stricter), the comment gives a performance reason, the tests stayed green, and the 403 branch left through a deletion that's easy to skim past.

## "GUIDs aren't guessable" is not a defense here
Conversation ids leak through: (1) the widget's own network traffic, which any shared or kiosk computer or a browser extension can read; (2) agent screenshots and pasted inbox URLs in tickets or Slack; (3) application logs (`RequestPath` contains the id: see the Serilog request log and `LoggingBehavior`), shipped to third-party log tools; (4) `SourceUrl`/referrers if an id ever appears in a URL; (5) former visitors on shared devices, since visitor tokens last 30 days (`TokenService.cs:74`). Authorization must hold even when the attacker knows the id. Unguessable ids are defense in depth, not the control.

## The metric
"403s on this route fell to 0" was a clear signal. A naive "403s > N" alert is noisy (it fires on attacks and bugs alike). The useful alert is a *drop-to-zero* anomaly on a route that normally rejects, or better, a synthetic probe (a canary visitor requests a canary foreign conversation every 5 min and pages if it gets 200).

## Fix
Restore the ownership check. It's one indexed `EXISTS` on the PK, so the "perf" win was about 4 ms. If the round-trip matters, join instead of OR: `_db.Messages.Where(m => m.ConversationId == id && m.Conversation.VisitorId == visitorId && …)`. That's the same single query and correct, although it returns `[]` rather than 403 for foreign ids, which hides probing. Prefer the explicit 403.

## Missing negative tests (write them all)
For visitor B's conversation, using visitor A's token: `GET …/messages` → 403; `GET …/messages?after=` → 403; `POST …/read` → 403; `POST /widget/messages {conversationId: B}` → 404 (current behavior, `SendVisitorMessage.cs:61-62`); hub `JoinConversation(B)` → `HubException`; hub `Typing(B)` → should reject (it doesn't today: `VisitorHub.cs:48-58`, pre-existing finding). Put them in one table-driven test class so a new endpoint gets added to the matrix.

## Blast radius
The request logs have the path (conversation id) but **not the visitor id** of the caller. `LoggingBehavior` logs only the request name. You can find tokens that requested ids they don't own only if you log `visitor_id` per request. Today you can find *conversations that were requested by more than one distinct client IP* (a weak signal: the widget moves between networks). State it honestly: "we cannot prove non-exploitation for the 9-day window." That drives the customer-notification decision. Action item: add `VisitorId`/`AgentUserId` to the Serilog request enrichment.
