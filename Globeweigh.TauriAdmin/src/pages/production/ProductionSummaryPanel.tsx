import ToolbarDate from "../../components/ToolbarDate";
import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import { todayInput } from "../../format";
import { getProductionSummary, ProductionSummaryItem } from "../../api/production";

// Production Summary — mirrors the WPF ProductionSummaryView: totals per product over
// a date range, optionally grouped by production line, with pallet/bag totals.
export default function ProductionSummaryPanel() {
  const [rows, setRows] = useState<ProductionSummaryItem[]>([]);
  const [from, setFrom] = useState(todayInput(-5));
  const [to, setTo] = useState(todayInput(1));
  const [byLine, setByLine] = useState(false);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(false);

  async function refresh() {
    setLoading(true);
    try {
      setRows(await getProductionSummary(from, to, byLine));
    } catch (err) {
      setRows([]);
      void messageDialog("Failed to load production summary.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [from, to, byLine]);

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return rows;
    return rows.filter((r) => [r.gtin, r.description].some((v) => v?.toLowerCase().includes(term)));
  }, [rows, term]);

  const totals = useMemo(
    () => ({
      pallets: filtered.reduce((s, r) => s + (r.recordCount || 0), 0),
      bags: filtered.reduce((s, r) => s + (r.bagCount || 0), 0),
    }),
    [filtered],
  );

  const fmt = (n: number) => n.toLocaleString();

  const columns: Column<ProductionSummaryItem>[] = [
    ...(byLine
      ? [{ key: "lineNumber", header: "Line No", width: 70, align: "right" as const }]
      : []),
    { key: "gtin", header: "Evergreen Code", width: 140, render: (r) => <Highlight text={r.gtin} term={term} /> },
    { key: "description", header: "Product", render: (r) => <Highlight text={r.description} term={term} /> },
    { key: "size", header: "Size", width: 70, align: "right" },
    { key: "upp", header: "UPP", width: 70, align: "right" },
    { key: "recordCount", header: "No. Of Pallets", width: 120, align: "right", render: (r) => fmt(r.recordCount) },
    { key: "bagCount", header: "Bags", width: 110, align: "right", render: (r) => fmt(r.bagCount) },
  ];

  return (
    <div className="po-main production-page">
      <div className="toolbar">
        <ToolbarDate label="From" value={from} onChange={setFrom} />
        <ToolbarDate label="To" value={to} onChange={setTo} />
        <button className="toolbar-btn" onClick={() => void refresh()} disabled={loading}>
          Search
        </button>
        <label className="stock-radio" style={{ marginLeft: 6 }}>
          <input type="checkbox" checked={byLine} onChange={(e) => setByLine(e.target.checked)} />
          Group by Line
        </label>
        <input
          className="form-input toolbar-search"
          placeholder="Search products…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        {loading && <span className="toolbar-note">Loading…</span>}
        <div className="toolbar-right">
          <span className="prod-summary-total">
            Pallets: <strong>{fmt(totals.pallets)}</strong>
          </span>
          <span className="prod-summary-total">
            Bags: <strong>{fmt(totals.bags)}</strong>
          </span>
          <span className="prod-summary-total">
            Products: <strong>{fmt(filtered.length)}</strong>
          </span>
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={filtered}
        rowKey={(r) => `${r.id ?? "x"}-${r.lineNumber ?? "n"}`}
        emptyText={loading ? "Loading…" : "No production in this range"}
      />
    </div>
  );
}
