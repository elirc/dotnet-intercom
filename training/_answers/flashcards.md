# Flashcard answers

1. `xmin`, via `IsRowVersion()` in `AppDbContext.cs:79`.
2. 500. `ApiExceptionHandler.cs:18-33` doesn't map it, so it falls to the default branch. `LoggingBehavior` logs it at Error.
3. `(ConversationId, CreatedAtUtc)`, from `AppDbContext.cs:88`.
4. It adds `agents` (`AgentHub.cs:30`). It doesn't restore `conv:{id}` groups after a reconnect.
5. `vconv:{conversationId}`, via `VisitorHub.JoinConversation` (`VisitorHub.cs:37-46`, ownership-checked).
6. `Online` (or `Away` if idle for more than 5 min). The grace comes from `LastDisconnectUtc` on the agent's state object (`PresenceTracker.cs:83-89`).
7. `PresenceTracker.DisconnectGrace`, `PresenceTracker.cs:15`.
8. Every 15 s (`PresenceSweeper.cs:15`). It upserts one `PresenceSnapshot` per changed agent. Nothing reads them back.
9. `"sla-breach-check"` (`Program.cs:190`). Recurring jobs are persisted by id. Per-host ids create N schedules that accumulate across deploys.
10. The guard checks in-memory state. Each sweep has its own `DbContext` and loads `Running`, and `SlaTimer` has no concurrency token, so both UPDATEs succeed.
11. Whether `now > BreachAtUtc`. A late reply before the next sweep counts as Met (`Sla.cs:31-40`).
12. `LastMessagePreview` / `LastMessageAtUtc` (`SendAgentMessage.cs:60-65`).
13. The visitor hub `conversationUpdated` payload, `POST /widget/session` (full DTO) and `POST /widget/messages` (full DTO).
14. Ids leak (network traffic, screenshots, logs, shared devices). Authorization must hold when the id is known.
15. It matches the visitor on client-supplied `ExternalId` with no proof (`StartConversation.cs:51-52`), then overwrites profile fields (`:60-63`).
16. `Seed:Enabled` = true in the base `appsettings.json:16-18`, combined with `DbSeeder.DefaultPassword`.
17. It's already cleared before the request (`Widget.tsx:83`), and there's no catch. The text is lost and the rejection is unhandled.
18. Each 401 runs its own refresh with the same rotated token. The losers get 401 and call `setTokens(null)` plus a hard redirect (`api.ts:38-51`).
19. The expired access token (`realtime.ts:11`), so the reconnect fails authentication until a REST call refreshes.
20. Nothing. `EnsureCreated` is a no-op when the database exists.
21. Routing depends on Billing's office hours in UTC wall-clock time, so the team varies with when the test runs.
22. `MultipleCollectionIncludeWarning` (single query loading multiple collections, with no `QuerySplittingBehavior`).
23. A cartesian product: messages × timers per conversation in a single query with two collection joins.
24. Nothing. `RouteAsync` runs only when a conversation is created (`SendVisitorMessage.cs:83`).
25. That both clients catch up with `GET …/messages?after=…` (`README.md:79`). The widget refetches everything, and the inbox invalidates all queries.
