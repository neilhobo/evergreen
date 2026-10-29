import { priceUnitsPer } from "../../api/products";
import ToolbarDate from "../../components/ToolbarDate";
import { MouseEvent, useEffect, useMemo, useState } from "react";
import styles from "../../widgets/DetailPanel.module.css";
import { Field } from "../../widgets/DetailPanel";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  DispatchOrder,
  DispatchOrderItem,
  OrderEditDetail,
  getCompletedOrders,
  getOrderItems,
  getOrderEditItems,
  deleteOrder,
} from "../../api/dispatch";
import { fmtDate, fmtDateTime, fmtQty, todayInput } from "../../format";
import { AdminUserDto } from "../../types";
import DeliveryNoteModal from "./DeliveryNoteModal";
import DispatchOrderEditModal from "../dispatch/DispatchOrderEditModal";

interface Props {
  user: AdminUserDto;
  // "View Order" hands the order UP rather than opening the detail panel here: the panel flies in
  // over the whole content card, page header and tabs included, and this page is only the body of
  // that card. DispatchPage owns the card, so DispatchPage owns the panel.
  onViewOrder: (order: DispatchOrder) => void;
  // My Orders > Completed: only the signed-in rep's orders, and read-only (no Edit / Delete).
  mine?: boolean;
}

