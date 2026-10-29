import { useEffect, useMemo, useState } from "react";
import Modal from "../../components/Modal";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import { getProducts, getProductTypes, Product, ProductTypeRef } from "../../api/products";

export interface StockTakeItemDraft {
  productId: number;
  palletCount: number;
}

interface Props {
  alreadySelectedProductIds: number[];
  existing: StockTakeItemDraft | null; // null = add
  onAdd: (item: StockTakeItemDraft) => void; // draft only — nothing persists until the take is saved
  onClose: () => void;
}

// Add/edit one counted product on a stock take — a slimmed-down AddOrderItemModal:
// Product Type list (left), searchable products grid (middle), pallet count (right).
export default function AddStockTakeItemModal({
  alreadySelectedProductIds,
  existing,
  onAdd,
  onClose,
}: Props) {
  const [productTypes, setProductTypes] = useState<ProductTypeRef[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [typeId, setTypeId] = useState(0); // 0 = all
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<Product | null>(null);
  const [palletCount, setPalletCount] = useState<number | null>(existing?.palletCount ?? null);
  const [loading, setLoading] = useState(true);

  const added = useMemo(() => new Set(alreadySelectedProductIds), [alreadySelectedProductIds]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [types, prods] = await Promise.all([getProductTypes(), getProducts()]);
        if (cancelled) return;
        setProductTypes(types);
        setProducts(prods.filter((p) => p.soldBy !== "CubicMeter")); // cubic meter products carry no stock
        if (existing) {
          const p = prods.find((x) => x.id === existing.productId) ?? null;
          setSelected(p);
          if (p) setTypeId(p.productTypeId);
        } else {
          // Default the type filter to Finished Product.
          const finished = types.find((t) => /finished/i.test(t.typeDescription ?? ""));
          if (finished) setTypeId(finished.id);
        }
      } catch (err) {
        if (!cancelled) void messageDialog("Failed to load products.\n" + (err as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [existing]);

  function selectProduct(p: Product) {
    if (added.has(p.id) && existing?.productId !== p.id) return; // already on the take
    setSelected(p);
  }

  function save() {
    if (!selected) {
      void messageDialog("Select a product.");
      return;
    }
    if (palletCount == null || palletCount < 0) {
      void messageDialog("Enter a pallet count.");
      return;
    }
    onAdd({ productId: selected.id, palletCount });
    onClose();
  }

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    return products.filter(
      (p) =>
        (typeId === 0 || p.productTypeId === typeId) &&
        (!term ||
          [p.description, p.skuCode, p.gtin].some((v) => v?.toLowerCase().includes(term))),
    );
  }, [products, typeId, term]);

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
      render: (p) => (
        <span style={added.has(p.id) ? { color: "#16a34a" } : undefined}>
          <Highlight text={p.description} term={term} />
          {added.has(p.id) ? " ✓" : ""}
        </span>
      ),
    },
    { key: "size", header: "Size", width: 55, align: "right" },
    { key: "upp", header: "UPP", width: 55, align: "right" },
  ];

  return (
    <Modal
      title={`${existing ? "Edit" : "Add"} Stock Take Item`}
      width={940}
      height={620}
      onClose={onClose}
      footer={
        <>
          <button className="toolbar-btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-blue" disabled={!selected || palletCount == null} onClick={save}>
            Save
          </button>
        </>
      }
    >
      {loading ? (
        <div className="data-table-empty">Loading…</div>
      ) : (
        <div style={{ display: "grid", gridTemplateColumns: "130px 1fr 240px", gap: 10, height: "100%", minHeight: 0, minWidth: 0, overflowX: "hidden" }}>
          {/* Left: product types */}
          <div className="stock-types" style={{ overflow: "auto" }}>
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

          {/* Middle: products grid */}
          <div className="dsp-prod-grid" style={{ display: "flex", flexDirection: "column", minWidth: 0, minHeight: 0 }}>
            <div className="toolbar">
              <input
                className="form-input toolbar-search"
                placeholder="Search products…"
                value={search}
                autoFocus
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
            <DataTable
              columns={columns}
              rows={filtered}
              rowKey={(p) => p.id}
              selectedKey={selected?.id ?? null}
              onSelect={selectProduct}
              onDoubleClick={selectProduct}
              emptyText="No products"
            />
          </div>

          {/* Right: pallet count */}
          <div className="po-side" style={{ width: "100%", minWidth: 0, overflow: "hidden" }}>
            {!selected ? (
              <p style={{ color: "var(--text-secondary)", fontSize: 13 }}>Select a product.</p>
            ) : added.has(selected.id) && existing?.productId !== selected.id ? (
              <p style={{ color: "#16a34a", fontSize: 13 }}>Already on this stock take.</p>
            ) : (
              <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
                <div className="form-field form-field-inline">
                  <label className="form-label">Pallet Count</label>
                  <input
                    className="form-input"
                    type="number"
                    min={0}
                    style={{ width: 100, flex: "none", textAlign: "left" }}
                    value={palletCount ?? ""}
                    onChange={(e) => setPalletCount(e.target.value === "" ? null : Number(e.target.value))}
                  />
                </div>
              </div>
            )}
          </div>
        </div>
      )}
    </Modal>
  );
}
