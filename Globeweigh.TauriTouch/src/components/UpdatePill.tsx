import { useEffect, useState } from "react";
import { check, Update } from "@tauri-apps/plugin-updater";
import { relaunch } from "@tauri-apps/plugin-process";
import { confirmDialog, messageDialog } from "./confirmDialog";

type State =
  | { kind: "none" }
  | { kind: "available"; update: Update }
  | { kind: "downloading"; progress: number }
  | { kind: "installing" };

// Shell-update badge (the desktop app itself, via the Tauri updater endpoint
// api/touch-app/latest.json). Rendered as a prominent badge — the same treatment as the
// server-published UI notice (UiRefreshPill), so both kinds of update read consistently.
// This one downloads + installs a new INSTALLER + relaunches; the UI notice just reloads.
// Floating bottom-right (clear of the UI notice bottom-left and the settings gear below).
export default function UpdatePill() {
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

  if (state.kind === "none") return null;

  async function onInstall() {
    if (state.kind !== "available") return;
    const update = state.update;
    if (
      !(await confirmDialog(
        `Update to version ${update.version}?\nThe screen will restart when the update completes.`,
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
      ? `App update — v${state.update.version}`
      : state.kind === "downloading"
        ? `Downloading… ${state.progress}%`
        : "Installing…";
  const sub =
    state.kind === "available" ? "Tap to install and restart" : "Please wait…";

  return (
    <button
      className="update-notice tt-update-notice-shell"
      disabled={state.kind !== "available"}
      onClick={onInstall}
      title="Tap to download and install the new app version"
    >
      <span className="update-notice-icon">
        {/* Download-into-box (install) icon — distinct from the refresh arrow. */}
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
          <path d="M7 10l5 5 5-5" />
          <path d="M12 15V3" />
        </svg>
      </span>
      <span className="update-notice-text">
        <span className="update-notice-title">{title}</span>
        <span className="update-notice-sub">{sub}</span>
      </span>
    </button>
  );
}
