import { useEffect, useState } from "react";
import styles from "./ServiceStatusOverlay.module.css";
import own from "./SageRefreshOverlay.module.css";

interface Props {
  /** What is being read, for the title: "customers", "sales figures". */
  what: string;
  /** How long a refresh normally takes, in words: "about a minute". */
  typically: string;
  /** When the refresh was asked for (Date.now()), for the elapsed clock. */
  startedAt: number;
  /** Stop the refresh. The cover shows "Cancelling…" until it resolves; the page closes it. */
  onCancel: () => Promise<void>;
}

/** Past this, say it is running long rather than just counting. */
const SLOW_MS = 3 * 60_000;

/**
 * Covers the app while the Sage sidecar reads Sage for a "Refresh from Sage" — a read that
 * takes minutes — so nothing on the page can be pressed half-way through, and says plainly
 * that the wait is expected. Same look as the "Globeweigh is updating" cover.
 */
export default function SageRefreshOverlay({ what, typically, startedAt, onCancel }: Props) {
  const [elapsed, setElapsed] = useState(0);
  const [cancelling, setCancelling] = useState(false);

  async function cancel() {
    setCancelling(true);
    try {
      await onCancel();
    } finally {
      setCancelling(false);
    }
  }

  useEffect(() => {
    const tick = () => setElapsed(Date.now() - startedAt);
    tick();
    const id = window.setInterval(tick, 1000);
    return () => window.clearInterval(id);
  }, [startedAt]);

  return (
    <div className={styles.overlay} role="alertdialog" aria-modal="true" aria-live="polite">
      <div className={styles.card}>
        <img src="images/Globe-96.png" alt="" className={styles.logo} />
        <div className={styles.spinner} aria-hidden="true" />
        <h2 className={styles.title}>Refreshing {what} from Sage</h2>
        <p className={styles.body}>
          The Sage link on the server is reading Sage now. This can take some time &mdash; {typically}
          &nbsp;&mdash; and the page will update itself when it&rsquo;s done.
        </p>
        <p className={`${styles.body} ${own.hint}`}>
          You can cancel at any time &mdash; what&rsquo;s on the page now stays as it is.
        </p>
        {elapsed >= SLOW_MS && (
          <p className={styles.note}>
            Still going. Sage can be slow when other people are using it; this will give up on its own if the
            server stops answering.
          </p>
        )}

        <div className={styles.footer}>
          <span className={styles.status}>Elapsed · {formatElapsed(elapsed)}</span>
          <button
            type="button"
            className={`${styles.retry} ${own.cancel}`}
            onClick={() => void cancel()}
            disabled={cancelling}
            title="Stop reading Sage. The figures already on the page stay as they are."
          >
            {cancelling ? "Cancelling…" : "Cancel refresh"}
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
