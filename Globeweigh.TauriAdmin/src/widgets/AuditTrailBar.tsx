import { useEffect, useState } from "react";
import styles from "./DetailPanel.module.css";
import { cx } from "./DetailPanel";
import { AuditEntry, getAuditTrail } from "../api/audit";
import { fmtDateTime } from "../format";

interface Props {
  // What the server files the trail under (api/audit.ts AUDIT_ENTITY) and which record.
  entityName: string;
  entityId: number;
}

// Something that HAPPENED to the record rather than a change to it — an email sent for the load —
// shown on the trail in its place among the changes, in a style of its own. Supplied by the
// caller, which is where the record's events live; the trail itself holds only changes.
export interface AuditEvent {
  id: string;
  when: string;
  // What happened: "Dispatch confirmation emailed".
  title: string;
  // The detail, one per line: every address the email went to.
  lines: string[];
  who: string | null;
}

// Everything changed on one record since it was created, in one of two shapes: a STRIP along the
// bottom of whatever column it is dropped into (AuditTrailBar — the order panel), or a COLUMN
// filling a card of its own down the far right of the page (AuditTrailColumn — the load panel).
// Both read the trail through the one hook below and draw the rows with the one list below that,
// so the two cannot drift apart in what they show; they differ only in where the rows go.

