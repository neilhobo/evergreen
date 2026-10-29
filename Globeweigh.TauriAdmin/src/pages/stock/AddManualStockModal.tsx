import { useEffect, useMemo, useState } from "react";
import Modal from "../../components/Modal";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import { getProducts, getProductTypes, Product, ProductTypeRef } from "../../api/products";
import { StockLocation, getStockLocations, getStockSummary, manualAddStock } from "../../api/stock";

interface Props {
  onClose: (saved: boolean) => void;
}

// Add Manual Stock — a stock correction for a miscounted take: pick a product, a
// location and a pallet quantity. Saves immediately (placeholder pallets + a Manual
// entry in the stock history).
export default function AddManualStockModal({ onClose }: Props) {
  const [productTypes, setProductTypes] = useState<ProductTypeRef[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [locations, setLocations] = useState<StockLocation[]>([]);
  // Current in-stock pallet count per product (the "All" stock summary).
  const [stockByProduct, setStockByProduct] = useState<Record<number, number>>({});
  const [typeId, setTypeId] = useState(0); // 0 = all
  const [search, setSearch] = useState("");
  const [sizeSearch, setSizeSearch] = useState("");
  const [selected, setSelected] = useState<Product | null>(null);
  const [locationId, setLocationId] = useState(0);
  const [palletCount, setPalletCount] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [types, prods, locs, stock] = await Promise.all([
          getProductTypes(),
          getProducts(),
          getStockLocations(),
          getStockSummary(),
        ]);
        if (cancelled) return;
        setProductTypes(types);
        setProducts(prods.filter((p) => p.soldBy !== "CubicMeter")); // cubic meter products carry no stock
        setLocations(locs);
        const map: Record<number, number> = {};
        stock.forEach((s) => {
          if (s.productId != null) map[s.productId] = s.palletCount;
        });
        setStockByProduct(map);
        // Default the type filter to Finished Product and the location to Main Yard.
        const finished = types.find((t) => /finished/i.test(t.typeDescription ?? ""));
        if (finished) setTypeId(finished.id);
        const mainYard = locs.find((l) => l.name === "Main Yard");
        if (mainYard) setLocationId(mainYard.id);
      } catch (err) {
        if (!cancelled) void messageDialog("Failed to load.\n" + (err as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  async function save() {
    if (!selected) {
      void messageDialog("Select a product.");
      return;
    }
    if (locationId <= 0) {
      void messageDialog("Select a location.");
      return;
    }
    if (palletCount == null || palletCount === 0) {
      void messageDialog("Enter a pallet quantity (negative removes stock).");
      return;
    }
    // Downward corrections are destructive-ish — confirm before removing stock.
    if (palletCount < 0) {
      const locName = locations.find((l) => l.id === locationId)?.name ?? "?";
      const ok = await confirmDialog(
        `Remove ${(-palletCount).toLocaleString()} pallets of "${selected.description}" from ${locName}?`,
      );
      if (!ok) return;
    }
    setBusy(true);
    try {
      await manualAddStock(selected.id, locationId, palletCount);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to add stock.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  const term = search.trim().toLowerCase();
  const sizeTerm = sizeSearch.trim();
  const filtered = useMemo(() => {
    return products
      .filter(
        (p) =>
          (typeId === 0 || p.productTypeId === typeId) &&
          (!term ||
            [p.description, p.skuCode, p.gtin].some((v) => v?.toLowerCase().includes(term))) &&
          (!sizeTerm || (p.size != null && String(p.size).startsWith(sizeTerm))),
      )
      .sort((a, b) => (a.description ?? "").localeCompare(b.description ?? ""));
  }, [products, typeId, term, sizeTerm]);

  const columns: Column<Product>[] = [
    {
      key: "gtin",
      header: "Evergreen Code",
      width: 120,
      render: (p) => <Highlight text={p.gtin} term={term} />,
    },
    {
      key: "description",
      header: "Product",
      render: (p) => <Highlight text={p.description} term={term} />,
    },
    { key: "size", header: "Size", width: 55, align: "right" },
    { key: "upp", header: "UPP", width: 55, align: "right" },
    {
      key: "currentStock",
      header: "Current Stock",
      width: 95,
      align: "right",
      render: (p) => <strong>{(stockByProduct[p.id] ?? 0).toLocaleString()}</strong>,
    },
  ];

  return (
    <Modal
      title="Add Manual Stock"
      width={1040}
      height={620}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button
            className="btn-blue"
            disabled={busy || !selected || locationId <= 0 || palletCount == null}
            onClick={() => void save()}
          >
            {busy ? "Adding…" : "Add Stock"}
          </button>
        </>
      }
    >
      {loading ? (
        <div className="data-table-empty">Loading…</div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", height: "100%", minHeight: 0, minWidth: 0, overflowX: "hidden" }}>
          {/* Product types span the full dialog width, above both panes */}
          <div style={{ display: "flex", flexWrap: "wrap", gap: 4, marginBottom: 8 }}>
            {productTypes.map((t) => (
              <div
                key={t.id}
                className={`stock-type-item${typeId === t.id ? " active" : ""}`}
                onClick={() => setTypeId(t.id)}
              >
                {t.typeDescription}
              </div>
            ))}
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 280px", gap: 10, flex: 1, minHeight: 0, minWidth: 0 }}>
          {/* Left: products grid */}
          <div className="dsp-prod-grid" style={{ display: "flex", flexDirection: "column", minWidth: 0, minHeight: 0 }}>
            <div className="toolbar">
              <input
                className="form-input toolbar-search"
                placeholder="Search products…"
                value={search}
                autoFocus
                onChange={(e) => setSearch(e.target.value)}
              />
              <label className="toolbar-label">Size:</label>
              <input
                className="form-input toolbar-search"
                title="Filter by size"
                style={{ width: 70, flex: "none" }}
                value={sizeSearch}
                onChange={(e) => setSizeSearch(e.target.value)}
              />
            </div>
            <DataTable
              columns={columns}
              rows={filtered}
              rowKey={(p) => p.id}
              selectedKey={selected?.id ?? null}
              onSelect={(p) => setSelected(p)}
              emptyText="No products"
            />
          </div>

          {/* Right: location + quantity card for the selected product (matches the
              Add Order Item card styling) */}
          <div
            style={{
              minWidth: 0,
              alignSelf: "start",
              background: "#fff",
              border: "1px solid var(--border)",
              borderRadius: 6,
              padding: 12,
              display: "flex",
              flexDirection: "column",
              gap: 10,
            }}
          >
            <h3 className="po-side-title" style={{ height: "auto", margin: 0 }}>
              Manual Stock
            </h3>
            <div style={{ borderBottom: "1px solid var(--border)" }} />
            {!selected ? (
              <p style={{ color: "var(--text-secondary)", fontSize: 12, margin: 0 }}>Select a product.</p>
            ) : (
              <>
                <div style={{ fontSize: 12, fontWeight: 600, color: "var(--text-heading)" }}>
                  {selected.description}
                  {selected.size != null ? ` — ${selected.size}L` : ""}
                </div>
                <div className="form-field">
                  <label className="form-label">Location *</label>
                  <select
                    className="form-input"
                    style={{ width: "100%" }}
                    value={locationId}
                    onChange={(e) => setLocationId(Number(e.target.value))}
                  >
                    <option value={0}>— Select —</option>
                    {locations.map((l) => (
                      <option key={l.id} value={l.id}>
                        {l.name}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="form-field">
                  <label className="form-label">Pallet Quantity (+ / −)</label>
                  <input
                    className="form-input"
                    type="number"
                    style={{ width: "100%", textAlign: "left" }}
                    value={palletCount ?? ""}
                    onChange={(e) => setPalletCount(e.target.value === "" ? null : Number(e.target.value))}
                  />
                </div>
                <div style={{ borderBottom: "1px solid var(--border)" }} />
                <div style={{ display: "flex", alignItems: "baseline", justifyContent: "space-between" }}>
                  <span className="form-label" style={{ marginBottom: 0 }}>
                    Resulting Quantity
                  </span>
                  <span style={{ fontSize: 15, fontWeight: 600, color: "var(--text-heading)" }}>
                    {((stockByProduct[selected.id] ?? 0) + (palletCount ?? 0)).toLocaleString()}
                  </span>
                </div>
              </>
            )}
          </div>
          </div>
        </div>
      )}
    </Modal>
  );
}
