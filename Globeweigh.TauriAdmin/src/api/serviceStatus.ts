import { useSyncExternalStore } from "react";
import { probeApi, setOutageHandler } from "./client";
import { getApiBaseUrl } from "../config";
import { markResumeAfterRefresh } from "../components/resumeAfterRefresh";

// Deploy-time outage detection.
//
// An API deploy drops app_offline.htm on the server, and for the length of the copy IIS
// answers EVERYTHING with a 503 — every API call, and the /admin files too. Without this an
// open app just falls apart quietly: empty grids, "503 Service Unavailable" toasts, a
// sign-in that fails for no visible reason.
//
// How it works:
//   - every call to the active API is watched (client.ts watchedFetch); a 502/503/504 or a
//     dead connection is treated as *suspicious*, not as an outage;
//   - one probe decides. If the API answers, nothing is shown — so a single failing endpoint
//     never blanks the app over a real bug;
//   - if it doesn't, the overlay goes up and the API is polled until the new build answers,
//     at which point the page reloads itself — back on the page it was on, still signed in
//     (session + resumeAfterRefresh), and on the UI that shipped alongside the new API.
//
// Also handles an open tab whose lazily-loaded chunks a deploy has replaced (the old hashed
// files are gone): reload once, or wait out the deploy first if that is why it failed.
//
// Install once, from ServiceStatusOverlay.

const POLL_MS = 4000;
/** Don't re-probe on every failing request in a burst. */
const PROBE_COOLDOWN_MS = 3000;
const PROBE_TIMEOUT_MS = 6000;

export interface ServiceStatus {
  /** The API is confirmed unreachable — the overlay is showing. */
  down: boolean;
  /** A probe is in flight. */
  probing: boolean;
  /** The API answered again; the page is about to reload. */
  recovering: boolean;
  /** When the outage was confirmed, for the elapsed timer and "taking longer" copy. */
  downSince: number | null;
}

let state: ServiceStatus = { down: false, probing: false, recovering: false, downSince: null };
const listeners = new Set<() => void>();

function setState(patch: Partial<ServiceStatus>) {
  state = { ...state, ...patch };
  listeners.forEach((l) => l());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useServiceStatus(): ServiceStatus {
  return useSyncExternalStore(subscribe, () => state);
}

let installed = false;
let pollTimer: number | null = null;
let lastProbeAt = 0;

async function probe(): Promise<boolean> {
  setState({ probing: true });
  try {
    return await probeApi(getApiBaseUrl(), PROBE_TIMEOUT_MS);
  } finally {
    lastProbeAt = Date.now();
    setState({ probing: false });
  }
}

/**
 * A request looked like an outage. Confirm with a probe before showing anything — a single
 * failing endpoint is a bug, not a deploy. Resolves true when the API is confirmed down.
 */
async function reportSuspectedOutage(): Promise<boolean> {
  if (state.down) return true;
  if (state.probing || Date.now() - lastProbeAt < PROBE_COOLDOWN_MS) return false;
  if (await probe()) return false;
  goDown();
  return true;
}

function goDown() {
  if (state.down) return;
  setState({ down: true, downSince: Date.now() });
  pollTimer = window.setInterval(async () => {
    if (state.recovering || state.probing) return;
    if (await probe()) recover();
  }, POLL_MS);
}

function recover() {
  if (pollTimer !== null) {
    window.clearInterval(pollTimer);
    pollTimer = null;
  }
  setState({ recovering: true });
  // Full reload rather than resuming in place: the page has been sitting on half-loaded
  // data, and the new API build may not be the one it started against.
  window.setTimeout(() => {
    markResumeAfterRefresh();
    window.location.reload();
  }, 600);
}

/** "Try now" — probe immediately instead of waiting for the next tick. */
export async function retryNow(): Promise<void> {
  if (state.recovering || state.probing) return;
  if (await probe()) recover();
}

// ---- chunk load errors: this tab's bundle was deployed over ---------------------------

const CHUNK_RELOAD_KEY = "adminChunkReloadAt";

function looksLikeChunkError(message?: string): boolean {
  return (
    !!message &&
    /Failed to fetch dynamically imported module|error loading dynamically imported module|Importing a module script failed|Unable to preload CSS/i.test(
      message,
    )
  );
}

let chunkHandling = false;

async function onChunkError() {
  if (chunkHandling || state.down) return;
  chunkHandling = true;
  try {
    // Mid-deploy the chunk 503'd along with everything else: the overlay's own reload
    // picks up the new bundle when it is back, so just show it.
    if (!(await probe())) {
      goDown();
      return;
    }
    // Server is fine, so the file is gone — the UI was redeployed under this tab. Guarded:
    // a chunk that is genuinely broken must not put the tab in a reload loop.
    try {
      const last = Number(sessionStorage.getItem(CHUNK_RELOAD_KEY) || 0);
      if (Date.now() - last < 30_000) return;
      sessionStorage.setItem(CHUNK_RELOAD_KEY, String(Date.now()));
    } catch {
      return;
    }
    markResumeAfterRefresh();
    window.location.reload();
  } finally {
    chunkHandling = false;
  }
}

// ---- install --------------------------------------------------------------------------

export function installServiceStatusMonitor() {
  if (installed) return;
  installed = true;

  setOutageHandler(() => void reportSuspectedOutage());

  // Vite's own signal for a failed lazy import/preload; the rejection catches the rest.
  window.addEventListener("vite:preloadError", () => void onChunkError());
  window.addEventListener("unhandledrejection", (e) => {
    const reason = e.reason as { message?: string } | undefined;
    if (looksLikeChunkError(reason?.message)) void onChunkError();
  });
}
