# Framework Mental Models

## ASP.NET Core: the middleware pipeline is ordered and the order is load-bearing

Read [Program.cs#L167-L191](../../../src/NetIntercom.Api/Program.cs#L167-L191) top to bottom — that *is* the request pipeline:

```
CorrelationId → ExceptionHandler → SerilogRequestLogging → [Swagger]
→ CORS → Authentication → Authorization → RateLimiter → endpoints
```

**Why order matters here:**
- CorrelationId is **first** so the id exists when the exception handler stamps it onto a ProblemDetails ([ApiExceptionHandler#L42](../../../src/NetIntercom.Api/Infrastructure/ApiExceptionHandler.cs#L42)).
- ExceptionHandler wraps everything after it — a throw in a handler becomes RFC 7807, not a stack trace.
- Authentication **before** Authorization (you can't check a role before establishing who the caller is).
- RateLimiter after auth means authenticated callers are limited by the same per-IP policy — a deliberate (and debatable) choice.

**Failure mode:** move `UseAuthorization` above `UseAuthentication` and every `[Authorize]` fails closed (no identity yet). **Drill:** what breaks if `UseCors` is placed *after* the endpoints? (Preflight requests never get CORS headers → browser blocks the inbox.)

## DI lifetimes and the captive dependency

The three lifetimes, each shown here:

| Lifetime | Example | Why |
| --- | --- | --- |
| Singleton | [`TokenService`, `PasswordService`, `IClock`](../../../src/NetIntercom.Infrastructure/DependencyInjection.cs#L24-L26), [`PresenceTracker`](../../../src/NetIntercom.Api/Program.cs#L38) | Immutable config or process-wide state |
| Scoped | [`AppDbContext`, `IAppDbContext`](../../../src/NetIntercom.Infrastructure/DependencyInjection.cs#L29-L31), [`ICurrentUser`](../../../src/NetIntercom.Api/Program.cs#L34) | One unit-of-work / one request's identity |
| Transient | the MediatR pipeline behaviors ([AddApplication#L15-L16](../../../src/NetIntercom.Application/DependencyInjection.cs#L15-L16)) | Cheap, stateless, new each resolve |

**The captive dependency bug** is the thing to internalize: a longer-lived service that holds a shorter-lived one *captures* it past its intended life. [`PresenceSweeper`](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L89-L110) is a **singleton** hosted service that needs a **scoped** `AppDbContext`. The wrong fix is constructor injection (one frozen context forever → `DbContext` is not thread-safe → concurrency exceptions and stale tracking). The right fix, shown at [#L91-L92](../../../src/NetIntercom.Api/Realtime/PresenceSweeper.cs#L91-L92): inject `IServiceScopeFactory`, create a scope per sweep, resolve the context from it, dispose with the scope.

**Drill:** `RealtimeNotifier` is a singleton ([Program.cs#L40](../../../src/NetIntercom.Api/Program.cs#L40)) and depends on `IHubContext<>` — also singleton. Why is *that* safe but a singleton depending on `IAppDbContext` is not? (Lifetime mismatch is the rule: singleton→singleton fine; singleton→scoped captures.)

## Model binding and validation

Controllers bind JSON bodies to records ([ConversationsController.SendMessageRequest](../../../src/NetIntercom.Api/Controllers/ConversationsController.cs#L48)) and route/query params via `[FromQuery]`/route constraints (`{id:guid}`). Then validation happens **not** at the controller but in the MediatR pipeline: [`ValidationBehavior`](../../../src/NetIntercom.Application/Common/Behaviors.cs#L7-L32) runs every FluentValidation validator for the request type before the handler.

This is a deliberate split worth understanding: *binding/shape* errors (malformed GUID) are rejected by the framework at the edge; *business* validation (body ≤ 10k, email format) lives with the use case. **Failure mode:** because `[ApiController]` automatic model validation and FluentValidation both exist, a contributor can't tell which runs — here the records have no data annotations, so FluentValidation is the only business gate. **Drill:** trace a `body: ""` widget message from POST to 400 response — name every component it passes through ([validator](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L23-L30) → behavior → [exception handler](../../../src/NetIntercom.Api/Infrastructure/ApiExceptionHandler.cs#L20-L27)).

## Options pattern

[`JwtOptions`](../../../src/NetIntercom.Infrastructure/Auth/TokenService.cs#L11-L21) is bound from config and registered as a singleton ([AddInfrastructure#L16-L23](../../../src/NetIntercom.Infrastructure/DependencyInjection.cs#L16-L23)) — note it's registered as the raw type, not `IOptions<JwtOptions>`, with a fail-fast guard if `SigningKey` is empty. That guard is a small masterclass: validate config at startup, not at first use. **Drill:** contrast with the framework's `IOptions<T>` (lazy, no startup validation by default). When would you want the lazy version? (Hot-reloadable settings — not the case for a signing key.)

---

## React: render vs commit

The inbox and widget are React 18 function components. The mental model that prevents most bugs: **render is a pure computation of UI from state; effects run after commit; cleanup runs before the next effect or on unmount.**

- **Render** computes JSX from props/state — must be side-effect-free. [`InboxPage`](../../../frontend/inbox/src/pages/Inbox.tsx#L18-L129) re-renders on every state/query change.
- **Effects** (`useEffect`) do the impure work: subscribe to hub events ([Inbox.tsx#L36-L81](../../../frontend/inbox/src/pages/Inbox.tsx#L36-L81)), join/leave conversation groups ([#L193-L203](../../../frontend/inbox/src/pages/Inbox.tsx#L193-L203)).
- **Cleanup** is the function an effect returns: `LeaveConversation` on unmount/selection-change ([#L200-L202](../../../frontend/inbox/src/pages/Inbox.tsx#L200-L202)), `conn.stop()` in the widget ([Widget.tsx#L68-L71](../../../frontend/widget/src/Widget.tsx#L68-L71)).

## Stale closures — the React bug this repo actually carries

An effect captures the variables in scope *when it ran*. The widget's bootstrap effect has `[]` deps and an explicit `// eslint-disable-next-line react-hooks/exhaustive-deps` ([Widget.tsx#L72](../../../frontend/widget/src/Widget.tsx#L72)) — so its `onreconnected` handler closes over `session.conversation` from first render ([#L52-L58](../../../frontend/widget/src/Widget.tsx#L52-L58)). For a visitor whose conversation is created *later* (via `send()`), that closure is still `null`, so reconnect catch-up does nothing for them. This is debugging scenario 4 — a textbook stale closure.

**Drill:** the inbox avoids this class of bug by reading fresh data through TanStack Query and invalidating, rather than closing over it. Find where the inbox's reconnect handler refetches *everything* ([Inbox.tsx#L73-L76](../../../frontend/inbox/src/pages/Inbox.tsx#L73-L76)) and explain why "invalidate, don't capture" is more robust than the widget's approach.

## Keys, effects-with-deps, and `key` to force remount

[`ConversationView`](../../../frontend/inbox/src/pages/Inbox.tsx#L116) is rendered with `key={selected.id}` — changing conversations *remounts* it, resetting composer state and re-running the join effect. That's an intentional use of `key` as identity, not just a list-diff hint. **Drill:** what would break if the `key` were removed? (Switching conversations would keep the old composer text and not re-run JoinConversation for the new id.)

## Cleanup correctness

The widget guards setState-after-unmount with a `cancelled` flag ([Widget.tsx#L25-L67](../../../frontend/widget/src/Widget.tsx#L25-L67)); the inbox uses a `mounted` flag in its hub effect ([Inbox.tsx#L37-L80](../../../frontend/inbox/src/pages/Inbox.tsx#L37-L80)). Both are defending against React 18 StrictMode double-invocation and async-resolves-after-unmount. **Failure mode:** the inbox's heartbeat listeners ([realtime.ts#L23-L27](../../../frontend/inbox/src/realtime.ts#L23-L27)) are added but **never removed** — a real (minor) cleanup omission. **Drill:** write the cleanup that removes those `mousemove`/`keydown` listeners, and explain why it currently leaks but doesn't visibly break.
