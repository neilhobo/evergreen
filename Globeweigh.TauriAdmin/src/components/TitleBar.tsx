import { useEffect, useState } from "react";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { isTauri } from "@tauri-apps/api/core";

// Custom window title bar. The window runs with decorations:false (the OS caption
// can't be themed dark on Windows Server 2012 — it renders an ugly light/blue Aero
// bar), so we draw our own dark bar with drag region + min/max/close. Rendered only
// inside the Tauri shell; in a plain browser (or `npm run dev`) it returns null.
export default function TitleBar() {
  // Only render when the window is actually undecorated — so an older shell that still
  // has the OS caption (or a plain browser) doesn't end up with two title bars.
  const [show, setShow] = useState(false);
  const [maximized, setMaximized] = useState(false);

  useEffect(() => {
    if (!isTauri()) return;
    const win = getCurrentWindow();
    win.isDecorated().then((d) => setShow(!d)).catch(() => setShow(false));
    win.isMaximized().then(setMaximized).catch(() => {});
    const unlisten = win.onResized(() => {
      win.isMaximized().then(setMaximized).catch(() => {});
    });
    return () => {
      unlisten.then((f) => f()).catch(() => {});
    };
  }, []);

  if (!show) return null;
  const win = getCurrentWindow();

  return (
    <div className="title-bar" data-tauri-drag-region>
      <div className="title-bar-brand" data-tauri-drag-region>
        <img className="title-bar-icon" src="images/Globe-96.png" alt="" />
        <span className="title-bar-text">Globeweigh WMS</span>
      </div>
      <div className="title-bar-controls">
        <button className="tb-btn" title="Minimize" aria-label="Minimize" onClick={() => win.minimize()}>
          <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true">
            <path d="M0 9h10" stroke="currentColor" strokeWidth="1" />
          </svg>
        </button>
        <button
          className="tb-btn"
          title={maximized ? "Restore" : "Maximize"}
          aria-label={maximized ? "Restore" : "Maximize"}
          onClick={() => win.toggleMaximize()}
        >
          {maximized ? (
            <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true">
              <rect x="0.5" y="2.5" width="6" height="6" fill="none" stroke="currentColor" strokeWidth="1" />
              <path d="M2.5 2.5V0.5h6v6h-2" fill="none" stroke="currentColor" strokeWidth="1" />
            </svg>
          ) : (
            <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true">
              <rect x="0.5" y="0.5" width="9" height="9" fill="none" stroke="currentColor" strokeWidth="1" />
            </svg>
          )}
        </button>
        <button className="tb-btn tb-close" title="Close" aria-label="Close" onClick={() => win.close()}>
          <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true">
            <path d="M0 0l10 10M10 0L0 10" stroke="currentColor" strokeWidth="1" />
          </svg>
        </button>
      </div>
    </div>
  );
}
