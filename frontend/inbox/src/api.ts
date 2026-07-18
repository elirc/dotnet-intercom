import type { AuthResult } from "./types";

const BASE = "/api/v1";

let accessToken: string | null = sessionStorage.getItem("accessToken");
let refreshToken: string | null = localStorage.getItem("refreshToken");

export function setTokens(auth: AuthResult | null) {
  accessToken = auth?.accessToken ?? null;
  refreshToken = auth?.refreshToken ?? null;
  if (auth) {
    sessionStorage.setItem("accessToken", auth.accessToken);
    localStorage.setItem("refreshToken", auth.refreshToken);
  } else {
    sessionStorage.removeItem("accessToken");
    localStorage.removeItem("refreshToken");
  }
}

export function getAccessToken() {
  return accessToken;
}

/** Fetch wrapper: bearer auth + transparent one-shot refresh on 401. */
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const doFetch = () =>
    fetch(`${BASE}${path}`, {
      ...init,
      headers: {
        "Content-Type": "application/json",
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
        ...init.headers,
      },
    });

  let response = await doFetch();

  if (response.status === 401 && refreshToken) {
    const refreshed = await fetch(`${BASE}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
    });
    if (refreshed.ok) {
      setTokens((await refreshed.json()) as AuthResult);
      response = await doFetch();
    } else {
      setTokens(null);
      window.location.href = "/login";
    }
  }

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail ?? problem?.title ?? `Request failed (${response.status})`);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export const post = <T>(path: string, body: unknown) =>
  api<T>(path, { method: "POST", body: JSON.stringify(body) });
export const put = <T>(path: string, body: unknown) =>
  api<T>(path, { method: "PUT", body: JSON.stringify(body) });
export const del = (path: string) => api<void>(path, { method: "DELETE" });
