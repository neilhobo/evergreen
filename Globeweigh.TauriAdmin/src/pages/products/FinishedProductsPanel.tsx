import { ReactNode, useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  deleteProduct,
  getProducts,
  Product,
  FINISHED_PRODUCT_TYPE_ID,
  soldByLabel,
} from "../../api/products";
import ProductEditModal from "./ProductEditModal";
import { fmtQty2 } from "../../format";

// "Finished Products" — mirrors the WPF ProductListView: Description, SKU Code,
// LTRS, UPP, Barcode, Category, Commodity Code and POS columns.
// onView opens the Product detail panel (ProductsPage owns it, since it covers the whole card);
// reloadToken bumps when that panel changed a product, so the list refetches.
export default function FinishedProductsPanel({
  typePicker,
  onView,
  reloadToken = 0,
}: {
  typePicker?: ReactNode;
  onView?: (product: Product) => void;
  reloadToken?: number;
}) {
  const [rows, setRows] = useState<Product[]>([]);
  const [selected, setSelected] = useState<Product | null>(null);
  const [search, setSearch] = useState("");
  const [bulkOnly, setBulkOnly] = useState(false);
  const [noWeightOnly, setNoWeightOnly] = useState(false);
  const [loading, setLoading] = useState(false);
  const [editing, setEditing] = useState<Product | null | "new">(null);

  async function refresh() {
    setLoading(true);
    try {
      const data = await getProducts(FINISHED_PRODUCT_TYPE_ID);
      setRows(data);
      setSelected((cur) => (cur ? data.find((r) => r.id === cur.id) ?? null : null));
    } catch (err) {
      void messageDialog("Failed to load products.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [reloadToken]);

  async function onDelete() {
    if (!selected) return;
    if (!(await confirmDialog(`Delete product "${selected.description}"?`))) return;
    try {
      await deleteProduct(selected.id);
      await refresh();
    } catch (err) {
      void messageDialog("Failed to delete.\n" + (err as Error).message);
    }
  }

  const term = search.trim().toLowerCase();
  const searched = useMemo(() => {
    if (!term) return rows;
    return rows.filter((r) =>
      [r.skuCode, r.gtin, r.description, r.categoryName].some((v) =>
        v?.toLowerCase().includes(term),
      ),
    );
  }, [rows, term]);
  // No Average Weight: a no-scan pick of such a product goes on at 0 kg unless it has weighed
  // pallets, so these are the rows someone needs to fill in.
  // Cubic meter products carry no Average Weight by design, so they are never flagged.
  const noWeight = (r: Product) => r.soldBy !== "CubicMeter" && !(r.averageWeight && r.averageWeight > 0);
  const bulkCount = useMemo(() => searched.filter((r) => r.isBulkBag).length, [searched]);
  const noWeightCount = useMemo(() => searched.filter(noWeight).length, [searched]);
  const filtered = searched.filter((r) => (!bulkOnly || r.isBulkBag) && (!noWeightOnly || noWeight(r)));

  const columns: Column<Product>[] = [
    { key: "description", header: "Description", render: (r) => <Highlight text={r.description} term={term} /> },
    { key: "skuCode", header: "SKU Code", width: 100, render: (r) => <Highlight text={r.skuCode} term={term} /> },
    { key: "size", header: "LTRS", width: 60, align: "right", render: (r) => r.size },
    { key: "upp", header: "UPP", width: 60, align: "right", render: (r) => r.upp },
    { key: "gtin", header: "Barcode", width: 110, render: (r) => <Highlight text={r.gtin} term={term} /> },
    { key: "categoryName", header: "Category", width: 200, render: (r) => <Highlight text={r.categoryName} term={term} /> },
    { key: "commodityCode", header: "Commodity Code", width: 120, render: (r) => r.commodityCode },
    { key: "posDescription", header: "POS", width: 150, render: (r) => r.posDescription },
    { key: "soldBy", header: "Sold By", width: 100, render: (r) => soldByLabel(r.soldBy) },
    {
      key: "isBulkBag",
      header: "Bulk Bag",
      width: 70,
      align: "center",
      render: (r) => (r.isBulkBag ? <span className="bulk-tick">✓</span> : ""),
    },
    {
      // g per empty bag (Pallet / Bag Setup in the product dialog); blank = not entered yet.
      key: "bagWeightGrams",
      header: "Bag Weight (g)",
      width: 110,
      align: "right",
      render: (r) => fmtQty2(r.bagWeightGrams),
    },
    {
      // kg per pallet — what a no-scan pick of a product with no weighed pallets is booked
      // at. Seeded from the pallet history; blank = none yet (such a pick goes on at 0 kg).
      key: "averageWeight",
      header: "Average Weight",
      width: 110,
      align: "right",
      className: "col-tint-orange",
      render: (r) => fmtQty2(r.averageWeight),
    },
    ...(onView
      ? [
          {
            // Opens the Product detail panel: zone prices and customer pricing.
            key: "costing",
            header: "",
            fit: true,
            noSort: true,
            render: (r: Product) => (
              <button
                className="link-btn"
                onClick={(e) => {
                  e.stopPropagation();
                  onView(r);
                }}
              >
                Costing
              </button>
            ),
          },
        ]
      : []),
  ];

  return (
    <div className="po-main">
      <div className="toolbar">
        {typePicker}
        <input
          className="form-input toolbar-search"
          placeholder="Search finished products…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <label className="stock-radio" style={{ marginLeft: 6 }}>
          <input type="checkbox" checked={bulkOnly} onChange={(e) => setBulkOnly(e.target.checked)} />
          Bulk Bags Only ({bulkCount})
        </label>
        <label className={`filter-chip${noWeightOnly ? " checked" : ""}`} style={{ marginLeft: 6 }}>
          <input type="checkbox" checked={noWeightOnly} onChange={(e) => setNoWeightOnly(e.target.checked)} />
          No Average Weight ({noWeightCount})
        </label>
        {loading && <span className="toolbar-note">Loading…</span>}
        <div className="toolbar-right">
          {onView && (
            <button className="toolbar-btn" disabled={!selected} onClick={() => selected && onView(selected)}>
              View
            </button>
          )}
          <button className="btn-primary" onClick={() => setEditing("new")}>
            Add
          </button>
          <button className="toolbar-btn" disabled={!selected} onClick={() => setEditing(selected)}>
            Edit
          </button>
          <button className="toolbar-btn" disabled={!selected} onClick={onDelete}>
            Delete
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
        // Double-click opens the detail panel, as it does for an order; Edit stays on the toolbar.
        onDoubleClick={(r) => (onView ? onView(r) : setEditing(r))}
        // Red outranks the bulk-bag green: a missing weight is the thing to act on.
        rowClass={(r) => (noWeight(r) ? "row-no-weight" : r.isBulkBag ? "row-bulk" : undefined)}
        emptyText={loading ? "Loading…" : "No products"}
      />

      {editing !== null && (
        <ProductEditModal
          existing={editing === "new" ? null : editing}
          productTypeId={FINISHED_PRODUCT_TYPE_ID}
          onClose={(saved) => {
            setEditing(null);
            if (saved) void refresh();
          }}
        />
      )}
    </div>
  );
}
