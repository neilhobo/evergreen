import { useEffect, useState } from "react";
import styles from "../../widgets/DetailPanel.module.css";
import t from "./DeliveryDetailPanel.module.css";
import { BackButton, Field, cx, useEscape } from "../../widgets/DetailPanel";
import { AuditTrailColumn } from "../../widgets/AuditTrailBar";
import { AUDIT_ENTITY } from "../../api/audit";
import { WeighbridgeComplete, WeighbridgeRecordDetail, getWeighbridgeRecord } from "../../api/weighbridge";
import { fmtDateTime, fmtQty, fmtQty2 } from "../../format";

// Deliveries/Incoming → Completed → View: one incoming delivery, read-only.
//
// An incoming delivery is not a dispatch load: there are no orders, no lines and no picking, just
// a lorry that crossed the bridge twice with a docket. So the panel is laid out around what it does
// have — the delivery's facts down the left, the weighbridge ticket in the middle (the two
// weighings, the net between them drawn to scale, the times, the sample), and the record's audit
// trail as a full-height card on the right, the same card the Load panel uses.
//
// The grid row is passed in for the figures the view computes (net, cubic m3); the record itself is
// read for what the grid leaves out (comments, trailer, created, deleted).
export default function DeliveryDetailPanel({ row, onClose }: { row: WeighbridgeComplete; onClose: () => void }) {
  const [rec, setRec] = useState<WeighbridgeRecordDetail | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setRec(null);
    setFailed(false);
    getWeighbridgeRecord(row.id)
      .then((r) => !cancelled && setRec(r))
      .catch(() => !cancelled && setFailed(true));
    return () => {
      cancelled = true;
    };
  }, [row.id]);

  const [auditOpen, setAuditOpen] = useState(readAuditOpen);
  const toggleAudit = () => {
    const next = !auditOpen;
    setAuditOpen(next);
    writeAuditOpen(next);
  };

  useEscape(onClose);

  const weightIn = rec?.weightIn ?? row.weightIn;
  const weightOut = rec?.weightOut ?? row.weightOut;
  const dateIn = rec?.dateWeightIn ?? row.dateWeightIn;
  const dateOut = rec?.dateWeightOut ?? row.dateWeightOut;
  const net = row.netWeight ?? (weightIn > 0 && weightOut > 0 ? Math.abs(weightIn - weightOut) : null);
  const deleted = !!rec?.dateDeleted;

  // An incoming lorry arrives full and leaves empty, so In is the gross and Out the tare. Said only
  // when the figures agree — a lorry weighed the other way round keeps plain In / Out.
  const inIsGross = weightIn > 0 && weightOut > 0 && weightIn >= weightOut;
  const gross = Math.max(weightIn, weightOut);
  const tare = Math.min(weightIn, weightOut);
  const tarePct = gross > 0 && tare > 0 ? (tare / gross) * 100 : 0;

  return (
    <div className={styles.panel} role="dialog" aria-label="Delivery details">
      <div className={styles.header}>
        <BackButton onClick={onClose} />
        <span className={styles.headerTitle}>
          Delivery{row.haulierName ? ` — ${row.haulierName}` : ""}
        </span>
        <span className={styles.headerRef}>
          {row.docketNumber ? `Docket ${row.docketNumber} · ` : ""}Record #{row.id}
        </span>
      </div>

      <div className={styles.content}>
        <aside className={styles.leftPanel}>
          <section className={styles.section}>
            <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
              <span>Delivery Details</span>
              <span className={cx(styles.statusTag, deleted ? styles.statusDeleted : styles.statusDispatched)}>
                {deleted ? "Deleted" : "Completed"}
              </span>
            </h3>
            <Field label="Record" value={`#${row.id}`} />
            <Field label="Docket No" value={row.docketNumber} />
            <Field label="Haulier" value={row.haulierName} />
            <Field label="Vehicle Reg" value={row.vehicleReg} />
            <Field label="Trailer No" value={rec?.trailerNo ?? row.trailerNo} />
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Load</h3>
            <Field label="Supplier" value={row.supplier} />
            <Field label="Product" value={row.product} />
            <Field label="Bog Name" value={row.bogName} />
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Comments</h3>
            <Field
              label="Delivery"
              value={<span className={styles.prose}>{failed ? "" : rec?.comments}</span>}
            />
          </section>

          {failed && <span className={styles.muted}>The full record could not be loaded.</span>}
        </aside>

        <div className={cx(styles.rightPanel, t.ticketPanel)}>
          <div className={t.ticket}>
            <div className={t.ticketHead}>
              <span className={t.ticketTitle}>Weighbridge Ticket</span>
              {row.docketNumber && <span className={t.ticketDocket}>Docket {row.docketNumber}</span>}
            </div>

            {/* The two weighings and the net between them. */}
            <div className={t.weights}>
              <div className={t.weight}>
                <span className={t.weightLabel}>
                  Weight In{inIsGross && <span className={t.weightRole}>Gross</span>}
                </span>
                <span className={t.weightValue}>
                  {weightIn > 0 ? fmtQty(weightIn) : "—"}
                  <small>kg</small>
                </span>
                <span className={t.weightWhen}>{fmtDateTime(dateIn) || "Not weighed in"}</span>
              </div>
              <div className={t.weight}>
                <span className={t.weightLabel}>
                  Weight Out{inIsGross && <span className={t.weightRole}>Tare</span>}
                </span>
                <span className={t.weightValue}>
                  {weightOut > 0 ? fmtQty(weightOut) : "—"}
                  <small>kg</small>
                </span>
                <span className={t.weightWhen}>{fmtDateTime(dateOut) || "Not weighed out"}</span>
              </div>
              <div className={cx(t.weight, t.weightNet)}>
                <span className={t.weightLabel}>Net Delivered</span>
                <span className={t.weightValue}>
                  {net != null ? fmtQty(net) : "—"}
                  <small>kg</small>
                </span>
                <span className={t.weightWhen}>
                  {net != null && net > 0 ? `${(net / 1000).toLocaleString("en-GB", { maximumFractionDigits: 2 })} tonnes` : ""}
                </span>
              </div>
            </div>

            {/* Gross drawn to scale: the lorry's own weight, and what it brought. */}
            {gross > 0 && tare > 0 && (
              <div className={t.scale}>
                <div className={t.bar} role="img" aria-label={`Tare ${fmtQty(tare)} kg, net ${fmtQty(gross - tare)} kg`}>
                  <span className={t.barTare} style={{ width: `${tarePct}%` }} />
                  <span className={t.barNet} style={{ width: `${100 - tarePct}%` }} />
                </div>
                <div className={t.barLegend}>
                  <span>
                    <i className={t.keyTare} />
                    {inIsGross ? "Vehicle (tare)" : "Lighter weighing"} {fmtQty(tare)} kg
                  </span>
                  <span>
                    <i className={t.keyNet} />
                    Net {fmtQty(gross - tare)} kg · {(100 - tarePct).toFixed(0)}% of gross
                  </span>
                </div>
              </div>
            )}
          </div>

          <div className={t.lower}>
            <section className={t.block}>
              <h3 className={t.blockTitle}>On Site</h3>
              <ol className={t.timeline}>
                <li className={rec?.dateCreated ? t.stepDone : undefined}>
                  <span className={t.stepName}>Booked in</span>
                  <span className={t.stepWhen}>{fmtDateTime(rec?.dateCreated) || "—"}</span>
                </li>
                <li className={dateIn ? t.stepDone : undefined}>
                  <span className={t.stepName}>Weighed in</span>
                  <span className={t.stepWhen}>{fmtDateTime(dateIn) || "—"}</span>
                </li>
                <li className={dateOut ? t.stepDone : undefined}>
                  <span className={t.stepName}>Weighed out</span>
                  <span className={t.stepWhen}>{fmtDateTime(dateOut) || "—"}</span>
                </li>
              </ol>
              <div className={t.turn}>
                <span>Time on site</span>
                <strong>{turnaround(dateIn, dateOut) ?? "—"}</strong>
              </div>
            </section>

            <section className={t.block}>
              <h3 className={t.blockTitle}>Sample &amp; Volume</h3>
              <div className={t.stats}>
                <div className={t.stat}>
                  <span className={t.statLabel}>Sample Weight</span>
                  <span className={t.statValue}>
                    {row.sampleBoxWeight != null ? `${fmtQty2(row.sampleBoxWeight)} kg` : "—"}
                  </span>
                </div>
                <div className={t.stat}>
                  <span className={t.statLabel}>Volume</span>
                  <span className={t.statValue}>{row.cubicM3 != null ? `${fmtQty(row.cubicM3)} m³` : "—"}</span>
                </div>
              </div>
            </section>
          </div>
        </div>

        <aside className={cx(styles.auditPanel, !auditOpen && styles.auditPanelCollapsed)}>
          <AuditTrailColumn
            entityName={AUDIT_ENTITY.weighbridgeRecord}
            entityId={row.id}
            open={auditOpen}
            onToggle={toggleAudit}
            noun="delivery"
          />
        </aside>
      </div>
    </div>
  );
}

function turnaround(from: string | null | undefined, to: string | null | undefined): string | null {
  if (!from || !to) return null;
  const ms = new Date(to).getTime() - new Date(from).getTime();
  if (!Number.isFinite(ms) || ms < 0) return null;
  const mins = Math.round(ms / 60000);
  return mins < 60 ? `${mins}m` : `${Math.floor(mins / 60)}h ${mins % 60}m`;
}

// Whether the audit card is open — shared with the Load panel's, so the choice follows the user.
const AUDIT_OPEN_KEY = "loadDetail.auditOpen";

function readAuditOpen(): boolean {
  try {
    return localStorage.getItem(AUDIT_OPEN_KEY) !== "0";
  } catch {
    return true;
  }
}

function writeAuditOpen(open: boolean) {
  try {
    localStorage.setItem(AUDIT_OPEN_KEY, open ? "1" : "0");
  } catch {
    // The choice just will not survive the session.
  }
}
