import { useEffect, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import { fmtDateTime } from "../../format";
import {
  StockTake,
  StockTakeItem,
  deleteStockTake,
  getStockTake,
  getStockTakes,
} from "../../api/stock";
import StockTakeEditModal from "./StockTakeEditModal";
import MakeCurrentStockModal from "./MakeCurrentStockModal";
import NewTransferModal from "./NewTransferModal";
import AddManualStockModal from "./AddManualStockModal";

// Stock Takes — one row per take (counts entered per product, no pallet identity),
// master/detail like Completed Orders: selecting a take shows its items on the right.
// New/Edit open a draft editor; nothing is stored until it's saved there.
export default function StockTakesPanel() {
  const [rows, setRows] = useState<StockTake[]>([]);
  const [selected, setSelected] = useState<StockTake | null>(null);
  const [items, setItems] = useState<StockTakeItem[]>([]);
  const [loading, setLoading] = useState(false);
  // null = closed; { existing: null } = new take; { existing: row } = edit.
  const [editModal, setEditModal] = useState<{ existing: StockTake | null } | null>(null);
  const [transferOpen, setTransferOpen] = useState(false);
  const [manualOpen, setManualOpen] = useState(false);

  async function refresh() {
    setLoading(true);
    try {
      setRows(await getStockTakes());
      setSelected(null);
      setItems([]);
    } catch (err) {
      setRows([]);
      void messageDialog("Failed to load stock takes.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  async function selectTake(take: StockTake) {
    setSelected(take);
    try {
      setItems((await getStockTake(take.id)).items);
    } catch (err) {
      setItems([]);
      void messageDialog("Failed to load stock take items.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  // The take being applied via the Make Current Stock dialog (summary → confirm → spinner).
  const [makeCurrentTake, setMakeCurrentTake] = useState<StockTake | null>(null);

  function openMakeCurrent() {
    if (!selected) return;
    if (!selected.locationId) {
      void messageDialog("Set a location on this stock take first (Edit).");
      return;
    }
    setMakeCurrentTake(selected);
  }

  async function removeSelected() {
    if (!selected) return;
    if (!(await confirmDialog(`Delete Stock Take #${selected.id}?`))) return;
    try {
      await deleteStockTake(selected.id);
      await refresh();
    } catch (err) {
      void messageDialog("Failed to delete stock take.\n" + (err as Error).message);
    }
  }

  const columns: Column<StockTake>[] = [
  // Best-fit fixed columns; the flexible Created By column absorbs the slack and
  // truncates with an ellipsis, so the grid never scrolls horizontally.
    { key: "id", header: "ID", fit: true },
    {
      key: "type",
      header: "Type",
      fit: true,
      render: (r) =>
        r.isTransfer ? (
          <span className="row-badge row-badge-transfer">Transfer</span>
        ) : (
          <span className="row-badge row-badge-take">Stock Take</span>
        ),
    },
    { key: "dateCreated", header: "Date", fit: true, render: (r) => fmtDateTime(r.dateCreated) },
    {
      key: "locationName",
      header: "Location",
      fit: true,
      // Transfers show the movement; takes just their location.
      render: (r) =>
        r.isTransfer ? `${r.fromLocationName ?? "?"} → ${r.locationName ?? "?"}` : r.locationName,
    },
    { key: "createdBy", header: "Created By" },
    { key: "productCount", header: "Products", fit: true, align: "right", render: (r) => r.productCount.toLocaleString() },
    { key: "palletCount", header: "Pallets", fit: true, align: "right", render: (r) => r.palletCount.toLocaleString() },
    {
      key: "status",
      header: "Status",
      fit: true,
      render: (r) =>
        r.isTransfer
          ? "Transferred"
          : r.dateMadeCurrentStock
            ? `Made Current ${fmtDateTime(r.dateMadeCurrentStock)}`
            : r.dateCompleted
              ? "Completed"
              : "Open",
    },
  ];

  // Best-fit columns hugging the left; the empty trailing column soaks up the
  // spare width so the data doesn't get pushed to the panel's right edge.
  const itemColumns: Column<StockTakeItem>[] = [
    { key: "description", header: "Product", fit: true, render: (r) => r.description ?? `#${r.productId}` },
    { key: "size", header: "Size", fit: true, align: "right", render: (r) => r.size },
    { key: "upp", header: "UPP", fit: true, align: "right", render: (r) => r.upp },
    { key: "palletCount", header: "Pallet Count", fit: true, align: "right", render: (r) => r.palletCount.toLocaleString() },
    { key: "_fill", header: "", render: () => null },
  ];

  return (
    <div className="po-open-layout production-page" style={{ flex: 1, minHeight: 0 }}>
      {/* Fixed-width list; the items panel (shown once a take is selected) takes the rest. */}
      <div className="po-main" style={{ flex: "none", width: 800 }}>
        <div className="toolbar">
          <button className="btn-blue" onClick={() => setEditModal({ existing: null })}>
            New Stock Take
          </button>
          <button className="btn-yellow" onClick={() => setTransferOpen(true)}>
            New Transfer
          </button>
          <button className="btn-blue" onClick={() => setManualOpen(true)}>
            Add Manual Stock
          </button>
          <button
            className="toolbar-btn"
            disabled={!selected || selected.isTransfer || selected.dateMadeCurrentStock != null}
            onClick={() => selected && setEditModal({ existing: selected })}
          >
            Edit
          </button>
          <button
            className="toolbar-btn"
            disabled={!selected || selected.dateMadeCurrentStock != null}
            onClick={() => void removeSelected()}
          >
            Delete
          </button>
          <button className="toolbar-btn" onClick={() => void refresh()} disabled={loading}>
            Refresh
          </button>
          {loading && <span className="toolbar-note">Loading…</span>}
          <div className="toolbar-right">
            <button
              className="btn-lavender"
              disabled={!selected || selected.isTransfer || selected.dateMadeCurrentStock != null}
              title={
                selected?.dateMadeCurrentStock != null
                  ? "Already made current stock — only open stock takes can be applied"
                  : undefined
              }
              onClick={openMakeCurrent}
            >
              Make Current Stock
            </button>
          </div>
        </div>

        <DataTable
          columns={columns}
          rows={rows}
          rowKey={(r) => r.id}
          selectedKey={selected?.id ?? null}
          onSelect={(r) => void selectTake(r)}
          onDoubleClick={(r) => setEditModal({ existing: r })}
          emptyText={loading ? "Loading…" : "No stock takes yet"}
        />
      </div>

      {selected && (
        <div className="po-side stock-take-items" style={{ width: "auto", flex: 1, minWidth: 0 }}>
          <h3 className="po-side-title">
            {`Stock Take #${selected.id} Items (${items.length.toLocaleString()} products, ${items
              .reduce((s, i) => s + i.palletCount, 0)
              .toLocaleString()} pallets)`}
          </h3>
          <DataTable
            columns={itemColumns}
            rows={items}
            rowKey={(r) => r.id}
            emptyText="No items"
          />
        </div>
      )}

      {editModal && (
        <StockTakeEditModal
          existing={editModal.existing}
          onClose={(saved) => {
            setEditModal(null);
            if (saved) void refresh();
          }}
        />
      )}

      {makeCurrentTake && (
        <MakeCurrentStockModal
          take={makeCurrentTake}
          onClose={(applied) => {
            setMakeCurrentTake(null);
            if (applied) void refresh();
          }}
        />
      )}

      {transferOpen && (
        <NewTransferModal
          onClose={(saved) => {
            setTransferOpen(false);
            if (saved) void refresh();
          }}
        />
      )}

      {manualOpen && (
        <AddManualStockModal
          onClose={(saved) => {
            setManualOpen(false);
            if (saved) void refresh();
          }}
        />
      )}
    </div>
  );
}
