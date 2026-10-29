// All server access goes through Globeweigh.Api — the app never talks to the
// database. Inside the Tauri shell, requests use the Tauri HTTP plugin (proxied
// through Rust, no CORS needed). Served in a plain browser (the UI lives on the
// API's own origin at /admin) there is no Tauri IPC, so fall back to native
// fetch — which is same-origin there and needs no CORS either.
import { fetch as tauriFetch } from "@tauri-apps/plugin-http";
import { DEV_API_URL, getApiBaseUrl } from "../config";

const inTauri = typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;
const httpFetch = inTauri ? tauriFetch : window.fetch.bind(window);

let bearerToken: string | null = null;

export function setToken(token: string | null) {
  bearerToken = token;
}

// Called when the API rejects our token (expired JWT) so the app can return to login.
let onUnauthorized: (() => void) | null = null;

export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler;
}

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

async function requestTo<T>(
  baseUrl: string,
  method: string,
  path: string,
  body?: unknown,
  signal?: AbortSignal,
): Promise<T> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (bearerToken) headers["Authorization"] = `Bearer ${bearerToken}`;

  const response = await httpFetch(`${baseUrl}/api${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });

  // Dev guard: the local API stamps every response. Without the stamp, something else is
  // answering on the dev port — say so instead of surfacing that app's 404s.
  if (import.meta.env.DEV && baseUrl === DEV_API_URL && response.headers.get("X-Globeweigh-App") !== "Evergreen") {
    throw new ApiError(
      502,
      `${DEV_API_URL} is answering, but it is not the Evergreen API — another app has taken the dev port. ` +
        `Stop it and restart dev:full.`,
    );
  }

  if (!response.ok) {
    // Expired/invalid token on a non-login call: bounce back to the login screen.
    if (response.status === 401 && !path.startsWith("/auth/") && bearerToken) {
      bearerToken = null;
      onUnauthorized?.();
      throw new ApiError(401, "Your session has expired. Please sign in again.");
    }
    let message = `${response.status} ${response.statusText}`;
    try {
      const data = await response.json();
      if (data?.message) message = data.message;
    } catch {
      // non-JSON error body — keep the status text
    }
    throw new ApiError(response.status, message);
  }

  // 204 No Content (and other empty bodies) have nothing to parse.
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

// Default variant: talk to whatever API base getApiBaseUrl() resolves (the active DB target).
function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  return requestTo<T>(getApiBaseUrl(), method, path, body);
}

export { request, requestTo };
export const apiGet = <T>(path: string) => request<T>("GET", path);
export const apiPost = <T>(path: string, body: unknown) => request<T>("POST", path, body);
// Post to a specific API base (e.g. always the local API), independent of the active DB target.
// Pass a signal for long-running calls the user must be able to abandon (see the Sync Data dialog):
// aborting drops the connection, which the API sees as a cancelled request and stops work on.
export const apiPostTo = <T>(baseUrl: string, path: string, body: unknown, signal?: AbortSignal) =>
  requestTo<T>(baseUrl, "POST", path, body, signal);
export const apiPut = <T>(path: string, body: unknown) => request<T>("PUT", path, body);
