import { useEffect, useState } from "react";
import { check, Update } from "@tauri-apps/plugin-updater";
import { relaunch } from "@tauri-apps/plugin-process";
import { confirmDialog, messageDialog } from "./confirmDialog";

type State =
  | { kind: "none" }
  | { kind: "available"; update: Update }
  | { kind: "downloading"; progress: number }
  | { kind: "installing" };

// Shell (installed desktop app) update notifier. Checks the updater endpoint on
// launch and every 30 minutes; click → download + install + relaunch in-app.
// Renders as a card at the bottom of the sidebar, in the same slot and style as
// the server-published UI update card (UiRefreshPill) — a shell update carries a
// UI refresh with it, so MainWindow shows only this one while it is active
// (onActiveChange drives that).
export default function UpdatePill({
  sidebarOpen = true,
  onActiveChange,
}: {
  sidebarOpen?: boolean;
  onActiveChange?: (active: boolean) => void;
}) {
  const [state, setState] = useState<State>({ kind: "none" });

  useEffect(() => {
    let cancelled = false;
    async function checkForUpdate() {
      try {
        const update = await check();
        if (!cancelled && update) setState({ kind: "available", update });
      } catch {
        // endpoint unreachable / dev mode — stay quiet
      }
    }
    void checkForUpdate();
    const timer = window.setInterval(() => void checkForUpdate(), 30 * 60 * 1000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, []);

  // Tell MainWindow whether a shell update is on screen, so the UI-refresh card
  // (and its idle auto-reload popup) stays out of the way while it is.
  useEffect(() => {
    onActiveChange?.(state.kind !== "none");
  }, [state.kind, onActiveChange]);

  if (state.kind === "none") return null;

  async function onInstall() {
    if (state.kind !== "available") return;
    const update = state.update;
    if (
      !(await confirmDialog(
        `Update to version ${update.version}?\nThe app will restart when the update completes.`,
      ))
    )
      return;

    let total = 0;
    let received = 0;
    try {
      await update.downloadAndInstall((event) => {
        switch (event.event) {
          case "Started":
            total = event.data.contentLength ?? 0;
            setState({ kind: "downloading", progress: 0 });
            break;
          case "Progress":
            received += event.data.chunkLength;
            setState({
              kind: "downloading",
              progress: total > 0 ? Math.round((received / total) * 100) : 0,
            });
            break;
          case "Finished":
            setState({ kind: "installing" });
            break;
        }
      });
      await relaunch();
    } catch (err) {
      setState({ kind: "available", update });
      void messageDialog("Update failed.\n" + String(err));
    }
  }

  const title =
    state.kind === "available"
      ? "Update available"
      : state.kind === "downloading"
        ? "Downloading update"
        : "Installing update";
  const sub =
    state.kind === "available"
      ? `Click to install v${state.update.version}`
      : state.kind === "downloading"
        ? `${state.progress}%`
        : "The app will restart";

  return (
    <button
      className={`update-notice${sidebarOpen ? "" : " collapsed"}`}
      disabled={state.kind !== "available"}
      onClick={onInstall}
      title={
        state.kind === "available"
          ? "A new version of the app is available — click to download and install"
          : `${title} — ${sub}`
      }
    >
      <span className="update-notice-icon">
        {/* package/box — an install, not just a page refresh */}
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
          <path d="M21 8v8l-9 5-9-5V8l9-5z" />
          <path d="m3 8 9 5 9-5" />
          <path d="M12 13v8" />
        </svg>
      </span>
      {sidebarOpen && (
        <span className="update-notice-text">
          <span className="update-notice-title">{title}</span>
          <span className="update-notice-sub">{sub}</span>
        </span>
      )}
    </button>
  );
}
