// API base URL resolution. Override at runtime via localStorage("apiBaseUrl") so a
// deployed build can be repointed without rebuilding.
const DEV_API_URL = "http://localhost:5099";

export function getApiBaseUrl(): string {
  const override = localStorage.getItem("apiBaseUrl");
  if (override) return override.replace(/\/+$/, "");

  // Server-hosted UI: the build is served from the API's own wwwroot at <api>/gate-web/,
  // so the API base is everything before /gate-web in the page URL.
  const path = window.location.pathname.toLowerCase();
  const idx = path.indexOf("/gate-web");
  if (window.location.protocol.startsWith("http") && idx >= 0) {
    return window.location.origin + window.location.pathname.slice(0, idx);
  }
  return DEV_API_URL;
}

// go2rtc video gateway base URL. go2rtc runs on the SAME host as the API in each environment
// (localhost in dev, the prod box in prod), so derive it from the API host on port 1984.
// An explicit GateBarrier:VideoBaseUrl from the API config overrides this. Or override at
// runtime via localStorage("videoBaseUrl").
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