// Completed (dispatched) orders master/detail — mirrors the WPF CompletedOrderListView (read-only).
export default function CompletedOrdersPage({ onViewOrder, mine = false }: Props) {
  const [rows, setRows] = useState<DispatchOrder[]>([]);
  const [selected, setSelected] = useState<DispatchOrder | null>(null);
  const [items, setItems] = useState<DispatchOrderItem[]>([]);
  // Comments etc. for the selected order's header block (not carried on the grid row).
  const [detail, setDetail] = useState<OrderEditDetail | null>(null);
  const [search, setSearch] = useState("");
  const [from, setFrom] = useState(todayInput(-30));
  const [to, setTo] = useState(todayInput());
  const [loading, setLoading] = useState(false);
  const [noteOrderId, setNoteOrderId] = useState<number | null>(null);
  const [editing, setEditing] = useState<DispatchOrder | null>(null);
  // Multi-selection for delete: shift-click extends from the last plain click,
  // ctrl-click toggles a row. A plain click clears it back to one row.
  const [checked, setChecked] = useState<Set<number>>(new Set());
  const [anchorId, setAnchorId] = useState<number | null>(null);

  async function refresh() {
    setLoading(true);
    try {
      setRows(await getCompletedOrders(from, to, mine));
      setSelected(null);
      setItems([]);
      setDetail(null);
      setChecked(new Set());
    } catch (err) {
      void messageDialog("Failed to load completed orders.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function selectOrder(order: DispatchOrder) {
    setSelected(order);
    setDetail(null);
    try {
      const [its, det] = await Promise.all([getOrderItems(order.id), getOrderEditItems(order.id)]);
      setItems(its);
      setDetail(det);
    } catch (err) {
      setItems([]);
      void messageDialog("Failed to load order items.\n" + (err as Error).message);
    }
  }

  function handleSelect(order: DispatchOrder, e?: MouseEvent) {
    if (e?.shiftKey && anchorId != null) {
      const a = filtered.findIndex((r) => r.id === anchorId);
      const b = filtered.findIndex((r) => r.id === order.id);
      if (a !== -1 && b !== -1) {
        const [lo, hi] = a < b ? [a, b] : [b, a];
        setChecked(new Set(filtered.slice(lo, hi + 1).map((r) => r.id)));
      }
    } else if (e?.ctrlKey) {
      setChecked((prev) => {
        const next = new Set(prev);
        if (next.has(order.id)) next.delete(order.id);
        else next.add(order.id);
        return next;
      });
      setAnchorId(order.id);
    } else {
      setChecked(new Set());
      setAnchorId(order.id);
    }
    void selectOrder(order);
  }

  // Soft delete (the API stamps DateDeleted) — the orders drop out of the grids
  // but stay in the database. Deletes the ticked rows; with none ticked, the
  // highlighted row.
  async function onDelete() {
    const ids = checked.size > 0 ? [...checked] : selected ? [selected.id] : [];
    if (ids.length === 0) return;
    const label =
      ids.length === 1
        ? `order ${rows.find((r) => r.id === ids[0])?.orderNo ?? ids[0]}`
        : `${ids.length} orders`;
    if (!(await confirmDialog(`Delete ${label}?`))) return;
    try {
      for (const id of ids) await deleteOrder(id);
      await refresh();
    } catch (err) {
      void messageDialog("Failed to delete.\n" + (err as Error).message);
      await refresh();
    }
  }

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return rows;
    return rows.filter((r) =>
      [String(r.orderNo), r.customerName, r.haulierName, r.poNumber].some((v) =>
        v?.toLowerCase().includes(term),
      ),
    );
  }, [rows, term]);

  const columns: Column<DispatchOrder>[] = [
    { key: "orderNo", header: "Order No", width: 80, render: (r) => r.orderNo },
    { key: "dateDispatched", header: "Dispatched", width: 130, render: (r) => fmtDateTime(r.dateDispatched) },
    { key: "customerName", header: "Customer", render: (r) => <Highlight text={r.customerName} term={term} /> },
    { key: "haulierName", header: "Haulier", render: (r) => <Highlight text={r.haulierName} term={term} /> },
    { key: "vehicleReg", header: "Vehicle", width: 90, render: (r) => r.vehicleReg },
    { key: "pickedQuantity", header: "Picked", width: 80, align: "right", render: (r) => fmtQty(r.pickedQuantity) },
    {
      key: "deliveryNote",
      header: "",
      width: 100,
      render: (r) => (
        <button
          className="link-btn"
          onClick={(e) => {
            e.stopPropagation();
            setNoteOrderId(r.id);
          }}
        >
          Delivery Note
        </button>
      ),
    },
    {
      key: "viewOrder",
      header: "",
      width: 80,
      render: (r) => (
        <button
          className="link-btn"
          onClick={(e) => {
            e.stopPropagation();
            onViewOrder(r);
          }}
        >
          View Order
        </button>
      ),
    },
  ];

  // Same columns as the View Order items grid.
  const priceByProduct = useMemo(() => {
    const map: Record<number, number | null> = {};
    detail?.items.forEach((i) => (map[i.productId] = i.price));
    return map;
  }, [detail]);

  const itemColumns: Column<DispatchOrderItem>[] = [
    { key: "productDescription", header: "Product", render: (r) => r.productDescription },
    { key: "size", header: "Size", width: 45, align: "right", render: (r) => r.size },
    {
      key: "quantity",
      header: "Order Quantity",
      width: 95,
      align: "right",
      // A short/over pick flags the ordered quantity red too, matching the Picked cell.
      render: (r) =>
        (r.pickedQuantity ?? 0) !== (r.quantity ?? 0) ? (
          <span style={{ color: "#dc2626", fontWeight: 600 }}>{r.quantity}</span>
        ) : (
          r.quantity
        ),
    },
    {
      key: "pickedQuantity",
      header: "Picked",
      width: 55,
      align: "right",
      // Short-picked (or over-picked) lines flag in red.
      render: (r) =>
        (r.pickedQuantity ?? 0) !== (r.quantity ?? 0) ? (
          <span style={{ color: "#dc2626", fontWeight: 600 }}>{r.pickedQuantity ?? 0}</span>
        ) : (
          r.pickedQuantity
        ),
    },
    {
      key: "price",
      header: "Price",
      width: 55,
      align: "right",
      render: (r) => {
        const p = priceByProduct[r.productId];
        return p != null ? p.toFixed(2) : "";
      },
    },
    {
      key: "total",
      header: "Total",
      width: 65,
      align: "right",
      // Price is per unit with quantity in pallets (scaled by UPP), or per m³ for a cubic meter product.
      render: (r) => {
        const p = priceByProduct[r.productId];
        return r.quantity != null && p != null ? (r.quantity * priceUnitsPer(r.soldBy, r.upp) * p).toFixed(2) : "";
      },
    },
  ];

  return (
    <div className="po-page completed-orders">
      <div className="po-open-layout">
        <div className="po-main">
          <div className="toolbar">
            <input
              className="form-input toolbar-search"
              placeholder="Search orders…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <ToolbarDate label="From" value={from} onChange={setFrom} />
            <ToolbarDate label="To" value={to} onChange={setTo} />
            {loading && <span className="toolbar-note">Loading…</span>}
            <div className="toolbar-right">
              {checked.size > 0 && <span className="toolbar-note">{checked.size} selected</span>}
              {!mine && (
                <>
                  <button
                    className="toolbar-btn"
                    disabled={!selected}
                    title={selected ? undefined : "Select an order first"}
                    onClick={() => selected && setEditing(selected)}
                  >
                    Edit
                  </button>
                  <button
                    className="toolbar-btn"
                    disabled={checked.size === 0 && !selected}
                    onClick={() => void onDelete()}
                  >
                    {checked.size > 1 ? `Delete (${checked.size})` : "Delete"}
                  </button>
                </>
              )}
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
            onSelect={handleSelect}
            onDoubleClick={(r) => onViewOrder(r)}
            rowClass={(r) => (checked.has(r.id) ? "selected" : undefined)}
            emptyText={loading ? "Loading…" : "No orders"}
          />
        </div>
        {/* +200px over the default 360 — narrows the orders column by the same amount. */}
        <div className="po-side" style={{ width: 560 }}>
          <h3 className="po-side-title">
            {selected ? `Order ${selected.orderNo}` : "Selected Order"}
          </h3>
          {/* The order's header, laid out as the Order Details page lays out its left panel — the
              same section titles and label/value rows — so the two read alike. */}
          {selected && (
            <div className="co-detail">
              <section className={styles.section}>
                <h3 className={styles.sectionTitle}>Order Details</h3>
                <Field label="Customer" value={selected.customerName} />
                <Field
                  label="Address"
                  value={<span className={styles.prose}>{selected.addressFull}</span>}
                />
                <Field label="PO Number" value={selected.poNumber} />
                <Field label="Haulier" value={selected.haulierName} />
                <Field label="Vehicle Reg" value={selected.vehicleReg} />
                <Field label="Trailer No" value={selected.trailerNo} />
                <Field
                  label="Delivery"
                  value={selected.deliveryDate ? fmtDate(selected.deliveryDate) : "ASAP"}
                />
                <Field label="Dispatched" value={fmtDateTime(selected.dateDispatched)} />
              </section>
              <section className={styles.section}>
                <h3 className={styles.sectionTitle}>Comments</h3>
                <Field
                  label="Delivery Note"
                  value={detail ? <span className={styles.prose}>{detail.comments}</span> : "…"}
                />
                <Field
                  label="Picking"
                  value={
                    detail ? <span className={styles.prose}>{detail.pickingComments}</span> : "…"
                  }
                />
              </section>
            </div>
          )}
          <DataTable
            columns={itemColumns}
            rows={items}
            rowKey={(r) => r.id}
            emptyText={selected ? "No items" : "Select an order"}
          />
        </div>
      </div>

      {noteOrderId != null && (
        <DeliveryNoteModal orderId={noteOrderId} onClose={() => setNoteOrderId(null)} />
      )}

      {editing && (
        <DispatchOrderEditModal
          existing={editing}
          onClose={(saved) => {
            setEditing(null);
            if (saved) void refresh();
          }}
        />
      )}
    </div>
  );
}
