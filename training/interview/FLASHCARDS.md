# Flashcards: NetIntercom (25). Answers in [`../_answers/flashcards.md`](../_answers/flashcards.md)

1. Which Postgres system column backs `Conversation.RowVersion`, and where is it mapped?
2. What HTTP status does a `DbUpdateConcurrencyException` produce today, and why?
3. Which index serves the transcript query for one conversation?
4. Which groups does `AgentHub.OnConnectedAsync` add a connection to? Which does it *not* restore?
5. Which group does a visitor need for live replies, and which method adds it?
6. What does `PresenceTracker.Compute` return for an agent with no connections who disconnected 30 s ago?
7. Where is the 60 s disconnect grace defined?
8. How often does `PresenceSweeper` run, and what does it persist?
9. What's the Hangfire recurring job id for the SLA sweep, and why must it be constant? (incident 003)
10. Why doesn't `SlaTimer.MarkBreached`'s guard prevent duplicate breaches across two sweeps?
11. What does `SlaTimer.MarkMet` fail to check?
12. Which field does `SendAgentMessage` intentionally leave alone for internal notes? (incident 001)
13. Name the three visitor-facing paths that serialize conversation data.
14. What makes the ownership check in `GetVisitorMessagesHandler` necessary even though ids are GUIDs? (incident 004)
15. Where does `StartWidgetSession` trust client input it shouldn't?
16. Which config key seeds a known admin password in every environment?
17. What does the widget do with the textbox content if `sendMessage` fails?
18. Why does the inbox log agents out when several requests hit 401 together?
19. What does `accessTokenFactory` return on hub reconnect after the access token expired?
20. What does `EnsureCreated` do to an existing database when you add a new index to the model?
21. Why does `HappyPathTests` only assert `teamName != null`?
22. What EF warning appears when a query includes two collection navigations? (incident 005)
23. In incident 005, why does a list page with one big conversation return hundreds of thousands of rows?
24. What does `RouteAsync` do for conversations already queued when an agent comes online?
25. What does the README claim about `?after=` that the frontends don't do?
