# Testing Strategy

## The two existing suites

| Suite | What it tests | How | Speed |
| --- | --- | --- | --- |
| [UnitTests](../../../tests/NetIntercom.UnitTests/) | Domain + small application logic, no I/O | xUnit + FluentAssertions, plain `new` | **Verified ~225ms, 27 tests** |
| [IntegrationTests](../../../tests/NetIntercom.IntegrationTests/) | Full HTTP surface over real Postgres | `WebApplicationFactory<Program>` + Testcontainers | Inferred (needs Docker) |

There is **no** UI/component test layer (no RTL/Playwright) — the frontends are untested. That's a real gap to name, not hide.

## What belongs where (the test pyramid, applied)

**Unit — pure logic, zero mocks.** This is where the repo is strongest: the domain is testable with `new Conversation()` because it has no I/O dependencies. Put here: state machines ([ConversationTests](../../../tests/NetIntercom.UnitTests/ConversationTests.cs)), routing-rule matching, office-hours windows ([RoutingAndOfficeHoursTests](../../../tests/NetIntercom.UnitTests/RoutingAndOfficeHoursTests.cs)), placeholder rendering, the median calculation ([PlaceholderRendererTests](../../../tests/NetIntercom.UnitTests/PlaceholderRendererTests.cs)). If a test needs a `DbContext`, it's not a unit test here.

**Integration (WebApplicationFactory + real DB) — wiring, auth, persistence, EF translation.** Anything that depends on the pipeline, the database, or claim plumbing. The existing [HappyPathTests](../../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs) covers the golden path + the auth-rejection cases. This is the right tool because the repo's port (`IAppDbContext`) exposes `IQueryable` — you can't meaningfully unit-test handlers without a real query translator, so EF-dependent logic is verified at this level instead.

**E2E (browser) — absent.** Would cover the SignalR + React behaviors (typing, presence, reconnect catch-up) that integration tests can't reach. The biggest untested surface; a senior project (S-level) is to add Playwright for the inbox happy path.

## What NOT to test

- **Don't unit-test EF query translation by mocking `DbSet`** — it's brittle and proves nothing about real SQL. Use integration tests against Postgres (the repo correctly does).
- **Don't test framework behavior** — that `[Authorize]` returns 401 is ASP.NET's job; test *your* policy *composition* (that the visitor token gets 403 on agent endpoints — which [HappyPathTests#L106-L117](../../../tests/NetIntercom.IntegrationTests/HappyPathTests.cs#L106-L117) does).
- **Don't test DTO `From` getters** in isolation — they're covered transitively by handler/integration tests.
- **Don't snapshot-test the whole `ConversationDto`** — it couples tests to incidental fields.

## Fixtures, builders, time, randomness

- **Time** is the most important test lever here. The domain takes `nowUtc` parameters, and the app uses `IClock` — so [ConversationTests](../../../tests/NetIntercom.UnitTests/ConversationTests.cs#L11) pins `Now = 2026-06-09 12:00 UTC` and computes expected breach times deterministically. **The one place this breaks down is `PresenceTracker`** (uses `DateTime.UtcNow` directly — R10), which is exactly why it has no tests. Lesson: testability is designed in, and the untested code is the code that skipped the seam.
- **Randomness:** the only randomness is token generation (cryptographic, not under test) and `Guid.NewGuid()` ids — tests don't assert on them.
- **Builders:** the repo uses small private factory helpers in tests ([ConversationTests.NewConversation](../../../tests/NetIntercom.UnitTests/ConversationTests.cs#L13-L23)) rather than a builder library — appropriate at this size.
- **Test data for integration:** the seeder runs (`Seed:Enabled=true` in [ApiFactory](../../../tests/NetIntercom.IntegrationTests/ApiFactory.cs#L26)), so integration tests get the three seeded agents and routing rules — they rely on `admin@netintercom.local` existing.

## Flake prevention

- The integration suite uses a **fresh container per factory** ([ApiFactory](../../../tests/NetIntercom.IntegrationTests/ApiFactory.cs#L12-L14)) and `Hangfire:Enabled=false` so the breach job doesn't mutate state mid-test. Both are deliberate flake guards — learn them.
- The happy-path test creates unique anonymous ids (`anon-{Guid}`) per run so re-runs don't collide.
- **Flake risk to watch:** any future test asserting on presence or timing would be flaky unless `IClock`/presence-clock is injectable first (G7).

## Coverage gaps worth a ticket

| Untested behavior | Why it matters | Recipe |
| --- | --- | --- |
| Routing round-robin + office-hours fallback | core product logic, only the *predicates* are tested | [recipe 5](02-writing-tests-here.md) |
| SLA breach job | largest untested behavior, has a real reliability gap | [recipe 6](02-writing-tests-here.md) |
| Refresh-token rotation | security-critical, has a known race | [recipe 4](02-writing-tests-here.md) |
| Cross-tenant rejection (widget) | IDOR surface | [recipe 3](02-writing-tests-here.md) |
| Presence state machine | concurrent logic, zero coverage | blocked on G7 (inject clock) |

**Self-grade:** Basic = can place a given test in the right layer. Solid = explains why EF logic is integration-tested not unit-mocked. Strong = can name the coverage gaps and connect "untested" to "skipped the testability seam" (presence/IClock).
