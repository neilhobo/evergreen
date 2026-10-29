import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import { getCostingProducts, getAllZonePrices, CostingZonePrice } from "../../api/costing";
import { getCostingZones, CostingZoneRow } from "../../api/costingZones";
import { Product } from "../../api/products";
import ProductZonePricesModal from "./ProductZonePricesModal";

// Costing Full — the printed-pricelist matrix: one row per product, one price
// column per costing zone (products grouped by category like the PDF pricelist).
export default function CostingFullPanel() {
  const [products, setProducts] = useState<Product[]>([]);
  const [zones, setZones] = useState<CostingZoneRow[]>([]);
  const [prices, setPrices] = useState<CostingZonePrice[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(false);
  const [selected, setSelected] = useState<Product | null>(null);
  const [editing, setEditing] = useState<Product | null>(null);

  async function refresh() {
    setLoading(true);
    try {
      const [p, z, pr] = await Promise.all([
        getCostingProducts(),
        getCostingZones(),
        getAllZonePrices(),
      ]);
      setProducts(p);
      setZones(z);
      setPrices(pr);
    } catch (err) {
      void messageDialog("Failed to load costing matrix.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  // productId -> zoneId -> price row.
  const priceMap = useMemo(() => {
    const map = new Map<number, Map<number, CostingZonePrice>>();
    for (const p of prices) {
      let byZone = map.get(p.productId);
      if (!byZone) {
        byZone = new Map();
        map.set(p.productId, byZone);
      }
      byZone.set(p.costingZoneId, p);
    }
    return map;
  }, [prices]);

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return products;
    return products.filter((r) =>
      [r.description, r.gtin, r.categoryName].some((v) => v?.toLowerCase().includes(term)),
    );
  }, [products, term]);

  const columns: Column<Product>[] = useMemo(() => {
    const cols: Column<Product>[] = [
      { key: "description", header: "Product", fit: true, render: (r) => <Highlight text={r.description} term={term} /> },
      { key: "size", header: "Ltrs", width: 45, render: (r) => r.size ?? "" },
      { key: "upp", header: "Units", width: 45, render: (r) => r.upp },
      { key: "gtin", header: "Barcode", fit: true, render: (r) => <Highlight text={r.gtin} term={term} /> },
    ];
    for (const z of zones) {
      cols.push({
        key: `zone-${z.id}`,
        header: z.secondaryDescription ? (
          <>
            {z.description}
            <br />
            {z.secondaryDescription}
          </>
        ) : (
          z.description
        ),
        width: 60,
        align: "right",
        hoverHighlight: true,
        render: (r) => {
          const price = priceMap.get(r.id)?.get(z.id);
          if (!price) return "";
          return price.na ? "N/A" : price.price.toFixed(2);
        },
      });
    }
    return cols;
  }, [zones, priceMap, term]);

  return (
    <div className="po-open-layout costing-full" style={{ flex: 1, minHeight: 0 }}>
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
            <button
              className="toolbar-btn"
              disabled={!selected}
              onClick={() => selected && setEditing(selected)}
            >
              Edit
            </button>
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
          onSelect={setSelected}
          onDoubleClick={(r) => setEditing(r)}
          groupKey={(r) => r.categoryName ?? ""}
          emptyText={loading ? "Loading…" : "No products"}
        />
      </div>

      {editing && (
        <ProductZonePricesModal
          product={editing}
          zones={zones}
          existing={priceMap.get(editing.id) ?? new Map()}
          products={products}
          onClose={(saved) => {
            setEditing(null);
            if (saved) void refresh();
          }}
        />
      )}
    </div>
  );
}
