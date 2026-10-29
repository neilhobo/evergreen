import { useEffect, useRef, useState } from "react";
import TopBar from "../components/touch/TopBar";
import WeightGauge from "../components/touch/WeightGauge";
import { keyboard } from "../components/touch/Keyboard";
import { messageDialog } from "../components/confirmDialog";
import { getPending, PendingRecord, weighOut } from "../api/touch";
import { fmtDateTime, fmtQty } from "../format";
import SuperAdminGear from "../components/touch/SuperAdminGear";
import DbSwitcher from "../components/DbSwitcher";

interface Props {
  onOpenSettings: () => void;
}

const stripWs = (s: string | null | undefined) => (s ?? "").replace(/\s+/g, "").toLowerCase();

// Weigh-Out screen. Mirrors the WPF WeighbridgeOutViewModel: an auto-refreshing pending list,
// confirm the vehicle reg before weighing, then record the out-weight. Kiosk: no login.
export default function WeighOutScreen({ onOpenSettings }: Props) {
  const [step, setStep] = useState<"list" | "weigh">("list");
  const [pending, setPending] = useState<PendingRecord[]>([]);
  const [selected, setSelected] = useState<PendingRecord | null>(null);
  const [confirmedReg, setConfirmedReg] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [overlay, setOverlay] = useState<string | null>(null);
  const [countdown, setCountdown] = useState<number | null>(null);

  const weightRef = useRef<number | null>(null);
  const [healthy, setHealthy] = useState(false);
  const onWeight = (w: number | null, ok: boolean) => { weightRef.current = w; setHealthy(ok); };

  async function refresh() {
    try {
      const list = await getPending();
      setPending(list);
      setSelected((cur) => (cur ? list.find((r) => r.id === cur.id) ?? null : null));
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  }

  // Initial load + auto-refresh while on the list. The WPF screen polled once a minute and
  // had a Refresh button; with no button here the poll is the only way a newly weighed-in
  // vehicle appears, so it runs oftener.
  useEffect(() => {
    if (step !== "list") return;
    void refresh();
    const t = window.setInterval(() => void refresh(), 20_000);
    return () => window.clearInterval(t);
  }, [step]);

  // Success countdown → back to list.
  useEffect(() => {
    if (countdown === null) return;
    if (countdown <= 0) {
      setOverlay(null);
      setCountdown(null);
      setSelected(null);
      setStep("list");
      return;
    }
    const t = window.setTimeout(() => setCountdown((c) => (c === null ? null : c - 1)), 1000);
    return () => window.clearTimeout(t);
  }, [countdown]);

  async function startWeighOut() {
    if (!selected) return;
    const entered = await keyboard({ title: "Enter Vehicle Reg to confirm correct Load" });
    if (entered === null) return;
    if (stripWs(entered) !== stripWs(selected.vehicleReg)) {
      void messageDialog("Vehicle Reg entered does not match");
      return;
    }
    setConfirmedReg(entered);
    setStep("weigh");
  }

  async function doWeighOut() {
    if (!selected || weightRef.current === null) return;
    setBusy(true);
    try {
      await weighOut(selected.id, confirmedReg, weightRef.current);
      setOverlay("Weigh out complete");
      setCountdown(5);
    } catch (err) {
      void messageDialog("Weigh out failed.\n" + (err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="tt-app">
      <TopBar title="Weigh Out" onBack={step === "weigh" ? () => setStep("list") : undefined} />
      <div className="tt-body">
        {step === "list" && (
          <div className="tt-weighout-list">
            {/* Left: the pending grid, columns as the WPF WeighbridgeOutView had them. */}
            <div className="tt-panel" style={{ display: "flex", flexDirection: "column", minHeight: 0 }}>
              <div className="tt-section-title">Pending — select a vehicle to weigh out</div>
              {error && <div className="tt-error" style={{ marginBottom: 10 }}>{error}</div>}
              <div className="tt-grid-wrap">
                <table className="tt-grid">
                  <thead>
                    <tr>
                      <th className="tt-col-grow">Haulier</th>
                      <th>Vehicle Reg</th>
                      <th>Product</th>
                      <th>Weigh Date</th>
                      <th>Weight In (Kg)</th>
                    </tr>
                  </thead>
                  <tbody>
                    {pending.map((r) => (
                      <tr
                        key={r.id}
                        className={selected?.id === r.id ? "tt-selected" : ""}
                        onClick={() => setSelected(r)}
                      >
                        <td className="tt-col-grow">{r.haulierName}</td>
                        <td>{r.vehicleReg}</td>
                        <td>{r.product}</td>
                        <td>{fmtDateTime(r.dateWeightIn)}</td>
                        <td>{fmtQty(r.weightIn)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {pending.length === 0 && (
                  <div className="tt-note" style={{ padding: 18 }}>No pending vehicles.</div>
                )}
              </div>
            </div>
            {/* Right: the screen's action in a column of its own, as the WPF view kept its
                Weigh Out button beside the grid rather than under it. No Refresh — the list
                polls on its own. */}
            <div className="tt-panel tt-weighout-actions">
              <button className="tt-btn tt-primary" disabled={!selected} onClick={startWeighOut}>Weigh Out →</button>
            </div>
          </div>
        )}

        {step === "weigh" && selected && (
          <div style={{ display: "grid", gridTemplateColumns: "1fr 460px", gap: 26 }}>
            <div className="tt-panel">
              <h2 className="tt-h2">Weigh Out</h2>
              <Field label="Vehicle Reg" value={selected.vehicleReg} />
              <Field label="Haulier" value={selected.haulierName} />
              <Field label="Product" value={selected.product} />
              <Field label="Weight In" value={`${selected.weightIn} kg`} />
            </div>
            <div className="tt-panel tt-weigh-side" style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 22 }}>
              <WeightGauge onChange={onWeight} />
              <button className="tt-btn tt-primary" style={{ width: "100%" }} disabled={!healthy || busy} onClick={doWeighOut}>
                {busy ? "Weighing…" : "Weigh Out"}
              </button>
              {!healthy && <div className="tt-error">Waiting for live weight…</div>}
            </div>
          </div>
        )}
      </div>

      {overlay && (
        <div className="tt-overlay tt-success">
          <div className="tt-overlay-ico">✓</div>
          <div className="tt-overlay-title">{overlay}</div>
          {countdown !== null && <div className="tt-countdown">{countdown}</div>}
        </div>
      )}

      {/* Footer on the pending list only — this screen's home step. The build number, gear
          and DB switcher get a strip of their own here rather than floating in the corner,
          where they sat on top of the grid and action panels. */}
      {step === "list" && (
        <div className="tt-footer">
          {/* Dev builds only: which API / database this run is pointed at, and a click to
              flip it. Prod builds show nothing. */}
          {import.meta.env.DEV && (
            <div className="tt-devbar">
              <DbSwitcher />
            </div>
          )}
          <span className="tt-build-ver">{__BUILD_VERSION__}</span>
          <SuperAdminGear onOpen={onOpenSettings} />
        </div>
      )}
    </div>
  );
}

function Field({ label, value }: { label: string; value?: string | null }) {
  return (
    <div className="tt-field">
      <div className="tt-label">{label}</div>
      <div className="tt-value tt-readonly">{value || "—"}</div>
    </div>
  );
}
