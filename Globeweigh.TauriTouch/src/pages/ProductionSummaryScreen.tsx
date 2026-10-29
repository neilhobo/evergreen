import { useEffect, useState } from "react";
import TopBar from "../components/touch/TopBar";
import { getProductionSummary, ProductionSummary } from "../api/touch";

interface Props {
  onHome: () => void;
}

const ymd = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

// Production Summary. Ports the WPF ProductionSummaryView: per-product pallet totals for the
// selected day (date back/forward, default today), reusing GET /api/production/summary.
export default function ProductionSummaryScreen({ onHome }: Props) {
  const [day, setDay] = useState(() => {
    const d = new Date();
    d.setHours(0, 0, 0, 0);
    return d;
  });
  const [rows, setRows] = useState<ProductionSummary[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function refresh() {
    setLoading(true);
    try {
      const date = ymd(day);
      setRows(await getProductionSummary(date, date));
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [day]);

  function shiftDay(n: number) {
    setDay((d) => {
      const nd = new Date(d);
      nd.setDate(nd.getDate() + n);
      return nd;
    });
  }

  const label = day.toLocaleDateString(undefined, { weekday: "short", day: "2-digit", month: "short", year: "numeric" });
  const isToday = ymd(day) === ymd(new Date());
  const totalPallets = rows.reduce((sum, r) => sum + r.recordCount, 0);

  const dateNav = (
    <div className="tt-barrier" style={{ gap: 14, fontSize: 16 }}>
      <button className="tt-btn tt-ghost" style={{ minHeight: 40, padding: "0 14px" }} onClick={() => shiftDay(-1)}>◀</button>
      <span style={{ minWidth: 200, textAlign: "center" }}>{label}</span>
      <button className="tt-btn tt-ghost" style={{ minHeight: 40, padding: "0 14px" }} disabled={isToday} onClick={() => shiftDay(1)}>▶</button>
    </div>
  );

  return (
    <div className="tt-app">
      <TopBar title="Production Summary" onBack={onHome} right={dateNav} hideBrand />
      <div className="tt-body">
        <div className="tt-panel" style={{ display: "flex", flexDirection: "column", height: "100%", minHeight: 0 }}>
          {error && <div className="tt-error" style={{ marginBottom: 10 }}>{error}</div>}
          <div className="tt-grid-wrap">
            <table className="tt-grid">
              <thead>
                <tr>
                  <th>Evergreen Code</th>
                  <th className="tt-col-grow">Product</th>
                  <th>Size</th>
                  <th>UPP</th>
                  <th className="tt-center-col">No. Of Pallets</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r, i) => (
                  <tr key={r.id ?? i}>
                    <td>{r.gtin}</td>
                    <td className="tt-col-grow">{r.description}</td>
                    <td>{r.size}</td>
                    <td>{r.upp}</td>
                    <td className="tt-center-col">{r.recordCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {rows.length === 0 && !loading && !error && (
              <div className="tt-note" style={{ padding: 18 }}>No production recorded on this day.</div>
            )}
            {loading && rows.length === 0 && <div className="tt-note" style={{ padding: 18 }}>Loading…</div>}
          </div>
          {/* Footer: product count + total pallets */}
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", paddingTop: 12 }}>
            <div className="tt-note">Products = {rows.length}</div>
            <div className="tt-note">Total pallets = {totalPallets}</div>
          </div>
        </div>
      </div>
    </div>
  );
}
