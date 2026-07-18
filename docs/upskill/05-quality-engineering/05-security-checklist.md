# Security Checklist

Tied to real code. Use the pre-merge checklist at the bottom on every PR that touches an endpoint, a handler, or auth.

## Surface-by-surface

### Authorization / IDOR
- **Resource ownership in the query, not after-the-fact.** Visitor reads are scoped by `VisitorId` ([WidgetQueries#L25-L30](../../../src/NetIntercom.Application/Widget/WidgetQueries.cs#L25-L30)). ✅ for REST.
- **Known gaps:** `externalId` trusted from body (**R1**, [StartConversation#L51-L52](../../../src/NetIntercom.Application/Widget/StartConversation.cs#L51-L52)); agents act on any conversation (**R2**); canned-reply team writes unchecked (**R3**); `VisitorHub.Typing` skips ownership (**R4**, [VisitorHub#L48-L58](../../../src/NetIntercom.Api/Realtime/VisitorHub.cs#L48-L58)). Hub methods need the *same* checks as REST — easy to forget.

### Authentication / tokens
- 15-min access JWTs; refresh tokens **hashed** at rest and rotated ([Refresh#L43-L52](../../../src/NetIntercom.Application/Auth/Refresh.cs#L43-L52)). ✅
- **Exposures (R8):** refresh token in `localStorage` (XSS → 14-day theft); access + 30-day visitor tokens irrevocable (no deny-list); hub token in query string (proxy logs). App refuses to start without a signing key ([AddInfrastructure#L16-L21](../../../src/NetIntercom.Infrastructure/DependencyInjection.cs#L16-L21)). ✅
- Dev signing key is committed but clearly dev-only ([appsettings.Development.json](../../../src/NetIntercom.Api/appsettings.Development.json)); production must use env/user-secrets.

### Input validation
- FluentValidation on every command ([ValidationBehavior](../../../src/NetIntercom.Application/Common/Behaviors.cs#L7-L32)); length caps on message bodies (10k), shortcuts (charset regex). ✅
- **Gap:** validators check shape, not existence — `SaveCannedReply` doesn't verify the team exists → FK violation → 500 instead of 404 (**G8/R3**).

### SQL injection
- **Not reachable** — everything goes through EF LINQ / parameterized queries; no raw SQL string concatenation anywhere. ✅ (If anyone adds `FromSqlRaw`, this changes — flag it in review.)

### XSS
- React escapes by default; no `dangerouslySetInnerHTML` in either frontend (grep to confirm before trusting). Message bodies render as text ([Inbox.tsx#L303](../../../frontend/inbox/src/pages/Inbox.tsx#L303), [Widget.tsx#L127](../../../frontend/widget/src/Widget.tsx#L127)). ✅
- Server-side placeholder rendering ([PlaceholderRenderer](../../../src/NetIntercom.Application/Common/PlaceholderRenderer.cs)) substitutes plain strings into reply bodies that are then rendered as text — no HTML interpolation. ✅
- **Watch:** `attributesJson` is parsed and `JSON.stringify`'d into a `<pre>` ([Inbox.tsx#L364-L424](../../../frontend/inbox/src/pages/Inbox.tsx#L364-L424)) — text, safe; but any future feature that renders visitor-supplied content as HTML reopens this.

### CSRF
- Bearer-token auth (not cookies) means classic CSRF doesn't apply to the API. The trade-off is the XSS/token-storage exposure above (see [comparison card 3](../03-architecture-and-patterns/07-comparative-implementations.md)). If anyone moves to cookie auth, antiforgery becomes mandatory.

### SSRF / open redirect
- **No outbound HTTP from user input** — no `HttpClient` calls, no webhooks, no URL fetching. `SourceUrl` is stored and displayed, never fetched ([Conversation.SourceUrl](../../../src/NetIntercom.Domain/Entities/Conversation.cs#L21)). ✅ The `api.ts` `window.location.href = "/login"` ([api.ts#L48](../../../frontend/inbox/src/api.ts#L48)) is a fixed path, not user-controlled. ✅

### Secrets & dependency risk
- Secrets via config/env, fail-fast on missing key. ✅
- Dependencies pinned per-csproj, mid-2024 versions; no `Directory.Packages.props` so version drift is possible but currently consistent. Run `dotnet list package --vulnerable` (inferred — not run at authoring) before a real release.

### Uploads / file handling
- **None** — no file upload endpoints. One less surface. ✅

### Webhooks
- **None** inbound or outbound. ✅

### Rate limiting
- Per-IP fixed window: 60/min widget, 10/min auth ([Program.cs#L114-L123](../../../src/NetIntercom.Api/Program.cs#L114-L123)). ✅ for brute-force/abuse basics.
- **Limits:** per-IP only (shared NAT weakens it); no rate limit on authenticated agent endpoints; the hub methods aren't rate-limited (a visitor could spam `Typing`).

### CORS
- Inbox origin-allow-listed; widget allows any origin with credentials ([Program.cs#L99-L111](../../../src/NetIntercom.Api/Program.cs#L99-L111)) — necessary for an embeddable widget, which is why the widget API must trust the *token*, never the origin.

## Pre-merge security checklist

Copy into the PR description; check honestly.

- [ ] Every new data access scoped to the caller's resource (visitor id / team), in the query — not after load.
- [ ] New hub methods repeat the ownership checks their REST equivalents have.
- [ ] No identity/authorization decision trusts request-body fields (claims only).
- [ ] New commands have a FluentValidation validator; existence checks throw `NotFoundException`, not FK-violation 500s.
- [ ] No `FromSqlRaw`/string-built SQL; no `dangerouslySetInnerHTML`.
- [ ] No new outbound HTTP from user-controlled input (SSRF).
- [ ] DTOs returned to a given audience don't leak fields that audience shouldn't see (visitor vs agent).
- [ ] Secrets from config/env, never hard-coded; no token logged.
- [ ] Rate-limit or abuse story for any new public/anonymous endpoint.
- [ ] `dotnet list package --vulnerable` clean if dependencies changed.

**Drill:** run the full checklist against the *existing* `WidgetController` + `VisitorHub` as if it were a fresh PR. You should independently rediscover R1 and R4. That's the checklist working.
**Self-grade:** Basic = can apply the checklist to a simple new endpoint. Solid = catches a missing ownership scope. Strong = catches the hub-vs-REST asymmetry and the DTO-leak-to-visitor class without the hints.
