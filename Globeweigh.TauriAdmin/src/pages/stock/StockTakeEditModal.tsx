import { useEffect, useMemo, useState } from "react";
import Modal from "../../components/Modal";
import DataTable, { Column } from "../../components/DataTable";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import { fmtDateTime } from "../../format";
import { getProducts, Product } from "../../api/products";
import {
  StockLocation,
  StockTake,
  createStockTake,
  getStockLocations,
  getStockTake,
  updateStockTake,
} from "../../api/stock";
import AddStockTakeItemModal, { StockTakeItemDraft } from "./AddStockTakeItemModal";

interface Props {
  existing: StockTake | null; // null = new
  onClose: (saved: boolean) => void;
}

// Add/edit a stock take, dispatch-order style: items are a local draft and nothing
// touches the database until Save — Cancel discards everything. Saving stores counts
// only; "Make Current Stock" is a later phase.
export default function StockTakeEditModal({ existing, onClose }: Props) {
  const isNew = existing === null;

  const [products, setProducts] = useState<Product[]>([]);
  const [locations, setLocations] = useState<StockLocation[]>([]);
  const [locationId, setLocationId] = useState(existing?.locationId ?? 0);
  const [items, setItems] = useState<StockTakeItemDraft[]>([]);
  const [itemModal, setItemModal] = useState<{ index: number | null } | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  // Load the lookups (and, when editing, the take's saved items) on mount.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [prods, locs, detail] = await Promise.all([
          getProducts(),
          getStockLocations(),
          existing ? getStockTake(existing.id) : Promise.resolve(null),
        ]);
        if (cancelled) return;
        setProducts(prods);
        setLocations(locs);
        if (detail) {
          setLocationId(detail.locationId ?? 0);
          setItems(detail.items.map((i) => ({ productId: i.productId, palletCount: i.palletCount })));
        }
      } catch (err) {
        if (!cancelled) void messageDialog("Failed to load.\n" + (err as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [existing]);

  function applyItem(draft: StockTakeItemDraft) {
    setItems((rows) => {
      if (itemModal?.index != null) {
        return rows.map((r, i) => (i === itemModal.index ? draft : r));
      }
      return [...rows, draft];
    });
  }

  async function removeItem(idx: number) {
    if (!(await confirmDialog("Remove this item?"))) return;
    setItems((rows) => rows.filter((_, i) => i !== idx));
  }

  function product(id: number) {
    return products.find((p) => p.id === id) ?? null;
  }

  async function onSave() {
    if (locationId <= 0) {
      void messageDialog("Select a location.");
      return;
    }
    setBusy(true);
    try {
      if (isNew) await createStockTake(locationId, items);
      else await updateStockTake(existing!.id, locationId, items);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  const totals = useMemo(
    () => ({
      products: items.length,
      pallets: items.reduce((s, i) => s + i.palletCount, 0),
    }),
    [items],
  );

  const columns: Column<StockTakeItemDraft>[] = [
    { key: "gtin", header: "Evergreen Code", width: 140, render: (r) => product(r.productId)?.gtin ?? "" },
    { key: "product", header: "Product", render: (r) => product(r.productId)?.description ?? `#${r.productId}` },
    { key: "size", header: "Size", width: 70, align: "right", render: (r) => product(r.productId)?.size ?? "" },
    { key: "upp", header: "UPP", width: 70, align: "right", render: (r) => product(r.productId)?.upp ?? "" },
    {
      key: "palletCount",
      header: "Pallet Count",
      width: 110,
      align: "right",
      render: (r) => r.palletCount.toLocaleString(),
    },
    {
      key: "actions",
      header: " ",
      width: 80,
      render: (r) => {
        const idx = items.indexOf(r);
        return (
          <button
            className="link-btn"
            onClick={(e) => {
              e.stopPropagation();
              void removeItem(idx);
            }}
          >
            Remove
          </button>
        );
      },
    },
  ];

  return (
    <>
      <Modal
        title={isNew ? "New Stock Take" : `Edit Stock Take #${existing!.id}`}
        width={860}
        height={620}
        onClose={() => onClose(false)}
        footer={
          <>
            <span className="prod-summary-total" style={{ marginRight: "auto" }}>
              Products: <strong>{totals.products.toLocaleString()}</strong>&nbsp;&nbsp; Pallets:{" "}
              <strong>{totals.pallets.toLocaleString()}</strong>
            </span>
            <button className="toolbar-btn" onClick={() => onClose(false)}>
              Cancel
            </button>
            <button
              className="btn-blue"
              disabled={busy || loading || locationId <= 0}
              onClick={() => void onSave()}
            >
              {busy ? "Saving…" : "Save"}
            </button>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: 8, height: "100%", minHeight: 0 }}>
          <div className="form-field form-field-inline" style={{ flex: "none" }}>
            <label className="form-label">Location *</label>
            <select
              className="form-input"
              style={{ width: 220, flex: "none" }}
              value={locationId}
              disabled={loading}
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
          <div className="toolbar">
            <button className="btn-blue" disabled={loading} onClick={() => setItemModal({ index: null })}>
              Add Item
            </button>
            <div className="toolbar-right">
              <span className="toolbar-note">
                {existing
                  ? `Created ${fmtDateTime(existing.dateCreated)}${existing.createdBy ? ` by ${existing.createdBy}` : ""}`
                  : ""}
              </span>
            </div>
          </div>

          <DataTable
            columns={columns}
            rows={items}
            rowKey={(r) => items.indexOf(r)}
            onDoubleClick={(r) => setItemModal({ index: items.indexOf(r) })}
            emptyText={loading ? "Loading…" : "No items — click Add Item to count a product"}
          />
        </div>
      </Modal>

      {itemModal && (
        <AddStockTakeItemModal
          alreadySelectedProductIds={items
            .filter((_, i) => i !== itemModal.index)
            .map((i) => i.productId)}
          existing={itemModal.index != null ? items[itemModal.index] : null}
          onAdd={applyItem}
          onClose={() => setItemModal(null)}
        />
      )}
    </>
  );
}
