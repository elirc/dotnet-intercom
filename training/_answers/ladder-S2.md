# Answer: S2 workspace multi-tenancy (reference design + rubric)

## Isolation model
Recommended: **row-level `WorkspaceId` on every tenant-owned table + EF Core global query filters + Postgres RLS as the fail-closed backstop.** Schema-per-tenant is overkill at 2,000 tenants on EF Core (model caching per schema, 2,000× migrations). Database-per-tenant is justified only for a few regulated enterprise tenants. Keep it as a later "dedicated" tier.

- EF filter: `modelBuilder.Entity<Conversation>().HasQueryFilter(c => c.WorkspaceId == _tenant.WorkspaceId)` for each entity. `CannedReply` already has a filter (`AppDbContext.cs:113`), so **combine** it (`!IsDeleted && WorkspaceId == …`). EF 8 allows only one filter per entity, so a second `HasQueryFilter` call silently *replaces* the first. That's a real trap.
- RLS: `ALTER TABLE … ENABLE ROW LEVEL SECURITY; CREATE POLICY ws ON … USING ("WorkspaceId" = current_setting('app.workspace_id')::uuid)`. Set it per connection/transaction via a `DbConnectionInterceptor` (`SET app.workspace_id = …`). Pooling means you must reset it (or use `SET LOCAL` in a transaction). If the setting is missing, `current_setting(..., true)` returns NULL → the predicate is false → **fail closed**.

## How the workspace reaches the handler
Agent JWT gains a `ws` claim (`TokenService.CreateAgentTokens`). The visitor token gets a `ws` claim from the workspace key in the widget snippet. `ICurrentUser.WorkspaceId`, then a scoped `ITenantContext` read by `AppDbContext`. **Background jobs:** `SlaBreachJob` has no HTTP context. Either iterate workspaces explicitly with a scoped tenant context per workspace, or run the sweep as a platform job that bypasses filters (`IgnoreQueryFilters`) and carries the `WorkspaceId` on each row it touches into the notifier. That bypass is the "one more thing" the ticket asks about: `IServiceScopeFactory` scopes in `PresenceSweeper` and Hangfire have no user, so an `ITenantContext` built from `IHttpContextAccessor` returns null there. Decide: throw (fail closed) unless the code is explicitly marked as platform-scoped.

## Indexes and uniqueness
`AgentUsers.Email` → unique per workspace `(WorkspaceId, Email)` (or keep it global if one login spans workspaces, a product decision). `Visitors.AnonymousId` → `(WorkspaceId, AnonymousId)`: two workspaces on the same site read the same cookie, so a global unique index would merge their visitors. All hot indexes get `WorkspaceId` as the leading column.

## Realtime
Groups get prefixes: `ws:{id}:agents`, `ws:{id}:visitors`. `conv:{id}` is safe only if every join is authorized against the workspace. Add the check to `AgentHub.JoinConversation`, which today does none (`AgentHub.cs:50-58`). `PresenceSweeper` broadcasts per workspace, and presence keys are namespaced.

## Widget identity
The snippet carries a public `workspaceKey`. Identified users need `userHash = HMAC-SHA256(externalId, workspaceSecret)`, computed by the customer's backend. Without a valid hash, ignore `externalId` (fixes review R1). For 30 days, old snippets without `workspaceKey` map to workspace #1 through an allow-list of known origins.

## Roles
Split into `platform-operator` (the Hangfire dashboard, cross-tenant support) and `workspace-admin` (routing, SLA, office hours). The current `Admin` policy maps to workspace-admin.

## Migration (expand → backfill → enforce), zero downtime
1. The baseline migration exists (S1 prerequisite). 2. Add nullable `WorkspaceId` columns and the `Workspaces` table, and insert workspace #1. 3. Deploy code that **writes** `WorkspaceId` on inserts (still reading without filters). 4. Backfill in batches (`UPDATE … WHERE WorkspaceId IS NULL LIMIT …` loops) to #1. 5. Validate a `NOT NULL` constraint (`ADD CONSTRAINT … CHECK (…) NOT VALID` then `VALIDATE CONSTRAINT`, which avoids a long lock). 6. Deploy the filters, then RLS. 7. Swap the unique indexes concurrently.

## Tests
A cross-tenant suite: seed workspaces A and B, and for **every** endpoint and hub method call B's ids with A's token → 404/403. Generate the matrix from the route table so new endpoints can't skip it. A unit test asserts every entity implementing `ITenantOwned` has a filter. An RLS test runs raw SQL without a workspace setting and expects 0 rows.

Rubric: Basic = WorkspaceId + filters. Solid = + claims, jobs without an HTTP context, unique-index changes, group naming. Strong = + fail-closed RLS, the single-query-filter trap, the expand/backfill/enforce sequence, the HMAC widget identity, a generated cross-tenant test matrix.
