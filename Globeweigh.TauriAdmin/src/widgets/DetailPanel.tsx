import { ReactNode, useEffect } from "react";
import styles from "./DetailPanel.module.css";

// The small shared pieces every detail fly-in is built from. The panel itself is not a component:
// each one lays its own left column out differently, and wrapping that in props would cost more
// than the markup it saved. What IS shared is the look (DetailPanel.module.css), the label/value
// row below, and the two blocks in widgets/OrderGrids.tsx and widgets/AuditTrailBar.tsx.

// Joins the class names that are set (the one thing clsx would otherwise be pulled in for).
export function cx(...names: Array<string | false | null | undefined>): string {
  return names.filter(Boolean).join(" ");
}

// One label/value row in a panel's left column. An empty value reads as an em dash rather than as
// a blank line, so a section with nothing in it still says which fields it was looking for.
export function Field({ label, value }: { label: string; value: ReactNode }) {
  const empty =
    value === null ||
    value === undefined ||
    value === "" ||
    // A <span> wrapping an empty string is still nothing to show.
    (typeof value === "object" &&
      "props" in (value as never) &&
      !((value as { props: { children?: unknown } }).props.children));
  return (
    <div className={styles.field}>
      <span className={styles.fieldLabel}>{label}</span>
      <span className={styles.fieldValue}>
        {empty ? <span className={styles.muted}>—</span> : value}
      </span>
    </div>
  );
}

// The Back button every panel's header opens with.
export function BackButton({ onClick }: { onClick: () => void }) {
  return (
    <button type="button" className={styles.backBtn} onClick={onClick}>
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden
      >
        <path d="M19 12H5M12 19l-7-7 7-7" />
      </svg>
      <span>Back</span>
    </button>
  );
}

// Escape closes an open panel, like every other overlay in the app. Skipped while `enabled` is
// false, so a panel with a lightbox over it lets the lightbox have the key first.
export function useEscape(onEscape: () => void, enabled = true) {
  useEffect(() => {
    if (!enabled) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onEscape();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onEscape, enabled]);
}
