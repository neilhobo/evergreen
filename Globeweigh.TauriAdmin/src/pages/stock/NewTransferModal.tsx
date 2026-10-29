import { useEffect, useMemo, useState } from "react";
import Modal from "../../components/Modal";
import DataTable, { Column } from "../../components/DataTable";
import { messageDialog } from "../../components/confirmDialog";
import {
  StockLocation,
  StockSummaryItem,
  createStockTransfer,
  getStockLocations,
  getStockSummary,
} from "../../api/stock";

interface Props {
  onClose: (saved: boolean) => void;
}

// One product movement in this transfer session — each line carries its own
// source and destination, so one session can move stock between many locations.
interface TransferLine {
  productId: number;
  description: string | null;
  size: number | null;
  fromId: number;
  fromName: string | null;
  toId: number;
  toName: string | null;
  qty: number;
}

// New Transfer — move counted stock between locations. Left: pick a source location
// and see its stock. The → arrow asks for a quantity AND the destination, and the
// movement appears in the right-hand list. Lines can come from several source
// locations; Complete Transfer saves them all (one transfer record per from→to pair).
export default function NewTransferModal({ onClose }: Props) {
  const [locations, setLocations] = useState<StockLocation[]>([]);
  const [fromId, setFromId] = useState(0);
  const [fromRows, setFromRows] = useState<StockSummaryItem[]>([]);
  const [pending, setPending] = useState<TransferLine[]>([]);
  const [selected, setSelected] = useState<StockSummaryItem | null>(null);
  // The product awaiting quantity + destination after the arrow was clicked.
  const [qtyFor, setQtyFor] = useState<StockSummaryItem | null>(null);
  const [qty, setQty] = useState<number | null>(null);
  const [toId, setToId] = useState(0);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    getStockLocations()
      .then(setLocations)
      .catch((err) => void messageDialog("Failed to load locations.\n" + (err as Error).message));
  }, []);

  useEffect(() => {
    if (fromId <= 0) {
      setFromRows([]);
      return;
    }
    getStockSummary(fromId)
      .then(setFromRows)
      .catch(() => setFromRows([]));
    setSelected(null);
  }, [fromId]);

  const locName = (id: number) => locations.find((l) => l.id === id)?.name ?? "?";

  // Outbound pending quantity for a product FROM the currently selected location.
  const pendingOut = (productId: number | null) =>
    pending
      .filter((p) => p.fromId === fromId && p.productId === productId)
      .reduce((s, p) => s + p.qty, 0);

  // Source rows with this session's pending movements already deducted.
  const sourceRows = useMemo(
    () =>
      fromRows
        .map((r) => ({ ...r, palletCount: r.palletCount - pendingOut(r.productId) }))
        .filter((r) => r.palletCount > 0),
    [fromRows, pending, fromId],
  );

  function openQty(row: StockSummaryItem) {
    if (row.productId == null) return;
    setQty(null);
    setToId(0);
    setQtyFor(row);
  }

  function confirmQty() {
    if (!qtyFor || qtyFor.productId == null) return;
    const max = qtyFor.palletCount; // already net of earlier pending lines
    if (toId <= 0) {
      void messageDialog("Select the destination location.");
      return;
    }
    if (toId === fromId) {
      void messageDialog("Destination must differ from the source location.");
      return;
    }
    if (qty == null || qty <= 0 || qty > max) {
      void messageDialog(`Enter a quantity between 1 and ${max.toLocaleString()}.`);
      return;
    }
    setPending((rows) => {
      const existing = rows.find(
        (r) => r.productId === qtyFor.productId && r.fromId === fromId && r.toId === toId,
      );
      if (existing) {
        return rows.map((r) => (r === existing ? { ...r, qty: r.qty + qty } : r));
      }
      return [
        ...rows,
        {
          productId: qtyFor.productId!,
          description: qtyFor.description,
          size: qtyFor.size,
          fromId,
          fromName: locName(fromId),
          toId,
          toName: locName(toId),
          qty,
        },
      ];
    });
    setQtyFor(null);
    setSelected(null);
  }

  function removeLine(line: TransferLine) {
    setPending((rows) => rows.filter((r) => r !== line));
  }

  // Save: one transfer record per from→to pair (the backend model), all sequentially.
  async function complete() {
    setBusy(true);
    const groups = new Map<string, { fromId: number; toId: number; lines: TransferLine[] }>();
    for (const line of pending) {
      const key = `${line.fromId}|${line.toId}`;
      if (!groups.has(key)) groups.set(key, { fromId: line.fromId, toId: line.toId, lines: [] });
      groups.get(key)!.lines.push(line);
    }
    try {
      for (const g of groups.values()) {
        await createStockTransfer(
          g.fromId,
          g.toId,
          g.lines.map((l) => ({ productId: l.productId, palletCount: l.qty })),
        );
        // Drop saved lines so a later failure leaves only the unsaved ones behind.
        setPending((rows) => rows.filter((r) => !g.lines.includes(r)));
      }
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to complete transfer.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  const stockColumns: Column<StockSummaryItem>[] = [
    { key: "description", header: "Product", render: (r) => r.description ?? r.code13 },
    { key: "size", header: "Size", fit: true, align: "right", render: (r) => r.size },
    { key: "palletCount", header: "Pallets", fit: true, align: "right", render: (r) => r.palletCount.toLocaleString() },
  ];

  const moveColumns: Column<TransferLine>[] = [
    { key: "description", header: "Product", render: (r) => r.description },
    { key: "size", header: "Size", fit: true, align: "right", render: (r) => r.size },
    { key: "fromName", header: "From", fit: true },
    { key: "toName", header: "To", fit: true },
    { key: "qty", header: "Pallets", fit: true, align: "right", render: (r) => r.qty.toLocaleString() },
    {
      key: "remove",
      header: " ",
      fit: true,
      render: (r) => (
        <button className="link-btn" onClick={() => removeLine(r)}>
          ✕
        </button>
      ),
    },
  ];

  return (
    <Modal
      title="New Stock Transfer"
      width={1000}
      height={640}
      onClose={() => onClose(false)}
      footer={
        <>
          <span className="prod-summary-total" style={{ marginRight: "auto" }}>
            Moving:{" "}
            <strong>{pending.reduce((s, p) => s + p.qty, 0).toLocaleString()} pallets</strong>
            &nbsp;({pending.length.toLocaleString()} movements)
          </span>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button
            className="btn-yellow"
            disabled={busy || pending.length === 0}
            onClick={() => void complete()}
          >
            {busy ? "Transferring…" : "Complete Transfer"}
          </button>
        </>
      }
    >
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 46px 1.2fr",
          gap: 10,
          height: "100%",
          minHeight: 0,
        }}
      >
        {/* Left: source location stock */}
        <div style={{ display: "flex", flexDirection: "column", gap: 6, minWidth: 0, minHeight: 0 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <label className="toolbar-label">From:</label>
            <select
              className="form-input"
              style={{ width: 200, flex: "none" }}
              value={fromId}
              onChange={(e) => setFromId(Number(e.target.value))}
            >
              <option value={0}>— Select location —</option>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name}
                </option>
              ))}
            </select>
          </div>
          <DataTable
            columns={stockColumns}
            rows={sourceRows}
            rowKey={(r) => r.productId ?? r.code13 ?? "x"}
            selectedKey={selected?.productId ?? null}
            onSelect={setSelected}
            onDoubleClick={openQty}
            emptyText={fromId > 0 ? "No stock at this location" : "Select the source location"}
          />
        </div>

        {/* Middle: the transfer arrow */}
        <div style={{ display: "flex", alignItems: "center", justifyContent: "center" }}>
          <button
            className="btn-yellow"
            style={{ padding: "0 10px", fontSize: 16, height: 34 }}
            title="Transfer the selected product"
            disabled={!selected || fromId <= 0}
            onClick={() => selected && openQty(selected)}
          >
            →
          </button>
        </div>

        {/* Right: the movements making up this transfer */}
        <div style={{ display: "flex", flexDirection: "column", gap: 6, minWidth: 0, minHeight: 0 }}>
          <h3 className="po-side-title" style={{ margin: 0 }}>
            Product Movements
          </h3>
          <DataTable
            columns={moveColumns}
            rows={pending}
            rowKey={(r) => `${r.productId}-${r.fromId}-${r.toId}`}
            rowClass={() => "row-pending"}
            emptyText="No movements yet — pick a product on the left and press →"
          />
        </div>
      </div>

      {qtyFor && (
        <Modal
          title={`Transfer ${qtyFor.description ?? "product"}`}
          width={400}
          onClose={() => setQtyFor(null)}
          footer={
            <>
              <button className="toolbar-btn" onClick={() => setQtyFor(null)}>
                Cancel
              </button>
              <button
                className="btn-yellow"
                disabled={qty == null || toId <= 0}
                onClick={confirmQty}
              >
                Add Movement
              </button>
            </>
          }
        >
          <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
            <div className="form-field form-field-inline">
              <label className="form-label">From</label>
              <span className="form-static">{locName(fromId)}</span>
            </div>
            <div className="form-field form-field-inline">
              <label className="form-label">To *</label>
              <select
                className="form-input"
                style={{ width: 180, flex: "none" }}
                value={toId}
                onChange={(e) => setToId(Number(e.target.value))}
              >
                <option value={0}>— Select —</option>
                {locations
                  .filter((l) => l.id !== fromId)
                  .map((l) => (
                    <option key={l.id} value={l.id}>
                      {l.name}
                    </option>
                  ))}
              </select>
            </div>
            <div className="form-field form-field-inline">
              <label className="form-label">Quantity (pallets)</label>
              <input
                className="form-input"
                type="number"
                min={1}
                max={qtyFor.palletCount}
                autoFocus
                style={{ width: 100, flex: "none", textAlign: "left" }}
                value={qty ?? ""}
                onChange={(e) => setQty(e.target.value === "" ? null : Number(e.target.value))}
                onKeyDown={(e) => e.key === "Enter" && confirmQty()}
              />
            </div>
            <p style={{ fontSize: 12, color: "var(--text-secondary)", margin: 0 }}>
              {qtyFor.palletCount.toLocaleString()} pallets available at {locName(fromId)}.
            </p>
          </div>
        </Modal>
      )}
    </Modal>
  );
}
