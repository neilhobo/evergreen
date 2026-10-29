// API base URL resolution, mirroring the desktop app's config-driven ApiBaseUrl rule:
// never hardcoded anywhere else. Override at runtime via the "apiBaseUrl" storage key
// so a deployed build can be repointed without rebuilding.
const DEV_API_URL = "http://localhost:5099";
// Production must use the server's LAN IP, not the "evergreen" hostname (that name
// only resolves from the dev PC via Tailscale; on the prod LAN it doesn't).
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
  // local dev shell runs against the live prod API; "development" forces localhost.
  // Takes precedence over the dev/served auto-detection below (but not the localStorage
  // override above).
  const target = import.meta.env.VITE_API_TARGET;
  if (target === "production") return PROD_API_URL;
  if (target === "development") return DEV_API_URL;
  // Server-hosted UI: the build is served from the API's own wwwroot at <api>/admin/,
  // so the API base is simply everything before /admin in the page URL. This works for
  // any host the user reached the server by (LAN IP, hostname, Tailscale).
  const path = window.location.pathname.toLowerCase();
  const adminIdx = path.indexOf("/admin");
  if (window.location.protocol.startsWith("http") && adminIdx >= 0) {
    return window.location.origin + window.location.pathname.slice(0, adminIdx);
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

// Switching target signs you out — see DbSwitcher for why — and the reason has to survive a full
// page reload to reach the login screen, so it is left here rather than held in React state.
const SWITCH_NOTICE_KEY = "dbSwitchNotice";

export function noteDbSwitch(target: DbTarget): void {
  sessionStorage.setItem(SWITCH_NOTICE_KEY, target);
}

// The notice for the login screen, if the reload we just came through was a DB switch. Reading it
// clears it, so it is shown once and does not reappear on the next refresh.
export function takeDbSwitchNotice(): string | null {
  const target = sessionStorage.getItem(SWITCH_NOTICE_KEY);
  if (!target) return null;
  sessionStorage.removeItem(SWITCH_NOTICE_KEY);
  return `Now pointed at the ${target === "local" ? "local" : "PROD"} database — please sign in again.`;
}

// True when the UI is talking to a local API/DB — i.e. running on a dev PC against
// localhost rather than the prod "evergreen" host. Drives the "Local DB" badge so a
// dev build can't be mistaken for production. The connection string is local
// ((local)\SQLEXPRESS) in both environments, so the only client-visible signal is
// which host the UI reached the API by.
export function isLocalDb(): boolean {
  try {
    const host = new URL(getApiBaseUrl()).hostname.toLowerCase();
    return host === "localhost" || host === "127.0.0.1" || host === "::1";
  } catch {
    return false;
  }
}

// VNC bridge (GlobeweighService :8765 /vnc) host resolution. The remote-screen WebSocket
// is a DIRECT browser→service connection (unlike live weight, which the API proxies), so
// this machine must resolve the host itself. The "evergreen" hostname doesn't resolve from a
// dev PC, so in dev use the server's LAN IP (the dev PC is on the 192.168.250.x LAN, same as
// PROD_API_URL). Override for any client via localStorage("vncWsHost") = host or host:port
// (e.g. set it to the Tailscale IP 100.67.163.114:8765 when developing off-site).
const DEV_VNC_HOST = "192.168.250.5:8765"; // evergreen LAN IP

export function resolveVncWsUrl(wsUrl: string): string {
  const host = localStorage.getItem("vncWsHost") || (import.meta.env.DEV ? DEV_VNC_HOST : null);
  // Swap the authority (host[:port]) while keeping scheme, path and query.
  return host ? wsUrl.replace(/\/\/[^/]+/, `//${host}`) : wsUrl;
}

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
