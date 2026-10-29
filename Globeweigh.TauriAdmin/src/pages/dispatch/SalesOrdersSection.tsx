import { useCallback, useEffect, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import { getProducts, priceUnitsOf, Product } from "../../api/products";
import {
  SalesOrderRow,
  deleteOfficeSalesOrder,
  getOfficeSalesOrders,
  returnSalesOrder,
} from "../../api/salesOrders";
import Modal from "../../components/Modal";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import { ago, dayLabel, gbp } from "../sales/myOrdersShared";
import DispatchOrderEditModal from "./DispatchOrderEditModal";
import { useRowMenu } from "./useRowMenu";

// Orders / Dispatch, foot of the Open Orders column: "Orders (from sales)" — the orders the
// sales reps have built in My Orders and sent to the office, waiting to be approved. Longest
// waiting first. Drafts are the reps' own and never appear here. Approve opens the order in the
// order dialog (the office may change it and add the haulier) and saving makes it an open
// order; Return sends it back to the rep with a note. `onApproved` refreshes Open Orders.
export default function SalesOrdersSection({ onApproved }: { onApproved?: () => void }) {
  const [rows, setRows] = useState<SalesOrderRow[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [approving, setApproving] = useState<SalesOrderRow | null>(null);
  const [returning, setReturning] = useState<SalesOrderRow | null>(null);
  const selected = rows.find((o) => o.id === selectedId) ?? null;
  const [menu, menuApi] = useRowMenu<SalesOrderRow>();

  async function onDelete(o: SalesOrderRow) {
    const who = o.salesRepName ?? "the rep";
    const ask = `Delete ${o.ref} for ${o.customerName ?? "this customer"}?\nIt is removed from ${who}'s My Orders too.`;
    if (!(await confirmDialog(ask))) return;
    try {
      await deleteOfficeSalesOrder(o.id);
      if (selectedId === o.id) setSelectedId(null);
      await refresh();
    } catch (err) {
      void messageDialog("Failed to delete the order.\n" + (err as Error).message);
    }
  }

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await getOfficeSalesOrders());
    } catch {
      // Leave the last list up; the next refresh tries again.
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
    getProducts()
      .then(setProducts)
      .catch(() => setProducts([]));
    // Reps send orders at any time: keep the list current without a manual refresh.
    const t = window.setInterval(() => void refresh(), 60000);
    return () => window.clearInterval(t);
  }, [refresh]);

  const pallets = (o: SalesOrderRow) => o.items.reduce((n, i) => n + (i.quantity ?? 0), 0);
  const value = (o: SalesOrderRow) =>
    o.items.reduce(
      (n, i) =>
        n +
        (i.quantity != null && i.price != null
          ? i.quantity * priceUnitsOf(products.find((p) => p.id === i.productId)) * i.price
          : 0),
      0,
    );

  const columns: Column<SalesOrderRow>[] = [
    {
      key: "dateSent",
      header: "Sent",
      fit: true,
      render: (o) => (o.dateSent ? ago(o.dateSent) : ""),
      sortValue: (o) => (o.dateSent ? new Date(o.dateSent) : null),
    },
    { key: "salesRepName", header: "Rep", width: 96, render: (o) => o.salesRepName ?? "" },
    {
      key: "customerName",
      header: "Customer",
      render: (o) => (
        <>
          {o.customerName}
          {o.hasPos && <span className="dsp-sales-pos">POS</span>}
        </>
      ),
    },
    {
      key: "deliveryDate",
      header: "Delivery",
      fit: true,
      render: (o) => dayLabel(o.deliveryDate),
      sortValue: (o) => (o.deliveryDate ? new Date(o.deliveryDate) : null),
    },
    { key: "pallets", header: "Pallets", fit: true, align: "right", render: (o) => pallets(o), sortValue: pallets },
    { key: "value", header: "Value", fit: true, align: "right", render: (o) => gbp(value(o)), sortValue: value },
    {
      // Far-right "⋮" — the row's actions menu, as on the Loads grid.
      key: "menu",
      header: "",
      fit: true,
      align: "center",
      noSort: true,
      render: (o) => (
        <button
          type="button"
          className="dsp-row-menu-btn"
          title="More actions"
          aria-haspopup="menu"
          aria-expanded={menu?.row.id === o.id}
          onMouseDown={(e) => e.stopPropagation()}
          onDoubleClick={(e) => e.stopPropagation()}
          onClick={(e) => {
            e.stopPropagation();
            setSelectedId(o.id);
            menuApi.open(o, e.currentTarget);
          }}
        >
          ⋮
        </button>
      ),
    },
  ];

  return (
    <div className="dsp-sales-sec">
      <div className="dsp-grid-head">
        <h3 className="dsp-panel-title">Orders (from sales)</h3>
        {rows.length > 0 && <span className="dsp-sales-count">{rows.length} awaiting approval</span>}
        <span className="spacer" />
        {loading && <span className="toolbar-note">Loading…</span>}
        <button className="btn-blue" disabled={!selected} onClick={() => setApproving(selected)}>
          Approve
        </button>
        <button className="toolbar-btn" disabled={!selected} onClick={() => setReturning(selected)}>
          Return
        </button>
        <button className="toolbar-btn" onClick={() => void refresh()} disabled={loading}>
          Refresh
        </button>
      </div>
      <DataTable
        columns={columns}
        rows={rows}
        sortable
        rowKey={(o) => o.id}
        selectedKey={selectedId}
        onSelect={(o) => setSelectedId(o.id)}
        onDoubleClick={(o) => setApproving(o)}
        emptyText={loading ? "Loading…" : "No orders from sales waiting"}
      />
      {menu && (
        <div ref={menuApi.ref} className="dsp-row-menu" role="menu" style={{ top: menu.top, right: menu.right }}>
          <button
            type="button"
            role="menuitem"
            className="dsp-row-menu-item danger"
            onClick={() => {
              menuApi.close();
              void onDelete(menu.row);
            }}
          >
            Delete
          </button>
        </div>
      )}
      {approving && (
        <DispatchOrderEditModal
          existing={null}
          approve={approving}
          onClose={(saved) => {
            setApproving(null);
            if (!saved) return;
            setSelectedId(null);
            void refresh();
            onApproved?.();
          }}
        />
      )}
      {returning && (
        <ReturnSalesOrderModal
          order={returning}
          onClose={(saved) => {
            setReturning(null);
            if (!saved) return;
            setSelectedId(null);
            void refresh();
          }}
        />
      )}
    </div>
  );
}

// Send an order back to its rep. The note is what they see on the order in My Orders, so it
// is required: a bare "returned" tells them nothing about what to fix.
function ReturnSalesOrderModal({ order, onClose }: { order: SalesOrderRow; onClose: (saved: boolean) => void }) {
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);

  async function onReturn() {
    setBusy(true);
    try {
      await returnSalesOrder(order.id, note.trim());
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to return the order.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={`Return ${order.ref} to ${order.salesRepName ?? "the rep"}`}
      width={480}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || !note.trim()} onClick={() => void onReturn()}>
            {busy ? "Returning…" : "Return to rep"}
          </button>
        </>
      }
    >
      <div className="dsp-sales-return">
        <p>
          <b>{order.customerName}</b> — {dayLabel(order.deliveryDate)}. The rep can change the order and send
          it again.
        </p>
        <label>
          What needs changing?
          <textarea
            className="form-input"
            autoFocus
            rows={4}
            value={note}
            onChange={(e) => setNote(e.target.value)}
          />
        </label>
      </div>
    </Modal>
  );
}
