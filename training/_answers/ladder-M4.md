# Answer: M4 agents logged out about every 15 minutes

1. **Timeline.** At T+15 min the token expires. The inbox fires GET `/conversations`, GET `/agents` and GET `/canned-replies` at once (a realtime invalidation or the 30 s poll). All three get 401. Each call sees `refreshToken = R1` and POSTs `/auth/refresh {R1}`. Refresh #1 revokes R1 and returns `(A2, R2)` → `setTokens`. Refresh #2 arrives with R1, now revoked (`Refresh.cs:38-41`) → 401 → the client calls `setTokens(null)` **and wipes R2 too**, then redirects to `/login`. Each refresh reads the module-level `refreshToken` when it builds its request (`api.ts:42`). Only requests whose 401 arrives *before* refresh #1's response still send R1, and they're the ones that fail. That timing dependence is why the logout is intermittent.
2. **Hard redirect.** `window.location.href = "/login"` reloads the SPA: the SignalR connection drops, React Query's cache is gone, the composer text is lost, and the agent's presence goes through a disconnect. Use router navigation and keep the draft (e.g. `sessionStorage` per conversation).
3. **With reuse detection**, the second refresh (presenting R1 after rotation) looks like **token theft**. The server would revoke the whole family, including R2, and log a security event. The client bug would then produce false security alerts and guaranteed logouts. Fix the client before hardening the server.
4. **Module-level promise.** An ES module is evaluated once per page load. StrictMode double-invokes effects, not module code, so a module-level single-flight is correct. HMR can re-evaluate `api.ts` in dev only, which is harmless. Reset `refreshing = null` in `finally`.
5. **`accessTokenFactory`** is called on negotiate/connect and on each reconnect attempt, not per message. An open WebSocket stays authenticated past token expiry (ASP.NET Core checks at connect time, unless `CloseOnAuthenticationExpiration = true`). On reconnect after expiry, the factory returns the **expired** access token (`realtime.ts:11`), so the reconnect gets 401 until some REST call refreshes. Better: make the factory async and ensure a fresh token (call the same single-flight refresh if expired).

## Reference fix
```ts
let refreshing: Promise<boolean> | null = null;
function refreshOnce() {
  refreshing ??= (async () => {
    const r = await fetch(`${BASE}/auth/refresh`, {...});
    if (!r.ok) return false;
    setTokens(await r.json());
    return true;
  })().finally(() => { refreshing = null; });
  return refreshing;
}
// on 401: if (await refreshOnce()) response = await doFetch(); else logout();
```
Test: mock `fetch` so the first calls to 3 endpoints return 401, `/auth/refresh` resolves after a delay, and the retries return 200. Assert the refresh count is 1 and all 3 promises resolve.

Rubric: Basic = single-flight. Solid = + correct interleaving explanation and a test. Strong = + reuse-detection interaction, hub token expiry, no hard reload.
