import { useMemo, useState } from "react";
import Modal from "../../components/Modal";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import {
  getProductZones,
  updateProductZonePrices,
  CostingZonePrice,
  ProductZonePriceItem,
} from "../../api/costing";
import { CostingZoneRow } from "../../api/costingZones";
import { Product } from "../../api/products";

interface Props {
  product: Product;
  zones: CostingZoneRow[];
  // Existing price rows for this product keyed by zone id (missing = no row yet).
  existing: Map<number, CostingZonePrice>;
  // Full product list for "Copy from another product".
  products: Product[];
  onClose: (saved: boolean) => void;
}

interface Entry {
  price: string;
  na: boolean;
  hasRow: boolean;
}

// Edit a product's price in every costing zone at once (Costing matrix Edit).
// Zones left blank that never had a price row stay without one.
export default function ProductZonePricesModal({ product, zones, existing, products, onClose }: Props) {
  const [entries, setEntries] = useState<Record<number, Entry>>(() => {
    const init: Record<number, Entry> = {};
    for (const z of zones) {
      const row = existing.get(z.id);
      init[z.id] = row
        ? { price: row.price.toFixed(2), na: row.na, hasRow: true }
        : { price: "", na: false, hasRow: false };
    }
    return init;
  });
  const [busy, setBusy] = useState(false);
  const [picking, setPicking] = useState(false);
  const [copiedFrom, setCopiedFrom] = useState<string | null>(null);

  function set(zoneId: number, patch: Partial<Entry>) {
    setEntries((e) => ({ ...e, [zoneId]: { ...e[zoneId], ...patch } }));
  }

  // Copy the picked product's prices into the form (zones the source product
  // has no price row for are left as they are). Nothing is saved until Save.
  async function copyFrom(source: Product) {
    setPicking(false);
    try {
      const sourcePrices = await getProductZones(source.id);
      setEntries((e) => {
        const next = { ...e };
        for (const p of sourcePrices) {
          const cur = next[p.costingZoneId];
          if (!cur) continue;
          next[p.costingZoneId] = { ...cur, price: p.price.toFixed(2), na: p.na };
        }
        return next;
      });
      setCopiedFrom(source.description ?? "");
    } catch (err) {
      void messageDialog("Failed to load prices to copy.\n" + (err as Error).message);
    }
  }

  async function onSave() {
    const items: ProductZonePriceItem[] = [];
    for (const z of zones) {
      const entry = entries[z.id];
      const blank = entry.price.trim() === "";
      if (blank && !entry.na && !entry.hasRow) continue; // never had a row — leave it that way
      if (blank && !entry.na) {
        void messageDialog(`Enter a price for "${z.description}" (or tick N/A).`);
        return;
      }
      const parsed = blank ? 0 : Number(entry.price);
      if (Number.isNaN(parsed)) {
        void messageDialog(`Price for "${z.description}" must be a number.`);
        return;
      }
      items.push({ costingZoneId: z.id, price: parsed, na: entry.na });
    }
    if (items.length === 0) {
      onClose(false);
      return;
    }
    setBusy(true);
    try {
      await updateProductZonePrices(product.id, items);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  // Two stacked columns so the dialog stays short with many zones.
  const half = Math.ceil(zones.length / 2);
  const zoneCols = [zones.slice(0, half), zones.slice(half)];

  return (
    <Modal
      title={`Edit Zone Prices — ${product.description ?? ""}`}
      width={720}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 10,
          marginBottom: 14,
          paddingBottom: 10,
          borderBottom: "1px solid var(--border)",
        }}
      >
        <button className="toolbar-btn" onClick={() => setPicking(true)}>
          Copy from another product
        </button>
        {copiedFrom && (
          <span className="toolbar-note">Copied from "{copiedFrom}" — review and Save</span>
        )}
      </div>

      <div className="form-inline zone-prices-form">
        <div className="form-cols">
          {zoneCols.map((col, colIdx) => (
            <div
              className="form-col"
              key={colIdx}
              style={colIdx === 1 ? { borderLeft: "1px solid var(--border)", paddingLeft: 20 } : undefined}
            >
              {col.map((z, i) => {
                const entry = entries[z.id];
                return (
                  <div className="form-field form-field-inline" key={z.id}>
                    <label
                      className="form-label"
                      title={`${z.description ?? ""}${z.secondaryDescription ? ` ${z.secondaryDescription}` : ""}`}
                    >
                      {z.description}
                      {z.secondaryDescription ? ` ${z.secondaryDescription}` : ""}
                    </label>
                    <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                      <input
                        className="form-input no-spin"
                        type="number"
                        step="0.01"
                        style={{ width: 70, flex: "none" }}
                        value={entry.price}
                        autoFocus={colIdx === 0 && i === 0}
                        disabled={entry.na}
                        onChange={(e) => set(z.id, { price: e.target.value })}
                      />
                      <label className="stock-radio" style={{ margin: 0 }}>
                        <input
                          type="checkbox"
                          checked={entry.na}
                          onChange={(e) => set(z.id, { na: e.target.checked })}
                        />
                        N/A
                      </label>
                    </div>
                  </div>
                );
              })}
            </div>
          ))}
        </div>
      </div>

      {picking && (
        <ProductPickerModal
          products={products.filter((p) => p.id !== product.id)}
          onPick={(p) => void copyFrom(p)}
          onClose={() => setPicking(false)}
        />
      )}
    </Modal>
  );
}

// Pick a product to copy zone prices from (double-click or select + Copy).
function ProductPickerModal({
  products,
  onPick,
  onClose,
}: {
  products: Product[];
  onPick: (product: Product) => void;
  onClose: () => void;
}) {
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<Product | null>(null);

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return products;
    return products.filter((r) =>
      [r.description, r.gtin, r.categoryName].some((v) => v?.toLowerCase().includes(term)),
    );
  }, [products, term]);

  const columns: Column<Product>[] = [
    { key: "description", header: "Description", render: (r) => <Highlight text={r.description} term={term} /> },
    { key: "categoryName", header: "Category", fit: true, render: (r) => <Highlight text={r.categoryName} term={term} /> },
  ];

  return (
    <Modal
      title="Copy Prices From…"
      width={560}
      onClose={onClose}
      footer={
        <>
          <button className="toolbar-btn" onClick={onClose}>
            Cancel
          </button>
          <button
            className="btn-blue"
            disabled={!selected}
            onClick={() => selected && onPick(selected)}
          >
            Copy
          </button>
        </>
      }
    >
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
        rowKey={(r) => r.id}
        selectedKey={selected?.id ?? null}
        onSelect={setSelected}
        onDoubleClick={onPick}
        maxHeight={380}
        emptyText="No products"
      />
    </Modal>
  );
}
