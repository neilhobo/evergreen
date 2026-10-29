import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import {
  getCostingProducts,
  getProductZones,
  getZonePriceHistory,
  CostingZonePrice,
  ZonePriceHistory,
} from "../../api/costing";
import { Product } from "../../api/products";
import { fmtDateTime } from "../../format";
import ZonePriceEditModal from "./ZonePriceEditModal";

// Costing price list — mirrors the WPF CostingPriceListView: Product List →
// Zone Pricing (on product select) → Pricing History (on zone-price select).
export default function CostingPricingPanel() {
  const [rows, setRows] = useState<Product[]>([]);
  const [selected, setSelected] = useState<Product | null>(null);
  const [zones, setZones] = useState<CostingZonePrice[]>([]);
  const [selectedZone, setSelectedZone] = useState<CostingZonePrice | null>(null);
  const [history, setHistory] = useState<ZonePriceHistory[]>([]);
  const [editing, setEditing] = useState<CostingZonePrice | null>(null);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(false);
  const [zonesLoading, setZonesLoading] = useState(false);
  const [historyLoading, setHistoryLoading] = useState(false);

  async function refresh() {
    setLoading(true);
    try {
      setRows(await getCostingProducts());
      setSelected(null);
      setZones([]);
      setSelectedZone(null);
      setHistory([]);
    } catch (err) {
      void messageDialog("Failed to load products.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  async function selectProduct(p: Product) {
    setSelected(p);
    setSelectedZone(null);
    setHistory([]);
    setZonesLoading(true);
    try {
      setZones(await getProductZones(p.id));
    } catch (err) {
      setZones([]);
      void messageDialog("Failed to load zone prices.\n" + (err as Error).message);
    } finally {
      setZonesLoading(false);
    }
  }

  async function selectZone(z: CostingZonePrice) {
    setSelectedZone(z);
    setHistoryLoading(true);
    try {
      setHistory(await getZonePriceHistory(z.id));
    } catch (err) {
      setHistory([]);
      void messageDialog("Failed to load pricing history.\n" + (err as Error).message);
    } finally {
      setHistoryLoading(false);
    }
  }

  async function reloadZones() {
    if (!selected) return;
    setZonesLoading(true);
    try {
      setZones(await getProductZones(selected.id));
      setSelectedZone(null);
      setHistory([]);
    } catch (err) {
      void messageDialog("Failed to load zone prices.\n" + (err as Error).message);
    } finally {
      setZonesLoading(false);
    }
  }

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return rows;
    return rows.filter((r) =>
      [r.description, r.gtin, r.categoryName].some((v) => v?.toLowerCase().includes(term)),
    );
  }, [rows, term]);

  // WPF CostingPriceListView product grid: Description | LTRS | UPP | Barcode | Category.
  const columns: Column<Product>[] = [
    { key: "description", header: "Description", render: (r) => <Highlight text={r.description} term={term} /> },
    { key: "size", header: "LTRS", width: 50, render: (r) => r.size ?? "" },
    { key: "upp", header: "UPP", width: 50, render: (r) => r.upp },
    { key: "gtin", header: "Barcode", fit: true, render: (r) => <Highlight text={r.gtin} term={term} /> },
    { key: "categoryName", header: "Category", fit: true, render: (r) => <Highlight text={r.categoryName} term={term} /> },
  ];

  // WPF zone pricing grid: Zone | (blank header = extra info) | Price.
  const zoneColumns: Column<CostingZonePrice>[] = [
    { key: "zone", header: "Zone", width: 150, render: (r) => r.description },
    { key: "secondaryDescription", header: " ", width: 110, render: (r) => r.secondaryDescription },
    {
      key: "price",
      header: "Price",
      width: 70,
      align: "right",
      render: (r) => (r.na ? "N/A" : (r.isEuro ? "€" : "£") + r.price.toFixed(2)),
    },
  ];

  // WPF pricing history grid: Old Price | New Price | Changed By | Date of change.
  const historyColumns: Column<ZonePriceHistory>[] = [
    { key: "oldPrice", header: "Old Price", width: 70, align: "right", render: (r) => r.oldPrice.toFixed(2) },
    { key: "newPrice", header: "New Price", width: 70, align: "right", render: (r) => r.newPrice.toFixed(2) },
    { key: "changedBy", header: "Changed By", render: (r) => r.changedBy },
    { key: "dateCreated", header: "Date of change", width: 120, render: (r) => fmtDateTime(r.dateCreated) },
  ];

  return (
    <div className="po-open-layout costing-pricing" style={{ flex: 1, minHeight: 0 }}>
      <div className="po-main">
        <div className="toolbar">
          <input
            className="form-input toolbar-search"
            placeholder="Search products…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {loading && <span className="toolbar-note">Loading…</span>}
          <div className="toolbar-right">
            <button className="toolbar-btn" onClick={() => void refresh()} disabled={loading}>
              Refresh
            </button>
          </div>
        </div>

        <DataTable
          columns={columns}
          rows={filtered}
          rowKey={(r) => r.id}
          selectedKey={selected?.id ?? null}
          onSelect={(r) => void selectProduct(r)}
          emptyText={loading ? "Loading…" : "No products"}
        />
      </div>

      {/* The side panels keep their fixed width at all times so the product grid
          never resizes — they are invisible (not collapsed) until selection. */}
      <div className="po-side" style={{ width: 350, visibility: selected ? "visible" : "hidden" }}>
          <div className="toolbar">
            <h3 className="po-side-title">Zone Pricing</h3>
            <div className="toolbar-right">
              <button
                className="toolbar-btn"
                disabled={!selectedZone}
                onClick={() => selectedZone && setEditing(selectedZone)}
              >
                Edit
              </button>
            </div>
          </div>
          <DataTable
            columns={zoneColumns}
            rows={zones}
            rowKey={(r) => r.id}
            selectedKey={selectedZone?.id ?? null}
            onSelect={(r) => void selectZone(r)}
            onDoubleClick={(r) => setEditing(r)}
            emptyText={zonesLoading ? "Loading…" : "No zone prices"}
          />
        </div>

      <div
        className="po-side"
        style={{ width: 380, visibility: selectedZone ? "visible" : "hidden" }}
      >
          <div className="toolbar">
            <h3 className="po-side-title">Pricing History</h3>
          </div>
          <DataTable
            columns={historyColumns}
            rows={history}
            rowKey={(r) => r.id}
            emptyText={historyLoading ? "Loading…" : "No price changes"}
          />
        </div>

      {editing && (
        <ZonePriceEditModal
          existing={editing}
          onClose={(saved) => {
            setEditing(null);
            if (saved) void reloadZones();
          }}
        />
      )}
    </div>
  );
}
