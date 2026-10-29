// API base URL resolution, mirroring the desktop app's config-driven ApiBaseUrl rule:
// never hardcoded anywhere else. Override at runtime via the "apiBaseUrl" storage key
// so a deployed build can be repointed without rebuilding.
// The local dev API port lives in dev-api-port.json, shared with dev-api.mjs (which starts
// the API on it). Not 5099 (the prod API on the server box), and deliberately an odd high
// number: other projects' dev APIs squatted on 5199 and then 5299, and the Touch app's calls
// were silently answered by the wrong API (403/404s). dev-api.mjs refuses to start if a
// foreign process holds the port, and client.ts rejects any response that lacks the
// API's X-Globeweigh-App: Evergreen stamp.
import devApiPort from "../dev-api-port.json";
export const DEV_API_URL = `http://localhost:${devApiPort.port}`;
// Production must always use the server IP (not the `evergreen` hostname) — touch screens
// can't rely on NetBIOS/DNS name resolution on the LAN. The dev box reaches this IP too
// (Tailscale subnet route), so it doubles as the dev "PROD DB" target.
const PROD_API_URL = "http://192.168.250.5:5099";

// Where the runtime override lives. Deployed builds use localStorage: repointing a served
// build is a deliberate, permanent choice. Dev builds use sessionStorage instead — the
// DbSwitcher still works for the current run (sessionStorage survives its reload), but the
// choice dies with the app, so every `npm run dev` starts on the target the script declared
// via VITE_API_TARGET. A localStorage override in dev outlives the session invisibly and
// silently hijacks later runs (e.g. pointing login at a localhost API that isn't running).
const OVERRIDE_KEY = "apiBaseUrl";
const overrideStore: Storage = import.meta.env.DEV ? sessionStorage : localStorage;
// Purge any leftover permanent override written by older dev builds so it can't resurface.
if (import.meta.env.DEV) localStorage.removeItem(OVERRIDE_KEY);

export function getApiBaseUrl(): string {
  const override = overrideStore.getItem(OVERRIDE_KEY);
  if (override) return override.replace(/\/+$/, "");
  // Build-time target override: `npm run dev` sets VITE_API_TARGET=production so the
  // local dev shell runs against the live prod API; dev:full leaves it unset (localhost).
  const target = import.meta.env.VITE_API_TARGET;
  if (target === "production") return PROD_API_URL;
  if (target === "development") return DEV_API_URL;
  // Server-hosted UI: the build is served from the API's own wwwroot at <api>/touch/,
  // so the API base is simply everything before /touch in the page URL. This works for
  // any host the user reached the server by (LAN IP, hostname, Tailscale).
  const path = window.location.pathname.toLowerCase();
  const touchIdx = path.indexOf("/touch");
  if (window.location.protocol.startsWith("http") && touchIdx >= 0) {
    return window.location.origin + window.location.pathname.slice(0, touchIdx);
  }
  return import.meta.env.DEV ? DEV_API_URL : PROD_API_URL;
}

// Dev-only DB switcher: the two API targets a developer can flip between, and helpers to
// read/set the active one. Switching just writes the apiBaseUrl override (above) so it wins
// over VITE_API_TARGET and the auto-detection; the caller reloads to apply it.
export type DbTarget = "local" | "prod";

export const DB_TARGET_URLS: Record<DbTarget, string> = {
  local: DEV_API_URL,
  prod: PROD_API_URL,
};

export function getDbTarget(): DbTarget {
  return isLocalDb() ? "local" : "prod";
}

export function setDbTarget(target: DbTarget): void {
  overrideStore.setItem(OVERRIDE_KEY, DB_TARGET_URLS[target]);
}

// True when the UI is talking to a local API/DB rather than the prod server.
export function isLocalDb(): boolean {
  try {
    const host = new URL(getApiBaseUrl()).hostname.toLowerCase();
    return host === "localhost" || host === "127.0.0.1" || host === "::1";
  } catch {
    return false;
  }
}

// Dev-only: sign this factory user in automatically instead of showing the login
// screen (matched by full name against /auth/touch-users). Null in production
// builds; set to null here too if you need to exercise the real login flow in dev.
export const DEV_AUTO_LOGIN = import.meta.env.DEV
  ? { fullName: "Colin McCuskey", passcode: "1234" }
  : null;

// go2rtc video gateway base URL. go2rtc runs on the host that can reach the cameras
// (the evergreen box in prod). An explicit GateBarrier:VideoBaseUrl from the API config
// overrides this; or override at runtime via localStorage("videoBaseUrl").
const GO2RTC_PORT = 1984;

export function getVideoBaseUrl(explicit?: string | null): string {
  const override = localStorage.getItem("videoBaseUrl");
  if (override) return override.replace(/\/+$/, "");
  if (explicit && explicit.trim()) return explicit.trim().replace(/\/+$/, "");
  try {
    const api = new URL(getApiBaseUrl());
    return `${api.protocol}//${api.hostname}:${GO2RTC_PORT}`;
  } catch {
    return `http://localhost:${GO2RTC_PORT}`;
  }
}
