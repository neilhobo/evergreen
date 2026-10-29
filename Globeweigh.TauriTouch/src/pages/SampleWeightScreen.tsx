import { useEffect, useState } from "react";
import TopBar from "../components/touch/TopBar";
import { keypad } from "../components/touch/Keypad";
import { messageDialog } from "../components/confirmDialog";
import { addSampleWeight, getToBeSampled, SampleRecord } from "../api/touch";

interface Props {
  onHome: () => void;
}

const ymd = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

// Sample Weight. Ports the WPF SampleWeightViewModel: records weighed in on the selected day
// (date back/forward, default today, auto-refresh), pick one, key in a decimal sample box
// weight (kg), which writes SampleBoxWeight + DateSampleWeightAdded.
export default function SampleWeightScreen({ onHome }: Props) {
  const [day, setDay] = useState(() => {
    const d = new Date();
    d.setHours(0, 0, 0, 0);
    return d;
  });
  const [rows, setRows] = useState<SampleRecord[]>([]);
  const [selected, setSelected] = useState<SampleRecord | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function refresh() {
    try {
      const list = await getToBeSampled(ymd(day));
      setRows(list);
      setSelected((cur) => (cur ? list.find((r) => r.id === cur.id) ?? null : null));
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  }

  useEffect(() => {
    void refresh();
    const t = window.setInterval(() => void refresh(), 60_000);
    return () => window.clearInterval(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [day]);

  function shiftDay(n: number) {
    setDay((d) => {
      const nd = new Date(d);
      nd.setDate(nd.getDate() + n);
      return nd;
    });
    setSelected(null);
  }

  async function enterSample() {
    if (!selected) return;
    const v = await keypad({
      title: "Enter Sample Weight (kg)",
      decimal: true,
      initial: selected.sampleBoxWeight != null ? String(selected.sampleBoxWeight) : "",
    });
    if (v === null) return;
    const w = Number(v);
    if (!isFinite(w) || v.trim() === "") {
      void messageDialog("Please enter a valid weight.");
      return;
    }
    setBusy(true);
    try {
      await addSampleWeight(selected.id, w);
      await refresh();
    } catch (err) {
      void messageDialog("Failed to save sample weight.\n" + (err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const label = day.toLocaleDateString(undefined, {
    weekday: "short",
    day: "2-digit",
    month: "short",
    year: "numeric",
  });
  const isToday = ymd(day) === ymd(new Date());

  const dateNav = (
    <div className="tt-barrier" style={{ gap: 14, fontSize: 16 }}>
      <button className="tt-btn tt-ghost" style={{ minHeight: 40, padding: "0 14px" }} onClick={() => shiftDay(-1)}>◀</button>
      <span style={{ minWidth: 200, textAlign: "center" }}>{label}</span>
      <button className="tt-btn tt-ghost" style={{ minHeight: 40, padding: "0 14px" }} disabled={isToday} onClick={() => shiftDay(1)}>▶</button>
    </div>
  );

  return (
    <div className="tt-app">
      <TopBar title="Sample Weight" onBack={onHome} right={dateNav} hideBrand />
      <div className="tt-body">
        <div style={{ display: "grid", gridTemplateColumns: "1fr 350px", gap: 16, height: "100%" }}>
          {/* Left: deliveries grid */}
          <div className="tt-panel" style={{ display: "flex", flexDirection: "column", minHeight: 0 }}>
            <div className="tt-section-title">Deliveries (No test weight recorded)</div>
            {error && <div className="tt-error" style={{ marginBottom: 10 }}>{error}</div>}
            <div className="tt-grid-wrap">
              <table className="tt-grid">
                <thead>
                  <tr>
                    <th className="tt-col-grow">Haulier</th>
                    <th>Vehicle Reg</th>
                    <th>Product</th>
                    <th>Supplier</th>
                    <th>Bog</th>
                    <th className="tt-center-col">Sample</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => {
                    const sampled = r.sampleCollected ?? r.sampleBoxWeight != null;
                    return (
                      <tr
                        key={r.id}
                        className={selected?.id === r.id ? "tt-selected" : ""}
                        onClick={() => setSelected(r)}
                      >
                        <td className="tt-col-grow">{r.haulierName}</td>
                        <td>{r.vehicleReg}</td>
                        <td>{r.product}</td>
                        <td>{r.supplier}</td>
                        <td>{r.bogName}</td>
                        <td className="tt-center-col">{sampled ? <span className="tt-tick-badge">✓</span> : ""}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
              {rows.length === 0 && (
                <div className="tt-note" style={{ padding: 18 }}>No records weighed in on this day.</div>
              )}
            </div>
          </div>

          {/* Right: only the sample weight input */}
          <div className="tt-panel" style={{ display: "flex", flexDirection: "column" }}>
            <div className="tt-section-title">Sample Weight</div>
            {selected ? (
              <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 24, marginTop: 24 }}>
                <div style={{ fontSize: 22 }}>Sample Weight (kg)</div>
                <div
                  className={`tt-textbox${selected.sampleBoxWeight == null ? " tt-empty" : ""}`}
                  style={{ width: 220, height: 80, fontSize: 30, cursor: busy ? "default" : "pointer" }}
                  onClick={busy ? undefined : enterSample}
                >
                  {selected.sampleBoxWeight != null ? String(selected.sampleBoxWeight) : "Tap to enter"}
                </div>
              </div>
            ) : (
              <div className="tt-center" style={{ height: "100%" }}>
                <div className="tt-note" style={{ fontSize: 18 }}>Select a delivery</div>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
