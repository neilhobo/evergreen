import { ReactNode, useEffect, useRef, useState } from "react";

interface Props {
  title?: string;
  userName?: string | null;
  onBack?: () => void;
  onHome?: () => void;
  onExit?: () => void;
  right?: ReactNode;
  hideBrand?: boolean; // drop the logo/wordmark (pages that need the top-bar space)
}

// Anything that covers the screen and answers Escape itself (keypad, on-screen keyboard,
// printer picker, modal, confirm/message dialog, the missed-scan alarm, the update popup and
// the post-weigh success overlay). While one of these is up, Escape belongs to it, not Back.
const OVERLAY_SELECTOR = [
  ".tt-pad-overlay",
  ".modal-overlay",
  ".tt-confirm-overlay",
  ".tt-alarm-overlay",
  ".update-popup-overlay",
  ".tt-overlay",
].join(", ");

// Escape is the Back key. Lives here so every screen with a Back button gets it for free,
// and screens without one (the kiosk home steps) are unaffected.
function useEscapeGoesBack(onBack?: () => void) {
  const backRef = useRef(onBack);
  backRef.current = onBack;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape" || !backRef.current) return;
      if (document.querySelector(OVERLAY_SELECTOR)) return;
      backRef.current();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, []);
}

function useClock() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const t = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(t);
  }, []);
  return now;
}

export default function TopBar({ title, userName, onBack, onHome, onExit, right, hideBrand }: Props) {
  const now = useClock();
  useEscapeGoesBack(onBack);
  const clock = now.toLocaleString(undefined, {
    weekday: "short",
    day: "2-digit",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });

  return (
    <div className="tt-topbar">
      {onBack && (
        <button className="tt-btn tt-ghost" style={{ minHeight: 44, padding: "0 18px" }} onClick={onBack}>
          ← Back
        </button>
      )}
      {!hideBrand && (
        <div className="tt-brand">
          <img className="tt-logo" src="images/Globe-96.png" alt="" />
          <span className="tt-wordmark">
            <span className="wg">Globe</span><span className="ww">weigh</span>
          </span>
        </div>
      )}
      {title && <div className="tt-title">{hideBrand ? title : `· ${title}`}</div>}
      <div className="tt-spacer" />
      {right}
      {userName && <div className="tt-user">{userName}</div>}
      <div className="tt-clock">{clock}</div>
      {onHome && (
        <button className="tt-btn tt-ghost" style={{ minHeight: 44, padding: "0 18px" }} onClick={onHome}>
          Home
        </button>
      )}
      {onExit && (
        <button
          className="tt-btn tt-ghost tt-exit"
          style={{ minHeight: 44, padding: "0 18px" }}
          onClick={onExit}
          title="Exit Globeweigh Touch"
        >
          ✕ Exit
        </button>
      )}
    </div>
  );
}
