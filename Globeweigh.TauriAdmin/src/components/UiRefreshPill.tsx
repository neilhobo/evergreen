import { useEffect, useRef, useState } from "react";
import { markResumeAfterRefresh } from "./resumeAfterRefresh";

// Every refresh goes through here: the marker has to be set on ALL of them (the card,
// the popup button and the countdown), or an update applied by one path loses the page
// while another keeps it.
function refreshNow() {
  markResumeAfterRefresh();
  window.location.reload();
}

const CHECK_INTERVAL_MS = 30 * 1000;
const IDLE_BEFORE_POPUP_MS = 60 * 1000;
const POPUP_COUNTDOWN_SECONDS = 20;

// Server-published UI update notifier. The UI is served from the server
// (deploy-admin-ui.ps1 just copies files), so a running app polls the
// version.json deployed beside it and compares against its own baked-in
// __BUILD_ID__. When a newer build is on the server:
//   - an update card appears at the bottom of the sidebar, above the version
//     number (whole card clicks to refresh), and
//   - once the mouse has been idle for 60s, a popup announces the update and
//     auto-refreshes after a 20s countdown. Once the popup is showing it is
//     committed — mouse activity no longer dismisses it; the user must wait out
//     the countdown or click Refresh now.
// The session AND the open nav page are restored from sessionStorage after the
// reload (see resumeAfterRefresh), so the user lands back where they were, signed in.
export default function UiRefreshPill({ sidebarOpen = true }: { sidebarOpen?: boolean }) {
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
    return () => {
      window.removeEventListener("mousemove", onActivity);
      window.removeEventListener("mousedown", onActivity);
      window.removeEventListener("wheel", onActivity);
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
      refreshNow();
      return;
    }
    const timer = window.setTimeout(() => setCountdown((c) => (c === null ? null : c - 1)), 1000);
    return () => window.clearTimeout(timer);
  }, [countdown]);

  if (!available) return null;

  return (
    <>
      <button
        className={`update-notice${sidebarOpen ? "" : " collapsed"}`}
        onClick={refreshNow}
        title="A new version has been published — click to refresh"
      >
        <span className="update-notice-icon">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 3v12" />
            <path d="m7 10 5 5 5-5" />
            <path d="M4 21h16" />
          </svg>
        </span>
        {sidebarOpen && (
          <span className="update-notice-text">
            <span className="update-notice-title">Update available</span>
            <span className="update-notice-sub">Click to refresh and apply</span>
          </span>
        )}
      </button>

      {countdown !== null && (
        <div className="update-popup-overlay">
          <div className="update-popup">
            <div className="update-popup-title">Update published</div>
            <div className="update-popup-message">
              A new version of Globeweigh WMS is available.
              <br />
              The app will update automatically in
            </div>
            <div className="update-popup-countdown">{countdown}</div>
            <div className="update-popup-message">seconds</div>
            <button className="update-popup-button" onClick={refreshNow}>
              Update now
            </button>
          </div>
        </div>
      )}
    </>
  );
}
