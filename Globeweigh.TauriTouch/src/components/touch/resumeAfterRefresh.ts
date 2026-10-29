// A published UI release reloads the screen under whoever is standing at it (UiRefreshPill
// counts down and navigates). Without this they land back on the Home menu and have to find
// their way back to whatever they were doing, mid-shift.
//
// sessionStorage on purpose: it survives the reload (same tab, same origin, even through
// location.replace to a cache-busting URL) but NOT an app restart — so a power cycle or a
// crash still brings the screen up on Home rather than straight back into a module.
//
// Every accessor is wrapped: storage can throw, and resuming is a convenience, never a
// reason to fail a screen that is otherwise fine.

const MODULE_KEY = "touchOpenModule";
const RESUME_KEY = "touchResumeAfterRefresh";

/** Record what is open, so an update refresh can come back to it. */
export function rememberOpenModule(module: string | null): void {
  try {
    if (module) sessionStorage.setItem(MODULE_KEY, module);
    else sessionStorage.removeItem(MODULE_KEY);
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
 * The module to reopen, if this load came from an update refresh — and null otherwise, so
 * an ordinary start is unaffected. Clears the marker, so it only ever fires once.
 */
export function takeResumeModule(): string | null {
  try {
    const resuming = sessionStorage.getItem(RESUME_KEY);
    sessionStorage.removeItem(RESUME_KEY);
    return resuming ? sessionStorage.getItem(MODULE_KEY) : null;
  } catch {
    return null;
  }
}
