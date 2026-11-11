import { useEffect, useRef, useState } from "react";
import { messageDialog } from "../../components/confirmDialog";
import { requestSageSalesRefresh, cancelSageSalesRefresh } from "../../api/reports";
import { getSageSync } from "../../api/sage";

// "Refresh from Sage" for the reports built on the Sage sales mirror: ask the sidecar for a fresh
// read, then watch the Sales sync row until it moves (a full read is a few minutes: the audit
// trail and the whole invoice file). reload runs while the cover is still up, so the new figures
// are there when it lifts. Pair with components/SageRefreshOverlay.
export function useSageSalesRefresh(reload: () => Promise<void>) {
  // A refresh has been asked for and the sidecar has not answered yet.
  const [waiting, setWaiting] = useState(false);
  // When it was asked for (the cover's clock).
  const [waitStart, setWaitStart] = useState(0);
  const pollRef = useRef<number | null>(null);
  const reloadRef = useRef(reload);
  reloadRef.current = reload;

  function stopPolling() {
    if (pollRef.current != null) {
      window.clearInterval(pollRef.current);
      pollRef.current = null;
    }
  }

  useEffect(() => stopPolling, []);

  async function refresh() {
    if (waiting) return;
    let before: string | null;
    try {
      const s = await requestSageSalesRefresh();
      before = s.lastRunUtc;
    } catch (err) {
      void messageDialog("Could not request a refresh.\n" + (err as Error).message);
      return;
    }
    const startedAt = Date.now();
    setWaitStart(startedAt);
    setWaiting(true);
    stopPolling();
    pollRef.current = window.setInterval(async () => {
      try {
        const s = (await getSageSync()).find((x) => x.entity === "Sales");
        if (!s) return;
        if (s.lastRunUtc !== before) {
          stopPolling();
          await reloadRef.current();
          setWaiting(false);
          if (s.lastError) void messageDialog("The Sage sidecar could not refresh the sales figures.\n" + s.lastError);
        } else if (Date.now() - startedAt > 20 * 60_000) {
          stopPolling();
          setWaiting(false);
          void messageDialog(
            "No answer from the Sage sidecar after 20 minutes.\nIs the GlobeweighSageSidecar service running on the server?",
          );
        }
      } catch {
        // keep polling
      }
    }, 5000);
  }

  // Cancel from the cover: the API drops a request the sidecar has not picked up and stops a
  // read in progress within seconds. The figures on the page stay as they were.
  async function cancel() {
    try {
      await cancelSageSalesRefresh();
      stopPolling();
      setWaiting(false);
    } catch (err) {
      void messageDialog("Could not cancel the refresh.\n" + (err as Error).message);
    }
  }

  return { waiting, waitStart, refresh, cancel };
}
