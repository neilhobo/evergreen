import { useEffect, useRef, useState } from "react";
import TopBar from "../components/touch/TopBar";
import ProductionLabelPreview from "../components/touch/ProductionLabelPreview";
import { confirmDialog, touchConfirmDialog } from "../components/confirmDialog";
import {
  getProductionPallets,
  logLabelReprint,
  markPalletMissed,
  markPalletPassed,
  ProductionPallet,
} from "../api/touch";
import { renderProductionLabelCanvas } from "../api/productionLabelCanvas";
import { getSavedPrinter, PRINTING_ENABLED, printImage } from "../api/printer";

interface Props {
  // Both recorded against a label reprint (see logLabelReprint). userName is null on a
  // Factory device with Login disabled — nobody is signed in.
  deviceName: string | null;
  userName: string | null;
  onHome: () => void;
}

const REFRESH_MS = 10_000; // matches the WPF 10-second refresh timer

const ymd = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

function fmtTime(s: string | null): string {
  if (!s) return "";
  const d = new Date(s);
  if (isNaN(d.getTime())) return "";
  const p = (n: number) => String(n).padStart(2, "0");
  return `${p(d.getHours())}:${p(d.getMinutes())}`;
}

// Production Pallets. Ports the WPF ProductionPalletListView: a live grid of pallets over a
// rolling window (yesterday → tomorrow), auto-refreshing every 10s, duplicate rows highlighted,
// with a pulsing green "live" indicator and a row count.
export default function ProductionScreen({ deviceName, userName, onHome }: Props) {
  const [rows, setRows] = useState<ProductionPallet[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [loading, setLoading] = useState(true); // initial load only — refreshes are silent
  const [error, setError] = useState<string | null>(null);
  const [lastRefresh, setLastRefresh] = useState<Date | null>(null);
  const [agoSeconds, setAgoSeconds] = useState(0);
  const lastRef = useRef<Date | null>(null);

  async function refresh() {
    // Today only, from 00:00 (the API treats `to` inclusively, +1 day — so today..today
    // covers midnight to midnight).
    const today = new Date();
    try {
      const list = await getProductionPallets(ymd(today), ymd(today));
      setRows(list);
      const now = new Date();
      setLastRefresh(now);
      lastRef.current = now;
      setAgoSeconds(0);
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  // Poll every 10s.
  useEffect(() => {
    void refresh();
    const t = window.setInterval(() => void refresh(), REFRESH_MS);
    return () => window.clearInterval(t);
  }, []);

  // "updated Ns ago" ticker.
  useEffect(() => {
    const t = window.setInterval(() => {
      if (lastRef.current) setAgoSeconds(Math.round((Date.now() - lastRef.current.getTime()) / 1000));
    }, 1000);
    return () => window.clearInterval(t);
  }, []);

  // Missed scan — mirrors the API's alarm rule in ProductionController.UnackedMissedTodayAsync;
  // keep the two in step, thresholds included. Manual pallets are outside the rule on both
  // sides (they never pass the conveyor scanner), so they are neither missed nor evidence
  // that the scanner has moved on. Nor is a row an operator cleared by hand: only a real
  // SCANNER read (scanSource null or "Scanner") says anything about where the scanner got to.
  const isScannerRead = (p: ProductionPallet) =>
    p.isScanned && !p.isManual && (p.scanSource == null || p.scanSource === "Scanner");
  const maxScannerReadId = rows.reduce((m, r) => (isScannerRead(r) && r.id > m ? r.id : m), 0);

  // OVERTAKEN: the scanner read a later pallet, so it went past this one.
  const isOvertaken = (p: ProductionPallet) =>
    !p.isScanned && !p.isManual && p.id < maxScannerReadId;

  // BLIND SCANNER: the run of unread pallets after the last scanner read. Nothing has been
  // read after them, so the overtaken rule can never see them — a long enough run whose
  // oldest pallet has waited long enough means the scanner has stopped decoding altogether.
  const BLIND_MIN_RUN = 5;
  const BLIND_STALE_MIN = 20;
  const tail = rows
    .filter((p) => !p.isScanned && !p.isManual && p.id > maxScannerReadId)
    .sort((a, b) => a.id - b.id);
  const oldestTailAgeMin =
    tail.length > 0 && tail[0].dateCreated != null
      ? (Date.now() - new Date(tail[0].dateCreated).getTime()) / 60000
      : 0;
  const blind = tail.length >= BLIND_MIN_RUN && oldestTailAgeMin > BLIND_STALE_MIN;
  const blindIds = new Set(blind ? tail.map((p) => p.id) : []);

  const isMissed = (p: ProductionPallet) => isOvertaken(p) || blindIds.has(p.id);

  // Reprint (missed scan / no label / damaged label): the selected row, if it still exists —
  // the 10s refresh can drop it out of the rolling window.
  const selectedRow = rows.find((r) => r.id === selectedId) ?? null;
  const [reprint, setReprint] = useState<ProductionPallet | null>(null);

  // Shared by Mark As Scanned (a real operator action) and the testing-only Mark as
  // Missed Scan button below.
  const [marking, setMarking] = useState(false);
  async function markMissed(pallet: ProductionPallet) {
    const code = pallet.uniqueCode ?? `#${pallet.id}`;
    if (
      !(await confirmDialog(
        `Mark pallet ${code} as a missed scan?\n\nThis simulates a real missed scan: the row turns red and the ALARM SIREN WILL SOUND on the alarm-relay PC until silenced.`,
      ))
    )
      return;
    setMarking(true);
    try {
      await markPalletMissed(pallet.id);
      await refresh(); // row turns red immediately; the alarm poll picks it up within ~3s
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setMarking(false);
    }
  }
  // "Mark As Scanned" — clear a pallet the scanner never read, without printing a label
  // (a reprint clears it too, see ReprintLabelDialog). Confirmed first: it silences this
  // pallet's alarm, and the pallet goes on with no label having been reprinted.
  async function markScanned(pallet: ProductionPallet) {
    const code = pallet.uniqueCode ?? `#${pallet.id}`;
    if (
      !(await touchConfirmDialog({
        icon: "✔",
        title: "Mark as scanned?",
        detail: `Pallet ${code}`,
        message:
          "No label will be printed. The row clears and this pallet stops sounding the alarm.",
        confirmText: "✔ Mark as Scanned",
      }))
    )
      return;
    setMarking(true);
    try {
      await markPalletPassed(pallet.id);
      await refresh();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setMarking(false);
    }
  }

  const live = (
    <div style={{ display: "flex", alignItems: "center", gap: 18 }}>
      <div className="tt-note" style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <span className="tt-swatch tt-swatch-red" /> Missed scan
      </div>
      <div className="tt-note" style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <span className="tt-swatch tt-swatch-red-dim" /> Missed · alarm silenced
      </div>
      <div className="tt-note" style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <span className="tt-swatch" /> Duplicate
      </div>
      <div className="tt-note">Count = {rows.length}</div>
      <div className="tt-live">
        <span className="tt-live-dot" />
        <span>{lastRefresh ? `Live · updated ${agoSeconds}s ago` : "Connecting…"}</span>
      </div>
    </div>
  );

  return (
    <div className="tt-app">
      <TopBar title="Production Pallets" onBack={onHome} right={live} hideBrand />
      <div className="tt-body">
        <div style={{ display: "flex", flexDirection: "column", height: "100%", minHeight: 0 }}>
          {error && <div className="tt-error" style={{ marginBottom: 10 }}>{error}</div>}
          {loading ? (
            <div style={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "center" }}>
              <div className="tt-spinner-lg" />
            </div>
          ) : (
          <div className="tt-grid-wrap">
            <table className="tt-grid tt-bestfit">
              <thead>
                <tr>
                  <th>Unique Code</th>
                  <th>Codico Code</th>
                  <th>Evergreen Code</th>
                  <th className="tt-col-grow">Product</th>
                  <th>Size</th>
                  <th>UPP</th>
                  <th>Weight</th>
                  <th>Created</th>
                  <th className="tt-center-col">Scanned</th>
                  <th>Scanned At</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((p) => (
                  <tr
                    key={p.id}
                    className={`${p.isScanned ? "tt-scanned" : ""}${isMissed(p) ? ` tt-missed${p.alarmAcked ? " tt-alarm-acked" : ""}` : ""}${p.isDuplicate ? " tt-dup" : ""}${selectedId === p.id ? " tt-selected" : ""}`}
                    onClick={() => setSelectedId(p.id)}
                  >
                    <td>{p.uniqueCode}</td>
                    <td>{p.codicoCode}</td>
                    <td>{p.gtin}</td>
                    <td className="tt-col-grow">{p.description}</td>
                    <td>{p.size}</td>
                    <td>{p.upp}</td>
                    <td>{p.weight}</td>
                    <td>{fmtTime(p.dateCreated)}</td>
                    <td className="tt-center-col">
                      {p.isScanned && p.scanSource === "Reprint" ? (
                        // Cleared by reprinting its label rather than read by the scanner —
                        // still a tick to the operator, but the distinction is kept visible.
                        <span title="Label reprinted" style={{ fontSize: 22 }}>🖨</span>
                      ) : p.isScanned ? (
                        <span className="tt-tick">✓</span>
                      ) : isMissed(p) && p.alarmAcked ? (
                        // Missed scan whose alarm has been silenced.
                        <span title="Alarm silenced" style={{ fontSize: 24 }}>🔕</span>
                      ) : (
                        ""
                      )}
                    </td>
                    <td>{fmtTime(p.dateScanned)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {rows.length === 0 && !error && (
              <div className="tt-note" style={{ padding: 18 }}>No production pallets in this period.</div>
            )}
          </div>
          )}
          {/* Row actions — only once a row is tapped (same idiom as Manual Stock's pallet list). */}
          {selectedRow && !loading && (
            <div style={{ display: "flex", alignItems: "center", gap: 12, paddingTop: 10 }}>
              <button
                className="tt-btn tt-ghost"
                style={{ minHeight: 40, fontSize: 13, padding: "0 14px" }}
                onClick={() => setReprint(selectedRow)}
              >
                🖨 Reprint Label
              </button>
              {/* The other way to clear a missed pallet: mark it scanned with no label
                  printed. Only offered for a row that isn't scanned yet. */}
              {!selectedRow.isScanned && (
                <button
                  className="tt-btn tt-ghost"
                  style={{
                    minHeight: 40,
                    fontSize: 13,
                    padding: "0 14px",
                    color: "var(--tt-green)",
                    borderColor: "var(--tt-green)",
                  }}
                  disabled={marking}
                  onClick={() => void markScanned(selectedRow)}
                >
                  {marking ? "Marking…" : "✔ Mark As Scanned"}
                </button>
              )}
              <span className="tt-note">Pallet {selectedRow.uniqueCode ?? selectedRow.id}</span>
              {/* TEMPORARY — testing only: fakes a missed scan so the alarm fires for real
                  (needs a later pallet already scanned, same as the genuine rule). Clearing one
                  again is the Mark As Scanned button above, which is a real operator feature.
                  Remove together with markPalletMissed and the mark-missed API endpoint. */}
              {selectedRow.isScanned && (
                <button
                  className="tt-btn tt-ghost tt-exit"
                  style={{ minHeight: 40, fontSize: 13, padding: "0 14px", marginLeft: "auto" }}
                  disabled={marking}
                  onClick={() => void markMissed(selectedRow)}
                >
                  {marking ? "Marking…" : "⚠ Mark as Missed Scan"}
                </button>
              )}
            </div>
          )}
        </div>
      </div>
      {reprint && (
        <ReprintLabelDialog
          pallet={reprint}
          deviceName={deviceName}
          userName={userName}
          // A reprint marks an unscanned pallet as scanned on the API, so pull the grid
          // again rather than waiting up to 10s for the poll to show the row cleared.
          onClose={() => {
            setReprint(null);
            void refresh();
          }}
        />
      )}
    </div>
  );
}

// Reprint popup: the production label template (its own fork — see ProductionLabelPreview /
// renderProductionLabelCanvas; edits there don't touch the Manual Stock label), filled from
// the CodicoPallet row. The DataMatrix carries the pallet's original payload, so the
// reprinted label scans through the conveyor exactly like the one it replaces.
function ReprintLabelDialog({
  pallet,
  deviceName,
  userName,
  onClose,
}: {
  pallet: ProductionPallet;
  deviceName: string | null;
  userName: string | null;
  onClose: () => void;
}) {
  const [status, setStatus] = useState<{ phase: "idle" | "printing" | "done" | "error"; text: string }>(
    { phase: "idle", text: "" },
  );

  const data = {
    product: pallet.description ?? "",
    orderNumber: null,
    // Weight is not printed any more, but it still forms part of the DataMatrix payload.
    netWeightKg: pallet.weight,
    // Size prefixes the description ("60L Multipurpose Compost") rather than printing as a field.
    size: pallet.size,
    upp: pallet.upp,
    gtin: pallet.gtin ?? "",
    palletCode: pallet.uniqueCode ?? "",
  };

  async function print() {
    const printerName = getSavedPrinter();
    if (!printerName) {
      setStatus({ phase: "error", text: "No printer is set up on this PC.\nSelect one via the Printer badge on the Home screen first." });
      return;
    }
    setStatus({
      phase: "printing",
      text: PRINTING_ENABLED
        ? `Sending label ${data.palletCode} to ${printerName}…`
        : `Dev build — skipping the printer…`,
    });
    try {
      const label = await renderProductionLabelCanvas(data);
      await printImage(printerName, label);
      // The print never touches the API, so this is the only trace that the label went out a
      // second time — and it is what marks an unscanned pallet as scanned, which is how the
      // factory tracks that a missed pallet has had a label printed.
      // Deliberately AFTER printImage — a label that failed to print is not a reprint.
      logLabelReprint({
        palletId: pallet.id,
        uniqueCode: pallet.uniqueCode,
        source: "Production",
        deviceName,
        printerName,
        userName,
      });
      setStatus({
        phase: "done",
        text: PRINTING_ENABLED
          ? `Label ${data.palletCode} sent to ${printerName}`
          : `Dev build — nothing was printed.\nPallet ${data.palletCode} marked as scanned.`,
      });
    } catch (err) {
      setStatus({ phase: "error", text: err instanceof Error ? err.message : String(err) });
    }
  }

  return (
    <div className="tt-pad-overlay">
      <div className="tt-panel" style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 14, padding: 24 }}>
        <div className="tt-col-title">Reprint Label — 15 × 10 cm</div>
        <ProductionLabelPreview d={data} />
        {status.phase !== "idle" && (
          <div
            className="tt-note"
            style={{
              whiteSpace: "pre-line",
              textAlign: "center",
              color: status.phase === "error" ? "var(--tt-red)" : status.phase === "done" ? "var(--tt-green)" : undefined,
            }}
          >
            {status.text}
          </div>
        )}
        <div style={{ display: "flex", gap: 12 }}>
          <button
            className="tt-btn tt-primary"
            style={{ minHeight: 54, fontSize: 17, padding: "0 28px" }}
            disabled={status.phase === "printing"}
            onClick={() => void print()}
          >
            {status.phase === "printing" ? "Printing…" : "🖨 Print"}
          </button>
          <button className="tt-btn tt-ghost" style={{ minHeight: 54, fontSize: 17 }} onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