// The trail for one record, fetched afresh whenever the record changes.
function useAuditTrail(entityName: string, entityId: number | null) {
  const [audit, setAudit] = useState<AuditEntry[]>([]);
  const [failed, setFailed] = useState(false);
  const [loading, setLoading] = useState(entityId != null);

  // Fetched whether or not anything is open — the count is the point of the shut strip. No
  // record yet (the load panel before its orders arrive) is an empty trail, not a failed one.
  useEffect(() => {
    if (entityId == null) {
      setAudit([]);
      setFailed(false);
      setLoading(false);
      return;
    }
    let cancelled = false;
    setLoading(true);
    getAuditTrail(entityName, entityId)
      .then((rows) => {
        if (cancelled) return;
        setAudit(rows);
        setFailed(false);
        setLoading(false);
      })
      .catch(() => {
        if (cancelled) return;
        setFailed(true);
        setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [entityName, entityId]);

  // The count is of CHANGES. Every record's trail closes with its Created row, which is not one —
  // a count that could never read 0 would say nothing about whether anyone has touched it.
  const changes = audit.filter((a) => a.action !== "Created").length;

  return { audit, failed, loading, changes };
}

interface ListProps {
  audit: AuditEntry[];
  failed: boolean;
  loading: boolean;
  // Each row laid out DOWN rather than across — for a column too narrow to read a change on one
  // line (see .auditListStacked).
  stacked?: boolean;
  // Events to show among the changes, in date order.
  events?: AuditEvent[];
}

type Row = { kind: "change"; entry: AuditEntry } | { kind: "event"; event: AuditEvent };

// The changes as the server orders them (newest first, the record's creation last), with the
// events slotted in by date. The server's order is kept as it is — the events are placed, the
// changes are not re-sorted.
function merge(audit: AuditEntry[], events: AuditEvent[]): Row[] {
  const pending = [...events].sort((a, b) => b.when.localeCompare(a.when));
  const rows: Row[] = [];
  for (const entry of audit) {
    while (pending.length > 0 && pending[0].when >= entry.dateChanged) {
      rows.push({ kind: "event", event: pending.shift()! });
    }
    rows.push({ kind: "change", entry });
  }
  for (const event of pending) rows.push({ kind: "event", event });
  return rows;
}

// The rows, or the reason there are none.
function AuditTrailList({ audit, failed, loading, stacked, events = [] }: ListProps) {
  if (failed) return <p className={styles.auditEmpty}>Could not load the audit trail.</p>;
  if (loading) return <p className={styles.auditEmpty}>Loading…</p>;
  if (audit.length === 0 && events.length === 0) {
    return (
      <p className={styles.auditEmpty}>
        Nothing has been changed on this record since it was created.
      </p>
    );
  }
  return (
    <ol className={cx(styles.auditList, stacked && styles.auditListStacked)}>
      {merge(audit, events).map((row) =>
        row.kind === "event" ? (
          <EventItem key={row.event.id} event={row.event} />
        ) : (
          <ChangeItem key={row.entry.id} a={row.entry} stacked={stacked} />
        ),
      )}
    </ol>
  );
}

// One change: the field, from → to (or what was added / removed / created), who and when. A
// STAMP — a date set to the moment of the change, recording an action — is the field alone over
// who and when: its from → to line would only repeat the time on the row; the same stamp taken
// back is the field and "cleared". The who is whoever the record names as having done it, else
// the login the change came from, else nobody.
function ChangeItem({ a, stacked }: { a: AuditEntry; stacked?: boolean }) {
  const created = a.action === "Created";
  // Rows that are the field line alone over who and when: the record's creation ("Order
  // created"), a stamp, and a stamp taken back.
  const bare = created || a.stamp || a.stampCleared;
  return (
    <li className={styles.auditItem}>
      <span className={styles.auditField}>
        {a.fieldLabel}
        {created && " created"}
        {a.fieldScope && <span className={styles.auditScope}> – {a.fieldScope}</span>}
        {a.stampCleared && <span className={styles.auditScope}> – cleared</span>}
      </span>
      {bare ? (
        // Across, the middle column is kept so who and when stay on the right edge; down,
        // there is nothing to hold a place for.
        !stacked && <span className={styles.auditChange} />
      ) : (
      <span className={styles.auditChange}>
        {a.action === "Added" ? (
          <>
            <span className={cx(styles.auditActionTag, styles.auditActionAdded)}>Added</span>
            <span className={styles.auditTo}>{a.newValue}</span>
          </>
        ) : a.action === "Removed" ? (
          <>
            <span className={cx(styles.auditActionTag, styles.auditActionRemoved)}>Removed</span>
            <span className={styles.auditFrom}>{a.oldValue}</span>
          </>
        ) : (
          <>
            <span className={styles.auditFrom}>
              {a.oldValue ?? <span className={styles.muted}>(not set)</span>}
            </span>
            <span className={styles.auditArrow} aria-hidden>
              →
            </span>
            <span className={styles.auditTo}>
              {a.newValue ?? <span className={styles.muted}>(cleared)</span>}
            </span>
          </>
        )}
      </span>
      )}
      <Meta when={a.dateChanged}>
        {a.actor ??
          a.userName ??
          // A change from a screen with no login says so; a creation with nobody
          // recorded (an order from the old system) just says nothing.
          (!created && <span className={styles.muted}>Not signed in</span>)}
      </Meta>
    </li>
  );
}

// One event: what happened, the lines under it, who and when. Compact and tinted, so it reads as
// a thing that was done rather than a field that was edited.
function EventItem({ event }: { event: AuditEvent }) {
  return (
    <li className={cx(styles.auditItem, styles.auditEvent)}>
      <span className={styles.auditEventBody}>
        <span className={styles.auditEventTitle}>
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden
          >
            <rect x="3" y="5" width="18" height="14" rx="2" />
            <path d="M3 7l9 6 9-6" />
          </svg>
          {event.title}
        </span>
        {event.lines.map((line, i) => (
          <span key={i} className={styles.auditEventLine}>
            {line}
          </span>
        ))}
      </span>
      <Meta when={event.when}>{event.who}</Meta>
    </li>
  );
}

// Who did it, and when. Where the change came from (the app) is recorded on the row but not
// shown — the office asked for the name alone.
function Meta({ when, children }: { when: string; children: React.ReactNode }) {
  return (
    <span className={styles.auditMeta}>
      <span className={styles.auditWhen}>{fmtDateTime(when)}</span>
      <span className={styles.auditWho}>{children}</span>
    </span>
  );
}

// The strip. Faint and shut by default: a panel is opened to find out what a record SAYS, and
// who changed what afterwards is a different question. The count rides the shut bar, so whether
// there is anything worth opening reads at a glance without opening it.
export default function AuditTrailBar({ entityName, entityId }: Props) {
  const trail = useAuditTrail(entityName, entityId);
  const [open, setOpen] = useState(false);

  // Shut again whenever the bar moves to a different record.
  useEffect(() => setOpen(false), [entityId]);

  return (
    <section className={cx(styles.auditRow, open && styles.auditRowOpen)}>
      <button
        type="button"
        className={styles.auditToggle}
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        title={open ? "Hide the audit trail" : "Show what has been changed on this record"}
      >
        <svg
          className={cx(styles.auditChevron, open && styles.auditChevronOpen)}
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.5"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden
        >
          <path d="m9 18 6-6-6-6" />
        </svg>
        <span className={styles.auditTitle}>Audit Trail</span>
        <span className={cx(styles.auditCount, trail.changes > 0 && styles.auditCountSome)}>
          {trail.loading ? "…" : trail.changes}
        </span>
      </button>

      {open && (
        <div className={styles.auditBody}>
          <AuditTrailList audit={trail.audit} failed={trail.failed} loading={trail.loading} />
        </div>
      )}
    </section>
  );
}

interface ColumnProps {
  entityName: string;
  // Null while there is no record to read yet: the title still shows, the body is empty.
  entityId: number | null;
  // Open, or folded to a rail. Held by the caller, since it is the caller's card that changes
  // width (.auditPanel / .auditPanelCollapsed).
  open: boolean;
  onToggle: () => void;
  // Events to show among the record's changes (the load's emails). Shown, and counted, whether
  // or not there is a record to read a trail for.
  events?: AuditEvent[];
  // What the record is called in the count's tooltip: "since this ${noun} was created".
  noun?: string;
}

// The column. Its title is the left column's first title over again, the count riding it where
// the status rides LOAD DETAILS and holding the same 17px, so the rules under the two fall on one
// pixel row across the page; below it, one change per card down the column rather than across
// it. A chevron on the title folds the card away to a rail down the right edge — the count and
// the name still on it, so what was folded and whether it holds anything stay readable — and the
// width goes to the grids. The caller supplies the card this fills.
export function AuditTrailColumn({ entityName, entityId, open, onToggle, events = [], noun = "order" }: ColumnProps) {
  const trail = useAuditTrail(entityName, entityId);
  const total = trail.changes + events.length;
  const count =
    entityId == null && events.length === 0 ? "" : trail.loading ? "…" : trail.failed ? "!" : total;
  const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? "" : "s"}`;
  const countTitle =
    entityId == null && events.length === 0
      ? undefined
      : trail.failed
        ? "The audit trail could not be loaded"
        : events.length === 0
          ? `${plural(trail.changes, "change")} since this ${noun} was created`
          : `${plural(trail.changes, "change")} since this ${noun} was created, and ${plural(events.length, "email")} sent for the load`;
  const pill = (
    <span
      className={cx(styles.auditCount, styles.auditCountTitle, total > 0 && styles.auditCountSome)}
      title={countTitle}
    >
      {count}
    </span>
  );

  if (!open) {
    return (
      <div className={styles.auditRail}>
        <button
          type="button"
          className={styles.auditFoldBtn}
          onClick={onToggle}
          aria-expanded={false}
          title="Show the audit trail"
        >
          <Chevron dir="left" />
        </button>
        {pill}
        <span className={styles.auditRailLabel}>Audit Trail</span>
      </div>
    );
  }

  return (
    <>
      <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow, styles.auditPanelTitle)}>
        <span>Audit Trail</span>
        <span className={styles.auditTitleRight}>
          {pill}
          <button
            type="button"
            className={styles.auditFoldBtn}
            onClick={onToggle}
            aria-expanded
            title="Fold the audit trail away"
          >
            <Chevron dir="right" />
          </button>
        </span>
      </h3>
      <div className={styles.auditColBody}>
        {(entityId != null || events.length > 0) && (
          <AuditTrailList
            audit={trail.audit}
            failed={trail.failed}
            loading={trail.loading}
            events={events}
            stacked
          />
        )}
      </div>
    </>
  );
}

// The fold button's chevron: right to fold the card away towards the edge it sits on, left to
// bring it back.
function Chevron({ dir }: { dir: "left" | "right" }) {
  return (
    <svg
      className={styles.auditChevron}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      <path d={dir === "right" ? "m9 18 6-6-6-6" : "m15 18-6-6 6-6"} />
    </svg>
  );
}
