import ToolbarDate from "../../components/ToolbarDate";
import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import {
  StockLocationCounts,
  StockMovement,
  StockSummaryItem,
  getStockHistory,
  getStockLocationCounts,
  getStockSummary,
} from "../../api/stock";
import { fmtDateTime, todayInput } from "../../format";

// Current Stock — per-product pallet counts over the in-stock pallets of the CodicoPallet
// master list. Pallets go in/out of stock via "Make Current Stock" on a stock take.
// The location list on the left filters the view and is a breakdown of "All": the
// satellite yards show their counted stock (latest made-current take, plus transfers and
// manual corrections since), and Main Yard shows the rest of the in-stock pallets — so
// production and dispatch land there without waiting on the next stock take.
export default function CurrentStockPanel() {
  const [rows, setRows] = useState<StockSummaryItem[]>([]);
  const [counts, setCounts] = useState<StockLocationCounts | null>(null);
  const [locationId, setLocationId] = useState(0); // 0 = All
  const [search, setSearch] = useState("");
  // WPF-style auto-filter row under the grid headers, one box per column.
  const [codeFilter, setCodeFilter] = useState("");
  const [productFilter, setProductFilter] = useState("");
  const [sizeFilter, setSizeFilter] = useState("");
  const [uppFilter, setUppFilter] = useState("");
  const [loading, setLoading] = useState(false);

  const [history, setHistory] = useState<StockMovement[]>([]);
  // History date range — the 7D preset by default (last 7 days including today).
  const [histFrom, setHistFrom] = useState(todayInput(-6));
  const [histTo, setHistTo] = useState(todayInput());
  // Click a product in the stock grid to filter the history to it (click again to clear).
  const [historyProduct, setHistoryProduct] = useState<StockSummaryItem | null>(null);

  // Live feed: silently re-poll the movement list every 10s while the tab is open.
  useEffect(() => {
    const id = window.setInterval(() => {
      getStockHistory(histFrom, histTo)
        .then(setHistory)
        .catch(() => {
          /* transient — next tick retries */
        });
    }, 10_000);
    return () => window.clearInterval(id);
  }, [histFrom, histTo]);

  async function refresh(locId: number) {
    setLoading(true);
    try {
      const [summary, locCounts, hist] = await Promise.all([
        getStockSummary(locId > 0 ? locId : undefined),
        getStockLocationCounts(),
        getStockHistory(histFrom, histTo),
      ]);
      setRows(summary);
      setCounts(locCounts);
      setHistory(hist);
    } catch (err) {
      setRows([]);
      void messageDialog("Failed to load stock summary.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh(locationId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [locationId, histFrom, histTo]);

  const term = search.trim().toLowerCase();
  const codeTerm = codeFilter.trim().toLowerCase();
  const productTerm = productFilter.trim().toLowerCase();
  const sizeTerm = sizeFilter.trim();
  const uppTerm = uppFilter.trim();
  const filtered = useMemo(() => {
    let result = rows;
    if (term) {
      result = result.filter((r) =>
        [r.gtin, r.description, r.code13].some((v) => v?.toLowerCase().includes(term)),
      );
    }
    // Auto-filter row: contains for the text columns, starts-with for the numeric ones.
    if (codeTerm) {
      result = result.filter((r) => (r.gtin ?? r.code13)?.toLowerCase().includes(codeTerm));
    }
    if (productTerm) {
      result = result.filter((r) => r.description?.toLowerCase().includes(productTerm));
    }
    if (sizeTerm) {
      result = result.filter((r) => r.size != null && String(r.size).startsWith(sizeTerm));
    }
    if (uppTerm) {
      result = result.filter((r) => r.upp != null && String(r.upp).startsWith(uppTerm));
    }
    return result;
  }, [rows, term, codeTerm, productTerm, sizeTerm, uppTerm]);

  const totals = useMemo(
    () => ({
      pallets: filtered.reduce((s, r) => s + (r.palletCount || 0), 0),
    }),
    [filtered],
  );

  const fmt = (n: number) => n.toLocaleString();
  const byLocation = locationId > 0;

  const columns: Column<StockSummaryItem>[] = [
    {
      key: "gtin",
      header: "Evergreen Code",
      width: 140,
      render: (r) => <Highlight text={r.gtin ?? r.code13} term={term} />,
      filter: { value: codeFilter, onChange: setCodeFilter },
    },
    {
      key: "description",
      header: "Product",
      render: (r) =>
        r.description ? (
          <Highlight text={r.description} term={term} />
        ) : (
          <span className="muted">Unknown product{r.code13 ? ` (${r.code13})` : ""}</span>
        ),
      filter: { value: productFilter, onChange: setProductFilter },
    },
    {
      key: "size",
      header: "Size",
      width: 70,
      align: "right",
      filter: { value: sizeFilter, onChange: setSizeFilter },
    },
    {
      key: "upp",
      header: "UPP",
      width: 70,
      align: "right",
      filter: { value: uppFilter, onChange: setUppFilter },
    },
    {
      key: "palletCount",
      header: "No. Of Pallets",
      fit: true,
      align: "right",
      render: (r) => <strong>{fmt(r.palletCount)}</strong>,
    },
  ];

  // +N additions green, −N depletions red, transfers amber (net-zero moves).
  const qtyCell = (r: StockMovement) =>
    r.direction > 0 ? (
      <span style={{ color: "#16a34a", fontWeight: 700 }}>+{r.qty.toLocaleString()}</span>
    ) : r.direction < 0 ? (
      <span style={{ color: "#dc2626", fontWeight: 700 }}>−{r.qty.toLocaleString()}</span>
    ) : (
      <span style={{ color: "#b8860b", fontWeight: 700 }}>{r.qty.toLocaleString()}</span>
    );

  // Badge colours per movement type, matching the +/− quantity styling.
  const typeBadge = (r: StockMovement) => {
    const cls =
      r.type === "Production"
        ? "row-badge-production"
        : r.type === "Order"
          ? "row-badge-order"
          : r.type === "Transfer"
            ? "row-badge-transfer"
            : r.type === "Manual"
              ? "row-badge-manual"
              : "row-badge-take";
    return <span className={`row-badge ${cls}`}>{r.type}</span>;
  };

  const historyColumns: Column<StockMovement>[] = [
    { key: "date", header: "Date", fit: true, render: (r) => fmtDateTime(r.date) },
    { key: "type", header: "Type", fit: true, render: typeBadge },
    { key: "description", header: "Product", render: (r) => r.description },
    { key: "size", header: "Size", fit: true, align: "right", render: (r) => r.size },
    { key: "reference", header: "Ref", fit: true, render: (r) => r.reference },
    { key: "qty", header: "Pallets", fit: true, align: "right", render: qtyCell },
  ];

  return (
    <div className="po-open-layout production-page current-stock" style={{ flex: 1, minHeight: 0 }}>
      {/* Left: location filter — "All" is every in-stock pallet, and the locations below
          break that total down (satellites counted, Main Yard the remainder). */}
      <div className="stock-types" style={{ width: 180, flex: "none", overflow: "auto" }}>
        <div
          className={`stock-type-item${locationId === 0 ? " active" : ""}`}
          style={{ display: "flex", justifyContent: "space-between", gap: 8 }}
          onClick={() => setLocationId(0)}
        >
          <span>All</span>
          <span>{counts ? fmt(counts.allPallets) : ""}</span>
        </div>
        {(counts?.locations ?? []).map((l) => (
          <div
            key={l.id}
            className={`stock-type-item${locationId === l.id ? " active" : ""}`}
            style={{ display: "flex", justifyContent: "space-between", gap: 8 }}
            onClick={() => setLocationId(l.id)}
          >
            <span>{l.name}</span>
            <span>{fmt(l.palletCount)}</span>
          </div>
        ))}
      </div>

      <div className="po-main">
        <div className="toolbar">
          <button className="toolbar-btn" onClick={() => void refresh(locationId)} disabled={loading}>
            Refresh
          </button>
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
              Products: <strong>{fmt(filtered.length)}</strong>
            </span>
          </div>
        </div>

        <DataTable
          columns={columns}
          rows={filtered}
          rowKey={(r) => `${r.code13 ?? "no-code"}-${r.gtin ?? ""}`}
          selectedKey={
            historyProduct ? `${historyProduct.code13 ?? "no-code"}-${historyProduct.gtin ?? ""}` : null
          }
          onSelect={(r) =>
            setHistoryProduct((cur) =>
              cur && cur.productId === r.productId ? null : r.productId != null ? r : null,
            )
          }
          groupKey={(r) => String(r.categoryId ?? "none")}
          groupLabel={(r) => r.category ?? "Uncategorised"}
          emptyText={
            loading
              ? "Loading…"
              : byLocation
                ? "No current stock for this location — make a stock take current first"
                : "No stock"
          }
        />
      </div>

      {/* Right: the live stock movement feed. stock-take-items switches the table to
          auto layout so the best-fit columns size properly. */}
      <div className="po-side stock-take-items" style={{ width: "auto", flex: 1, minWidth: 340 }}>
        <h3 className="po-side-title">
          Stock History
          {historyProduct && (
            <span style={{ marginLeft: 8, fontWeight: 600 }}>
              — {historyProduct.description ?? historyProduct.gtin}{" "}
              <button className="link-btn" onClick={() => setHistoryProduct(null)}>
                show all
              </button>
            </span>
          )}
        </h3>
        <div className="toolbar" style={{ flex: "none" }}>
          <ToolbarDate label="From" value={histFrom} onChange={setHistFrom} />
          <ToolbarDate label="To" value={histTo} onChange={setHistTo} />
          {[2, 4, 7].map((d) => {
            const from = todayInput(-(d - 1));
            const active = histFrom === from && histTo === todayInput();
            return (
              <button
                key={d}
                className={`range-chip${active ? " active" : ""}`}
                onClick={() => {
                  setHistFrom(from);
                  setHistTo(todayInput());
                }}
              >
                {d}D
              </button>
            );
          })}
        </div>
        <DataTable
          columns={historyColumns}
          rows={
            historyProduct
              ? history.filter((m) => m.productId != null && m.productId === historyProduct.productId)
              : history
          }
          rowKey={(r) => r.id}
          emptyText={loading ? "Loading…" : "No stock movements in this range"}
        />
        <div
          className="muted"
          style={{ flex: "none", fontSize: 11, padding: "4px 2px", display: "flex", alignItems: "center", gap: 6 }}
        >
          <span
            style={{
              width: 8,
              height: 8,
              borderRadius: "50%",
              background: "#dc2626",
              flex: "none",
            }}
          />
          Production is grouped per product per day — the row grows as pallets arrive.
        </div>
      </div>
    </div>
  );
}
