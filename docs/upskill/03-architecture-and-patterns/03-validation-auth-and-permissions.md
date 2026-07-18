# Validation, Auth and Permissions

## The three layers of "is this allowed?"

1. **Authentication** — who are you? One JWT bearer scheme ([Program.cs#L45-L71](../../../src/NetIntercom.Api/Program.cs#L45-L71)). Agent and visitor tokens differ only in claims, not scheme.
2. **Authorization (coarse)** — what role? Three policies: `Agent`, `Admin`, `Visitor` ([Program.cs#L73-L76](../../../src/NetIntercom.Api/Program.cs#L73-L76)), applied as controller attributes.
3. **Resource authorization (fine)** — may *you* touch *this* row? Enforced inside handlers via ownership predicates in the query ([Pattern 7](05-pattern-catalog.md)).

The first two are framework-standard. The third is where the interesting decisions — and the gaps — live.

## Authentication details worth knowing

- Tokens are HMAC-SHA256 signed; the app refuses to start without a signing key ([AddInfrastructure#L16-L21](../../../src/NetIntercom.Infrastructure/DependencyInjection.cs#L16-L21)).
- Agent tokens carry `sub`, `email`, `name`, `role=agent`, plus `role=admin` for admins ([TokenService#L43-L64](../../../src/NetIntercom.Infrastructure/Auth/TokenService.cs#L43-L64)).
- Visitor tokens carry `sub`, `visitor_id`, `role=visitor` and last **30 days** ([#L66-L75](../../../src/NetIntercom.Infrastructure/Auth/TokenService.cs#L66-L75)).
- Hubs read the token from `?access_token=` because browsers can't set headers on the WebSocket handshake ([Program.cs#L60-L69](../../../src/NetIntercom.Api/Program.cs#L60-L69)).
- `ClockSkew` is tightened to 30s ([#L55](../../../src/NetIntercom.Api/Program.cs#L55)) — tighter than the 5-min default, so expiry is closer to honest.

## Identity extraction

[`CurrentUser`](../../../src/NetIntercom.Api/Infrastructure/CurrentUser.cs) reads claims off `HttpContext.User`:
- `AgentUserId` returns null unless the caller is *in the agent role* — so a visitor token can never masquerade as an agent id.
- `VisitorId` reads the `visitor_id` claim.
- `IsAdmin` checks the admin role.

This is the single chokepoint handlers trust. **Drill:** why does `AgentUserId` guard on `IsInRole("agent")` but `VisitorId` doesn't guard on the visitor role? Is that a problem? (Trace: visitor endpoints already require the Visitor policy, so the role is established before the claim is read — defensible, but the asymmetry is worth a comment.)

## Resource isolation: the visitor side (good)

Every widget data operation re-checks `VisitorId` ownership in the query:
- [`GetVisitorMessagesHandler`](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L25-L30) and [`MarkReadByVisitorHandler`](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L61-L66) — `AnyAsync(ownership)` → `ForbiddenException`.
- [`SendVisitorMessageHandler`](../../../src/NetIntercom.Application/Widget/SendVisitorMessage.cs#L55-L62) — folds ownership into the load.
- [`VisitorHub.JoinConversation`](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L37-L46) — checks before adding to the group.

This is textbook IDOR prevention: the conversation id in the request is untrusted, the visitor id comes from the token, and the row only returns if they match.

## Resource isolation: the gaps (the senior-checks-these list)

| Gap | Where | Risk |
| --- | --- | --- |
| `externalId` is trusted from the body | [StartConversation#L51-L52](../../../src/NetIntercom.Application/Widget/StartConversation.cs#L51-L52) | **R1** — identity spoofing |
| Agents can act on any conversation | [ConversationsController](../../../src/NetIntercom.Api/Controllers/ConversationsController.cs#L15) (role only) | **R2** — no team scoping |
| Canned-reply team writes unchecked | [SaveCannedReplyHandler#L71-L97](../../../src/NetIntercom.Application/CannedReplies/CannedReplies.cs#L71-L97) | **R3** |
| `VisitorHub.Typing` skips ownership | [VisitorHub#L48-L58](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L48-L58) | **R4** |

The pattern: ownership is checked *where someone remembered to check it*. There's no central resource-authorization mechanism (no `IAuthorizationHandler`, no `[Authorize(Policy)]` with a requirement), so each new endpoint re-decides. Juniors miss these because the *coarse* `[Authorize]` is present and looks sufficient.

## Validation layers

- **Shape/binding:** the framework rejects malformed GUIDs/types at the route/binding edge.
- **Business:** FluentValidation validators run in the [ValidationBehavior](../../../src/NetIntercom.Application/Common/Behaviors.cs#L7-L32) before every handler. Examples: [StartWidgetSessionValidator](../../../src/NetIntercom.Application/Widget/StartConversation.cs#L26-L34) (email format, lengths), [SaveCannedReplyValidator](../../../src/NetIntercom.Application/CannedReplies/CannedReplies.cs#L52-L63) (shortcut charset regex).
- **Domain:** invariants throw `DomainException` from entity methods ([Conversation.Snooze](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L63-L77)) — the last line of defense, mapped to 422.

This three-tier split is a strength: a bad request can be rejected at the cheapest applicable layer. **Failure mode:** FluentValidation validates *input shape* but not *existence/authorization* — e.g., `SaveRoutingRuleValidator` requires a `TeamId` but the handler must separately check the team exists ([TeamsAndAdmin#L141-L145](../../../src/NetIntercom.Application/Teams/TeamsAndAdmin.cs#L141-L145)); its canned-reply sibling forgets to (R3/G8).

## Other security surfaces

- **Rate limiting:** per-IP fixed window — 60/min on the widget, 10/min on auth ([Program.cs#L114-L123](../../../src/NetIntercom.Api/Program.cs#L114-L123)). Mitigates brute force and widget abuse, but per-IP only (NAT/proxy sharing weakens it).
- **CORS:** inbox is origin-allow-listed; the widget allows **any** origin with credentials ([Program.cs#L99-L111](../../../src/NetIntercom.Api/Program.cs#L99-L111)) — necessary for an embeddable widget, but it means the widget API must never rely on origin for trust (it relies on the visitor token instead).
- **Password hashing:** PBKDF2 via ASP.NET Identity's hasher, without taking on full Identity ([PasswordService](../../../src/NetIntercom.Infrastructure/Services/PasswordService.cs)).

**Drill 1:** build the full authorization matrix — for each of the ~25 endpoints/hub methods: role required, resource-ownership checked (y/n), and if n, is that correct? You'll reproduce risks R1–R4 from first principles.
**Drill 2:** design the central fix for R2 — a resource-based authorization handler keyed on team membership — and decide whether the whole-inbox model is actually wrong or just undocumented.
**Self-grade:** Basic = explains the three policies and where they're applied. Solid = names the validation tiers and one ownership gap. Strong = produces the authorization matrix and proposes the centralized resource-authz design with its trade-offs.
