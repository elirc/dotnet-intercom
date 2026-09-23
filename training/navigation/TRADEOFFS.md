# Senior trade-offs: four decisions in NetIntercom

Format for each: Problem → naive approach → where it fails → options → trade-offs → choice → failure modes → how it's tested → how it's monitored → when to revisit.

## 1. Hubs are delivery-only, and the DB is the source of truth
- **Problem:** chat messages must be durable and ordered, and must show up live on two kinds of client.
- **Naive:** send messages over the hub (`hub.invoke("Send", …)`) and persist inside the hub method.
- **Where it fails:** hub methods have no HTTP semantics (status codes, validation pipeline, rate limiting, idempotency). A lost socket loses the message. Tests need a hub client.
- **Options:** A) write over the hub. B) write over REST, push over the hub (chosen: `SendVisitorMessage.cs:107-112`). C) REST plus a transactional outbox that feeds the hub.
- **Trade-offs:** B reuses the MediatR pipeline and REST tooling, but a push can be lost after the commit. C closes that gap at the cost of a table, a dispatcher and latency.
- **Choice:** B, with reconnect catch-up via `?after=` (`WidgetQueries.cs`, `GetConversation.cs:41-59`).
- **Failure modes:** a missed push while still connected (no catch-up is triggered). Catch-up is broken on the widget (lost group membership, stale closure).
- **Tested by:** nothing automated for pushes. **Monitored by:** nothing (no metric for push failures).
- **Revisit when:** you add webhooks or email notifications (a second consumer of "message created"), or when you scale out (S1).

## 2. Presence in memory with grace periods
- **Problem:** routing needs "who's online now", and the UI mustn't flicker on page refresh.
- **Naive:** online = has a connection.
- **Fails:** refreshes and proxy cuts flap agents offline, so routing skips them.
- **Options:** A) in-process tracker with a grace period (chosen). B) Redis keys with a TTL refreshed by heartbeats. C) DB rows with `LastSeenAt`.
- **Trade-offs:** A has zero dependencies and is fast, but it's per process, lost on restart and invisible to other nodes. B is shared and survives node death via TTL, but adds Redis and needs clock-free semantics. C is durable but puts heartbeat write load on Postgres.
- **Choice:** A, behind `IPresenceService` so B can drop in.
- **Failure modes:** multi-node (S1), restart (everyone is "offline" until they reconnect), and the grace-period dependency on the state object (incident 002).
- **Tested by:** nothing (no `IClock`). **Monitored by:** `PresenceSnapshot` rows, which are written but never read.
- **Revisit when:** there's more than one replica, or when presence feeds billing or analytics.

## 3. SLA breaches detected by a one-minute polling sweep
- **Problem:** mark timers breached and escalate, and survive restarts.
- **Naive:** `Task.Delay(until breach)` in memory per timer.
- **Fails:** lost on restart and deploy, and it doesn't scale.
- **Options:** A) a recurring sweep over an indexed `(Status, BreachAtUtc)` (chosen: `CheckSlaBreaches.cs`, `AppDbContext.cs:97`). B) one delayed Hangfire job per timer (`BackgroundJob.Schedule`), cancelled on reply. C) Postgres `LISTEN/NOTIFY` or a scheduler service.
- **Trade-offs:** A is simple, self-healing and idempotent *if* written set-based, with up to 60 s latency and a scan per minute. B is precise, but you must cancel or ignore stale jobs, and 200k scheduled jobs load Hangfire storage. C means more infrastructure.
- **Choice:** A. It's the right default.
- **Failure modes:** overlapping runs double-notify (no concurrency token), a late reply counts as met (`MarkMet` ignores the deadline), notify-after-commit loses escalations on a crash.
- **Tested by:** only domain unit tests of `MarkBreached`. **Monitored by:** a warning log per breach.
- **Revisit when:** escalations leave the system (pager/email: S3), or when latency under 60 s is sold as a feature.

## 4. Two separate frontends against one API
- **Problem:** a visitor widget embedded on third-party sites and an agent SPA have opposite constraints.
- **Naive:** one React app with two routes.
- **Fails:** the widget would ship the inbox's dependencies (React Query, router, forms) to every customer's page. It needs a single IIFE with injected CSS (`styles.ts`) and `all: initial` isolation. Its CORS and auth models differ too (any origin, visitor token vs. configured origins, agent JWT: `Program.cs:98-111`).
- **Options:** A) two apps (chosen). B) a monorepo with a shared types package. C) one app with separate build entries.
- **Trade-offs:** A has independent release cadence and bundle budgets, but duplicates DTO types (`widget/src/api.ts` vs `inbox/src/types.ts`) that can drift. B removes the drift but needs workspace tooling.
- **Failure modes:** the API changes a DTO and the widget, cached on customer sites for days, breaks. Backward compatibility is load-bearing for the widget, not for the inbox.
- **Tested by:** CI builds both apps (`ci.yml:25-40`). There are no contract tests. **Monitored by:** nothing client-side.
- **Revisit when:** a third client appears (mobile SDK), or the DTOs drift and cause a bug. Then generate types from OpenAPI.
