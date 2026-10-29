// A published UI release reloads the app under whoever is using it (UiRefreshPill counts
// down and refreshes). Without this they land back on the first nav page and have to find
// their way back to whatever they had open, mid-job.
//
// Ported from the TauriTouch resumeAfterRefresh (components/touch/resumeAfterRefresh.ts),
// which does the same for the kiosk's open module — same keys, same reasoning, separate
// storage keys so the two apps never read each other's.
//
// sessionStorage on purpose: it survives the reload (same tab, same origin) but NOT an app
// restart — so closing and reopening the app still starts on the default page rather than
// dropping straight back into wherever it was left days ago.
//
// Every accessor is wrapped: storage can throw, and resuming is a convenience, never a
// reason to fail an app that is otherwise fine.

const PAGE_KEY = "adminOpenPage";
const RESUME_KEY = "adminResumeAfterRefresh";

/** Record which nav page is open, so an update refresh can come back to it. */
export function rememberOpenPage(key: string | null): void {
  try {
    if (key) sessionStorage.setItem(PAGE_KEY, key);
    else sessionStorage.removeItem(PAGE_KEY);
  } catch {
    /* storage unavailable */
  }
}

/** Called immediately before an update reload, to mark this reload as resumable. */
export function markResumeAfterRefresh(): void {
  try {
    sessionStorage.setItem(RESUME_KEY, "1");
  } catch {
    /* storage unavailable */
  }
}

/**
 * The nav key to reopen, if this load came from an update refresh — and null otherwise, so
 * an ordinary start is unaffected. Clears the marker, so it only ever fires once.
 */
export function takeResumePage(): string | null {
  try {
    const resuming = sessionStorage.getItem(RESUME_KEY);
    sessionStorage.removeItem(RESUME_KEY);
    return resuming ? sessionStorage.getItem(PAGE_KEY) : null;
  } catch {
    return null;
  }
}
