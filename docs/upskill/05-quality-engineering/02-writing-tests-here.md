# Writing Tests Here

Eight recipes using the repo's existing helpers. Commands are marked verified/inferred. Run the unit ones (no Docker); the integration ones need a running Docker daemon (was down at authoring — marked inferred).

## Commands

```powershell
# Verified on the authoring machine (dotnet at ~\.dotnet\dotnet.exe):
dotnet test tests/NetIntercom.UnitTests                                   # 27/27, ~225ms

# Run one test class or method by filter:
dotnet test tests/NetIntercom.UnitTests --filter "FullyQualifiedName~ConversationTests"
dotnet test tests/NetIntercom.UnitTests --filter "DisplayName~VisitorMessage_StartsFirstResponseTimer"

# Inferred (needs Docker):
dotnet test tests/NetIntercom.IntegrationTests
dotnet test tests/NetIntercom.IntegrationTests --filter "FullyQualifiedName~HappyPathTests"
```

---

## Recipe 1 (unit): a happy-path domain transition

**Goal:** prove a NextResponse timer starts after the first reply. **Pattern to copy:** [ConversationTests.VisitorMessage_AfterFirstReply_StartsNextResponseTimer](../../../tests/NetIntercom.UnitTests/ConversationTests.cs#L69-L81).

```csharp
[Fact]
public void SecondVisitorMessage_AfterReply_UsesNextResponseWindow()
{
    var c = NewConversation(Policy);            // helper at ConversationTests.cs#L13
    c.RecordVisitorMessage(Now);                // First timer
    c.RecordAgentReply(Now.AddMinutes(2));      // meets it, stamps first reply
    c.RecordVisitorMessage(Now.AddMinutes(5));  // Next timer
    var t = c.SlaTimers.Single(x => x.Status == SlaTimerStatus.Running);
    t.Kind.Should().Be(SlaTimerKind.NextResponse);
    t.BreachAtUtc.Should().Be(Now.AddMinutes(5 + 30));  // 30 = NextResponseMinutes
}
```
**Why it works:** pure domain, deterministic `Now`, no mocks. This is the cheapest, fastest test you can write here.

## Recipe 2 (unit): a validation failure

**Goal:** the shortcut charset rule rejects spaces. **Pattern:** validators are plain classes — instantiate and call.

```csharp
[Fact]
public void SaveCannedReply_RejectsInvalidShortcut()
{
    var result = new SaveCannedReplyValidator()
        .Validate(new SaveCannedReplyCommand(null, Guid.NewGuid(), "bad shortcut", "T", "B", null, true));
    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.PropertyName == "Shortcut");
}
```
**Why:** validators ([SaveCannedReplyValidator](../../../src/NetIntercom.Application/CannedReplies/CannedReplies.cs#L52-L63)) need no infrastructure — test the rule directly, not through HTTP.

## Recipe 3 (integration): cross-tenant rejection (IDOR)

**Goal:** visitor A cannot read visitor B's messages. **Pattern:** [HappyPathTests.AgentEndpoints_RejectAnonymousAndVisitorCallers](../../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs#L106-L117).

```csharp
[Fact]
public async Task Visitor_CannotReadAnotherVisitorsMessages()
{
    var client = _factory.CreateClient();
    // visitor A: session + message → conversation id
    // visitor B: separate session → its own token
    // B GETs /api/v1/widget/conversations/{A's id}/messages → expect 403
    (await bClient.GetAsync($"/api/v1/widget/conversations/{aConversationId}/messages"))
        .StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```
**Why integration:** the ownership check is in the query ([WidgetQueries#L25-L30](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L25-L30)) — only a real DB + real claims prove it. This test would currently **pass** for the REST path; write the parallel one for `VisitorHub.Typing` and watch it expose R4.

## Recipe 4 (integration): refresh-token rotation

**Goal:** a refresh token works once, then is revoked. **Currently untested — security-critical.**

```csharp
[Fact]
public async Task RefreshToken_IsSingleUse()
{
    // login → grab refreshToken
    var first = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });
    first.StatusCode.Should().Be(HttpStatusCode.OK);
    var second = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken }); // same token
    second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);  // rotation revoked it
}
```
**Why:** locks in the rotation behavior ([Refresh.cs#L43](../../../src/NetIntercom.Application/Auth/Refresh.cs#L43)) and documents the property the client must respect (don't reuse).

## Recipe 5 (unit-ish): routing round-robin

**Goal:** assignment goes to the least-recently-assigned online member. **Tricky:** `ConversationRouter` needs `IAppDbContext`, `IPresenceService`, `IClock`. Use the EF **in-memory or SQLite** provider for the context and a fake presence service.

```csharp
// Fake the presence port:
var presence = new FakePresence(onlineIds: new[] { agentA.Id, agentB.Id });
// Seed two members with different LastAssignedAtUtc; route; assert agentB (older) got it.
```
**Why it's recipe-worthy:** this is the **untested core logic** (only `RoutingRule.Matches` is covered). Note: SQLite/in-memory won't replicate Postgres `jsonb`/locking semantics, so this proves the *algorithm*, not the concurrency (the race R5 needs a different test — concurrent integration calls).

## Recipe 6 (unit): the SLA breach job

**Goal:** an overdue Running timer is marked Breached. **Largest untested behavior.** The handler ([CheckSlaBreachesHandler](../../../src/NetIntercom.Application/Sla/CheckSlaBreaches.cs#L31-L62)) takes `IAppDbContext`, `IRealtimeNotifier`, `IClock`, `ILogger` — fake the notifier, control the clock.

```csharp
var clock = new FakeClock(Now.AddMinutes(20));   // past a 15-min timer
var notifier = new SpyNotifier();
var handler = new CheckSlaBreachesHandler(db, notifier, clock, NullLogger<...>.Instance);
var count = await handler.Handle(new CheckSlaBreachesCommand(), default);
count.Should().Be(1);
notifier.SlaBreachedCalls.Should().HaveCount(1);   // proves notify-after-save
```
**Bonus assertion:** run `Handle` twice; the second returns 0 (idempotency — proves the Running filter).

## Recipe 7 (integration): async side effect / SLA met end-to-end

**Goal:** agent reply meets the timer. Already done in [HappyPathTests#L73-L84](../../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs#L73-L84) — study it as the template for asserting `activeSlaTimer` goes null and `firstAgentReplyAtUtc` is set.

## Recipe 8 (integration): migration/seed behavior

**Goal:** the seeder is idempotent. **Pattern:** the seeder early-returns if any agent exists ([DbSeeder#L15-L18](../../../src/NetIntercom.Infrastructure/Persistence/DbSeeder.cs#L15-L18)).

```csharp
// Boot the factory twice against the same container (or call SeedAsync twice);
// assert AgentUsers count stays 3, not 6.
```
**Why:** seeding runs on every startup ([Program.cs#L159-L164](../../../src/NetIntercom.Api/Program.cs#L159-L164)); idempotency is load-bearing and worth a guard test.

---

## UI state (no harness yet)

There's no RTL/Playwright setup. To test the inbox's optimistic message append ([Inbox.tsx#L212-L217](../../../frontend/inbox/src/pages/Inbox.tsx#L212-L217)) or the widget's dedupe ([Widget.tsx#L19-L21](../../../frontend/widget/src/Widget.tsx#L19-L21)) you'd first add Vitest + React Testing Library (inbox) — that scaffolding *is* senior project S4. Until then, these behaviors are manual-test-only; say so in any PR that touches them.

**Self-grade:** Basic = wrote recipes 1–2 and they pass. Solid = wrote recipe 3 or 4 and understands why it's integration not unit. Strong = wrote recipe 6 including the idempotency re-run, and can explain which recipes prove *algorithm* vs *concurrency*.
