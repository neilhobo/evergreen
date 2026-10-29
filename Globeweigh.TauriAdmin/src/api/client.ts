// All server access goes through Globeweigh.Api — the app never talks to the
// database. Inside the Tauri shell, requests use the Tauri HTTP plugin (proxied
// through Rust, no CORS needed). Served in a plain browser (the UI lives on the
// API's own origin at /admin) there is no Tauri IPC, so fall back to native
// fetch — which is same-origin there and needs no CORS either.
import { fetch as tauriFetch } from "@tauri-apps/plugin-http";
import { getApiBaseUrl } from "../config";

const inTauri = typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;
const httpFetch = inTauri ? tauriFetch : window.fetch.bind(window);

let bearerToken: string | null = null;

export function setToken(token: string | null) {
  bearerToken = token;
}

// Current JWT — for the few places that can't send an Authorization header
// (e.g. the WebSocket VNC proxy, which passes it as ?access_token=).
export function getToken(): string | null {
  return bearerToken;
}

// Called when the API rejects our token (expired JWT) so the app can return to login.
let onUnauthorized: (() => void) | null = null;

export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler;
}

// Called when a request looks like the API has gone away (a deploy, most often) — see
// serviceStatus.ts, which confirms it with a probe before covering the app.
let onSuspectedOutage: (() => void) | null = null;

export function setOutageHandler(handler: (() => void) | null) {
  onSuspectedOutage = handler;
}

// What IIS answers while the API is down: 503 from app_offline.htm during a deploy, 502/504
// while ANCM restarts the process. A plain 500 is an API bug, not an outage.
export function isServiceDownStatus(status: number): boolean {
  return status === 502 || status === 503 || status === 504;
}

// httpFetch, reporting outage-shaped failures. Only for the active target: a call to the
// other API (Sync Data's local post) failing says nothing about the one the app runs on.
async function watchedFetch(baseUrl: string, url: string, init: RequestInit): Promise<Response> {
  const watch = baseUrl === getApiBaseUrl();
  let response: Response;
  try {
    response = await httpFetch(url, init);
  } catch (err) {
    // Network-level failure: server unreachable. A user-abandoned call is not one.
    if (watch && !init.signal?.aborted) onSuspectedOutage?.();
    throw err;
  }
  if (watch && isServiceDownStatus(response.status)) onSuspectedOutage?.();
  return response;
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
  // Names this app on every request so the audit trail can say WHERE a change came from, not
  // just who made it. A hint, not a credential — the server only ever files it under Source.
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    "X-Client": "TauriAdmin",
  };
  if (bearerToken) headers["Authorization"] = `Bearer ${bearerToken}`;

  const response = await watchedFetch(baseUrl, `${baseUrl}/api${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });

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

// Raw binary upload (e.g. an APK). Sends the bytes as the request body with an
// octet-stream content type — works in both the Tauri shell and a plain browser.
async function upload<T>(path: string, bytes: Uint8Array): Promise<T> {
  const headers: Record<string, string> = { "Content-Type": "application/octet-stream" };
  if (bearerToken) headers["Authorization"] = `Bearer ${bearerToken}`;

  const response = await watchedFetch(getApiBaseUrl(), `${getApiBaseUrl()}/api${path}`, {
    method: "POST",
    headers,
    body: bytes,
  });
  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    try {
      const data = await response.json();
      if (data?.message) message = data.message;
    } catch {
      // non-JSON error body — keep the status text
    }
    throw new ApiError(response.status, message);
  }
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

// True when an HTTP response comes back from `baseUrl` within the timeout — the status
// mostly doesn't matter (a 404 still proves the server is up); a network error/timeout, or
// IIS's 502/503/504 while the API is deploying or restarting, means unreachable. Used by the
// login page to surface a wrong/dead API target BEFORE the user burns time on sign-in
// attempts, and by serviceStatus.ts to tell when a deploy is over. Bypasses watchedFetch.
export async function probeApi(baseUrl: string, timeoutMs = 4000): Promise<boolean> {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), timeoutMs);
  try {
    const response = await httpFetch(`${baseUrl}/api`, {
      method: "GET",
      signal: ctl.signal,
      cache: "no-store",
    });
    return !isServiceDownStatus(response.status);
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
  }
}

// Raw binary download (a PDF to show or save): the response body as a Blob. Errors come back
// the same way as request(), so a failed render reads as its message rather than a broken file.
async function download(path: string): Promise<Blob> {
  const headers: Record<string, string> = {};
  if (bearerToken) headers["Authorization"] = `Bearer ${bearerToken}`;

  const response = await watchedFetch(getApiBaseUrl(), `${getApiBaseUrl()}/api${path}`, {
    method: "GET",
    headers,
  });
  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    try {
      const data = await response.json();
      if (data?.message) message = data.message;
    } catch {
      // non-JSON error body — keep the status text
    }
    throw new ApiError(response.status, message);
  }
  // The Tauri shell's fetch hands back a Blob with no type. A blob: URL of that shows in a
  // frame as the file's bytes, as text — so the type comes from the response header.
  const blob = await response.blob();
  const type = (response.headers.get("Content-Type") ?? "").split(";")[0].trim();
  return type && blob.type !== type ? new Blob([blob], { type }) : blob;
}

export { request, requestTo };
export const apiGet = <T>(path: string) => request<T>("GET", path);
export const apiDownload = (path: string) => download(path);
export const apiPost = <T>(path: string, body: unknown) => request<T>("POST", path, body);
// Post to a specific API base (e.g. always the local API), independent of the active DB target.
// Pass a signal for long-running calls the user must be able to abandon (see the Sync Data dialog):
// aborting drops the connection, which the API sees as a cancelled request and stops work on.
export const apiPostTo = <T>(baseUrl: string, path: string, body: unknown, signal?: AbortSignal) =>
  requestTo<T>(baseUrl, "POST", path, body, signal);
export const apiPut = <T>(path: string, body: unknown) => request<T>("PUT", path, body);
export const apiUpload = <T>(path: string, bytes: Uint8Array) => upload<T>(path, bytes);
