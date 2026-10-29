import { ReactNode, useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import { deleteProduct, getProducts, Product } from "../../api/products";
import ProductEditModal from "./ProductEditModal";

// "Point Of Sale" — mirrors the WPF PosProductListView: a plain Description list
// with Add/Edit/Delete (the editor carries only Description/Code/GTIN).
export default function PosProductsPanel({
  productTypeId,
  searchLabel,
  typePicker,
}: {
  productTypeId: number;
  searchLabel: string;
  typePicker?: ReactNode;
}) {
  const [rows, setRows] = useState<Product[]>([]);
  const [selected, setSelected] = useState<Product | null>(null);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(false);
  const [editing, setEditing] = useState<Product | null | "new">(null);

  async function refresh() {
    setLoading(true);
    try {
      const data = await getProducts(productTypeId);
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
  }, [productTypeId]);

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
  const filtered = useMemo(() => {
    if (!term) return rows;
    return rows.filter((r) => r.description?.toLowerCase().includes(term));
  }, [rows, term]);

  const columns: Column<Product>[] = [
    { key: "description", header: "Description", render: (r) => <Highlight text={r.description} term={term} /> },
  ];

  return (
    <div className="po-main">
      <div className="toolbar">
        {typePicker}
        <input
          className="form-input toolbar-search"
          placeholder={`Search ${searchLabel}…`}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        {loading && <span className="toolbar-note">Loading…</span>}
        <div className="toolbar-right">
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
        onDoubleClick={(r) => setEditing(r)}
        emptyText={loading ? "Loading…" : "No products"}
      />

      {editing !== null && (
        <ProductEditModal
          existing={editing === "new" ? null : editing}
          productTypeId={productTypeId}
          onClose={(saved) => {
            setEditing(null);
            if (saved) void refresh();
          }}
        />
      )}
    </div>
  );
}
