import { useEffect, useRef, useState } from "react";
import { markResumeAfterRefresh } from "./touch/resumeAfterRefresh";

const CHECK_INTERVAL_MS = 30 * 1000;
const IDLE_BEFORE_POPUP_MS = 60 * 1000;
const POPUP_COUNTDOWN_SECONDS = 20;

// Server-published UI update notifier. The UI is served from the server
// (deploy-touch-ui.ps1 just copies files), so a running screen polls the
// version.json deployed beside it and compares against its own baked-in
// __BUILD_ID__. When a newer build is on the server:
//   - a floating update pill appears bottom-left (tap to refresh), and
//   - once the screen has been idle (no touch/mouse) for 60s, a full-screen
//     popup announces the update and auto-refreshes after a 20s countdown.
//     Once the popup is showing it is committed — activity no longer dismisses
//     it; the user must wait out the countdown or tap Update now.
// Kiosk variant of the TauriAdmin sidebar card: same polling/idle/countdown
// logic, repositioned as a fixed floating pill since Touch has no sidebar.
// location.reload() can re-serve a heuristically-cached page in WebView2, leaving the
// pill "doing nothing". Navigating to index.html with a fresh query string is a URL the
// cache has never seen, and that path always carries no-cache headers from the server.
function hardRefresh() {
  // Let the fresh page reopen whatever module this one was showing.
  markResumeAfterRefresh();
  window.location.replace(`index.html?u=${Date.now()}`);
}

export default function UiRefreshPill() {
  const [available, setAvailable] = useState(false);
  const [countdown, setCountdown] = useState<number | null>(null); // null = popup hidden
  const lastActivity = useRef(Date.now());

  // --- poll version.json ---
  useEffect(() => {
    if (import.meta.env.DEV) return; // vite dev serves no version.json
    let cancelled = false;

    async function check() {
      try {
        // Relative to the page → <server>/SpiceOLifeApi/admin/version.json.
        // Native fetch: same-origin, no Tauri IPC needed.
        const response = await window.fetch(`version.json?_=${Date.now()}`, {
          cache: "no-store",
        });
        if (!response.ok) return;
        const data = await response.json();
        if (!cancelled && data?.buildId && data.buildId !== __BUILD_ID__) {
          setAvailable(true);
        }
      } catch {
        // server unreachable — stay quiet
      }
    }

    void check();
    const timer = window.setInterval(() => void check(), CHECK_INTERVAL_MS);
    const onFocus = () => void check();
    window.addEventListener("focus", onFocus);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
      window.removeEventListener("focus", onFocus);
    };
  }, []);

  // --- mouse activity tracking (only used to decide when the user is idle
  //     enough to first show the popup; it does NOT dismiss the popup) ---
  useEffect(() => {
    function onActivity() {
      lastActivity.current = Date.now();
    }
    window.addEventListener("mousemove", onActivity);
    window.addEventListener("mousedown", onActivity);
    window.addEventListener("wheel", onActivity);
    window.addEventListener("touchstart", onActivity);
    window.addEventListener("touchmove", onActivity);
    return () => {
      window.removeEventListener("mousemove", onActivity);
      window.removeEventListener("mousedown", onActivity);
      window.removeEventListener("wheel", onActivity);
      window.removeEventListener("touchstart", onActivity);
      window.removeEventListener("touchmove", onActivity);
    };
  }, []);

  // --- open the popup after the idle threshold of no mouse activity ---
  useEffect(() => {
    if (!available || countdown !== null) return;
    const timer = window.setInterval(() => {
      if (Date.now() - lastActivity.current >= IDLE_BEFORE_POPUP_MS) {
        setCountdown(POPUP_COUNTDOWN_SECONDS);
      }
    }, 1000);
    return () => window.clearInterval(timer);
  }, [available, countdown]);

  // --- countdown → auto refresh ---
  useEffect(() => {
    if (countdown === null) return;
    if (countdown <= 0) {
      hardRefresh();
      return;
    }
    const timer = window.setTimeout(() => setCountdown((c) => (c === null ? null : c - 1)), 1000);
    return () => window.clearTimeout(timer);
  }, [countdown]);

  if (!available) return null;

  return (
    <>
      <button
        className="update-notice tt-update-notice"
        onClick={hardRefresh}
        title="A new version has been published — tap to refresh"
      >
        <span className="update-notice-icon">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 3v12" />
            <path d="m7 10 5 5 5-5" />
            <path d="M4 21h16" />
          </svg>
        </span>
        <span className="update-notice-text">
          <span className="update-notice-title">Update available</span>
          <span className="update-notice-sub">Tap to refresh and apply</span>
        </span>
      </button>

      {countdown !== null && (
        <div className="update-popup-overlay">
          <div className="update-popup">
            <div className="update-popup-title">Update published</div>
            <div className="update-popup-message">
              A new version of Globeweigh Touch is available.
              <br />
              The screen will update automatically in
            </div>
            <div className="update-popup-countdown">{countdown}</div>
            <div className="update-popup-message">seconds</div>
            <button className="update-popup-button" onClick={hardRefresh}>
              Update now
            </button>
          </div>
        </div>
      )}
    </>
  );
}
