import { useEffect, useState } from "react";
import { installServiceStatusMonitor, retryNow, useServiceStatus } from "../api/serviceStatus";
import styles from "./ServiceStatusOverlay.module.css";

/** After this long an outage stops looking like a deploy and starts looking like the
 *  network or a stopped server — say so rather than repeating ourselves. */
const LONG_OUTAGE_MS = 45_000;

/**
 * Covers the app while the API is unreachable — a deploy, most often — instead of leaving
 * people with empty grids and error toasts. Mounted once, from App, over the login page too.
 */
export default function ServiceStatusOverlay({ signedIn }: { signedIn: boolean }) {
  const { down, probing, recovering, downSince } = useServiceStatus();
  const [elapsed, setElapsed] = useState(0);

  useEffect(() => installServiceStatusMonitor(), []);

  useEffect(() => {
    if (!down || !downSince) {
      setElapsed(0);
      return;
    }
    const tick = () => setElapsed(Date.now() - downSince);
    tick();
    const id = window.setInterval(tick, 1000);
    return () => window.clearInterval(id);
  }, [down, downSince]);

  if (!down) return null;

  const longOutage = elapsed >= LONG_OUTAGE_MS;

  return (
    <div className={styles.overlay} role="alertdialog" aria-modal="true" aria-live="assertive">
      <div className={styles.card}>
        <img src="images/Globe-96.png" alt="" className={styles.logo} />

        {recovering ? (
          <>
            <div className={`${styles.spinner} ${styles.spinnerDone}`} aria-hidden="true" />
            <h2 className={styles.title}>Back online</h2>
            <p className={styles.body}>Reloading the page&hellip;</p>
          </>
        ) : (
          <>
            <div className={styles.spinner} aria-hidden="true" />
            <h2 className={styles.title}>Globeweigh is updating</h2>
            <p className={styles.body}>
              A new version is going live, so the server is briefly unavailable.
              {signedIn ? <> You&rsquo;re still signed in &mdash; this</> : " This"} page will
              refresh itself as soon as it&rsquo;s back.
            </p>
            {longOutage && (
              <p className={styles.note}>
                This is taking longer than usual. If it keeps happening, check the network
                connection and that the Evergreen server is running.
              </p>
            )}
          </>
        )}

        <div className={styles.footer}>
          <span className={styles.status}>
            {recovering
              ? "Service restored"
              : probing
                ? "Checking…"
                : `Retrying automatically · ${formatElapsed(elapsed)}`}
          </span>
          <button
            type="button"
            className={styles.retry}
            onClick={() => void retryNow()}
            disabled={probing || recovering}
          >
            Try now
          </button>
        </div>
      </div>
    </div>
  );
}

function formatElapsed(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  if (total < 60) return `${total}s`;
  const mins = Math.floor(total / 60);
  const secs = total % 60;
  return `${mins}m ${String(secs).padStart(2, "0")}s`;
}
