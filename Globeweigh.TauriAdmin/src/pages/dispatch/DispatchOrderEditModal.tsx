import { useEffect, useMemo, useState } from "react";
import Modal from "../../components/Modal";
import DataTable, { Column } from "../../components/DataTable";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  DispatchOrder,
  OrderInput,
  OrderItemInput,
  createOrder,
  updateOrder,
  getOrderEditItems,
} from "../../api/dispatch";
import {
  getCustomersSuppliers,
  getAddresses,
  getSalesReps,
  SalesRepRef,
  CustomerSupplier,
  CustomerAddress,
} from "../../api/customersSuppliers";
import { getHauliers, HaulierRow } from "../../api/hauliers";
import { FINISHED_PRODUCT_TYPE_ID, getProducts, priceUnitsOf, Product } from "../../api/products";
import { getZonePrices } from "../../api/costing";
import { toDateInput } from "../../format";
import AddOrderItemModal, { OrderItemDraft } from "./AddOrderItemModal";
import SelectCustomerAddressModal from "./SelectCustomerAddressModal";
import { ORDER_DIALOG_WIDTH, ORDER_DIALOG_HEIGHT } from "./orderDialogSize";
import SetCostingZoneModal from "./SetCostingZoneModal";
import { SalesOrderRow, approveSalesOrder, createSalesOrder, updateSalesOrder } from "../../api/salesOrders";
import { useSession } from "../../session";
import { UserRole, hasRole } from "../../types";
import styles from "../../widgets/DetailPanel.module.css";
import { cx } from "../../widgets/DetailPanel";
import "./Dispatch.css";

interface Props {
  existing: DispatchOrder | null; // null = new
  onClose: (saved: boolean) => void;
  // My Orders (the Sales role): the same dialog, saving a salesperson's draft rather than an
  // office order. `order` is the draft being edited, null for a new one. The office-only rows
  // (order number, haulier, vehicle, trailer) are left out; the office sets those on approval.
  sales?: { order: SalesOrderRow | null };
  // The office approving an order a salesperson sent (Orders / Dispatch, "Orders (from sales)"):
  // a new office order filled in from it, with the office rows (haulier, vehicle, trailer) to
  // add. Saving makes the real order and marks the sales order approved.
  approve?: SalesOrderRow;
}

// Line item held in the order being edited.
interface ItemRow {
  productId: number;
  quantity: number | null;
  price: number | null;
  // Marks the POS board's grid row. Not a real line: it is the order's hasPos flag drawn as
  // one, pinned to the bottom, and never saved as an item.
  isPos?: true;
}

const POS_ROW: ItemRow = { productId: 0, quantity: 1, price: null, isPos: true };

// Mirrors CreateDispatchOrderView — add/edit a dispatch order and its line items.
// Header fields sit in the left column; the order items grid fills the right.
export default function DispatchOrderEditModal({ existing, onClose, sales, approve }: Props) {
  const salesOrder = sales?.order ?? null;
  // Whichever sales order fills the form: the rep's draft, or the one the office is approving.
  const fromSales = salesOrder ?? approve ?? null;
  // A Sales user building an order picks only from their own customers (the Customers grid's
  // Sales Rep). Anyone else trying My Orders still sees them all.
  const { user } = useSession();
  const repOnly = sales && hasRole(user, UserRole.Sales) ? user.id : undefined;
  const isNew = sales ? salesOrder === null : approve ? true : existing === null;
  // The header fields both kinds of order share, from whichever is being edited.
  const start = fromSales ?? existing;

  const [customers, setCustomers] = useState<CustomerSupplier[]>([]);
  const [hauliers, setHauliers] = useState<HaulierRow[]>([]);
  const [products, setProducts] = useState<Product[]>([]);

  // Order number. Only a pre-hand-over open order lets you correct it (orderNoEditable,
  // from the API); a new order gets its number on save; every other case is read-only.
  const [orderNo, setOrderNo] = useState(existing ? String(existing.orderNo) : "");
  const [orderNoEditable, setOrderNoEditable] = useState(false);

  const [customerId, setCustomerId] = useState(start?.customerId ?? 0);
  const [addressId, setAddressId] = useState(fromSales?.addressId ?? 0);
  const [addresses, setAddresses] = useState<CustomerAddress[]>([]);
  const [haulierId, setHaulierId] = useState(0);
  const [asap, setAsap] = useState(start ? start.deliveryDate == null : true);
  const [deliveryDate, setDeliveryDate] = useState(toDateInput(start?.deliveryDate));
  const [poNumber, setPoNumber] = useState(start?.poNumber ?? "");
  const [vehicleReg, setVehicleReg] = useState(existing?.vehicleReg ?? "");
  const [trailerNo, setTrailerNo] = useState(existing?.trailerNo ?? "");
  const [comments, setComments] = useState(fromSales?.comments ?? ""); // prints on the delivery note
  const [pickingComments, setPickingComments] = useState(fromSales?.pickingComments ?? "");
  const [items, setItems] = useState<ItemRow[]>(
    (fromSales?.items ?? []).map((i) => ({ productId: i.productId, quantity: i.quantity, price: i.price })),
  );
  // A POS (point-of-sale) board: 0 or 1 per order, no product/stock/price behind it.
  const [hasPos, setHasPos] = useState(fromSales?.hasPos ?? false);
  // The sales rep the order is credited to (office orders only). An order a rep sent from My
  // Orders keeps that rep, shown fixed; any other order gets the dropdown.
  const [salesReps, setSalesReps] = useState<SalesRepRef[]>([]);
  const [salesRepId, setSalesRepId] = useState<number | null>(null);
  const [salesRepFixed, setSalesRepFixed] = useState<string | null>(
    approve ? approve.salesRepName ?? "The rep who sent it" : null,
  );
  const gridRows = hasPos ? [...items, POS_ROW] : items;
  const [itemModal, setItemModal] = useState<{ index: number | null } | null>(null);
  const [customerPicker, setCustomerPicker] = useState(false);
  const [editCustomer, setEditCustomer] = useState(false);

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  const selectedCustomer = customers.find((c) => c.id === customerId) ?? null;
  const selectedAddress = addresses.find((a) => a.id === addressId) ?? null;

  // An editable order number must be a whole number in range before Save is allowed;
  // duplicates are caught server-side (the message surfaces on save).
  const orderNoText = orderNo.trim();
  const orderNoParsed = /^\d+$/.test(orderNoText) ? parseInt(orderNoText, 10) : NaN;
  const orderNoValid =
    !orderNoEditable ||
    (Number.isInteger(orderNoParsed) && orderNoParsed >= 1 && orderNoParsed <= 99999999);

  // Load lookups (and, when editing, the existing items) on mount.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [cs, hl, pr] = await Promise.all([
          getCustomersSuppliers(1),
          getHauliers(),
          getProducts(),
        ]);
        if (cancelled) return;
        setCustomers(cs);
        setHauliers(hl);
        setProducts(pr);
        if (!sales) getSalesReps().then((r) => !cancelled && setSalesReps(r)).catch(() => undefined);
        if (existing && !sales) {
          // The open-orders row carries haulierId; fall back to name match for older data.
          if (existing.haulierId > 0) setHaulierId(existing.haulierId);
          else {
            const h = hl.find((x) => x.name === existing.haulierName);
            if (h) setHaulierId(h.id);
          }
          const detail = await getOrderEditItems(existing.id);
          if (cancelled) return;
          setOrderNo(String(detail.orderNo));
          setOrderNoEditable(detail.orderNoEditable);
          setComments(detail.comments ?? "");
          setPickingComments(detail.pickingComments ?? "");
          setHasPos(detail.hasPos ?? false);
          setAddressId(detail.addressId);
          setSalesRepId(detail.salesRepId);
          if (detail.salesRepFixed) setSalesRepFixed(detail.salesRepName ?? "The rep who sent it");
          setItems(
            detail.items.map((i) => ({ productId: i.productId, quantity: i.quantity, price: i.price })),
          );
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
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `sales` is a fresh object every
    // parent render; whether this is a sales draft is all the load depends on.
  }, [existing, !!sales]);

  // Keep the selected customer's address list current — used to show the chosen
  // address and to require one whenever the customer has any.
  useEffect(() => {
    if (customerId <= 0) {
      setAddresses([]);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const rows = await getAddresses(customerId);
        if (!cancelled) setAddresses(rows);
      } catch {
        if (!cancelled) setAddresses([]);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [customerId]);

  function onPickCustomer(customer: CustomerSupplier, address: CustomerAddress | null, deliveryNote: string | null) {
    // Keep the local list current so costing-zone lookups see this customer.
    setCustomers((prev) => (prev.some((c) => c.id === customer.id) ? prev : [...prev, customer]));
    setCustomerId(customer.id);
    setAddressId(address?.id ?? 0);
    // A new order's rep defaults to the customer's (the Customers grid's Sales Rep), still
    // changeable in the dropdown. An existing order keeps the rep it has.
    if (isNew && !salesRepFixed) setSalesRepId(customer.salesRepId ?? null);
    // The address's Delivery Note Info goes onto Comments (which print on the delivery
    // note), above anything already typed there.
    if (deliveryNote) {
      setComments((cur) => (cur.includes(deliveryNote) ? cur : [deliveryNote, cur.trim()].filter(Boolean).join("\n\n")));
    }
  }

  // Mirrors the WPF OnAdd check: a customer must have a costing zone before items
  // can be priced. If none is set, offer to edit the customer and assign one.
  async function onAddItem() {
    if (!selectedCustomer) return;
    if ((selectedCustomer.costingZoneId ?? 0) === 0) {
      const ok = await confirmDialog(
        "Customer has no Costing Zone setup.\nPress OK to Edit Customer and set one up.",
      );
      if (!ok) return;
      setEditCustomer(true);
      return;
    }
    setItemModal({ index: null });
  }

  // After the customer is edited, reload so the new costing zone flows through to
  // item pricing, then continue into the Add Item dialog.
  async function reloadCustomerThenAddItem() {
    try {
      const cs = await getCustomersSuppliers(1);
      setCustomers(cs);
      const updated = cs.find((c) => c.id === customerId);
      if (updated && (updated.costingZoneId ?? 0) > 0) {
        setItemModal({ index: null });
      } else {
        void messageDialog("No Costing Zone was assigned to the customer.");
      }
    } catch (err) {
      void messageDialog("Failed to reload customer.\n" + (err as Error).message);
    }
  }

  function applyItem(draft: OrderItemDraft) {
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
  // DEV ONLY (the lavender "+ Random line"): a quick line for testing — a finished product not
  // already on the order, 1–4 pallets, priced from the customer's costing zone as Add Item
  // would (a product the zone has no price for gets 1.00).
  async function addRandomLine() {
    const taken = new Set(items.map((i) => i.productId));
    const pool = products.filter(
      (p) => p.productTypeId === FINISHED_PRODUCT_TYPE_ID && !p.dateDeleted && !taken.has(p.id),
    );
    if (pool.length === 0) {
      void messageDialog("Every finished product is already on the order.");
      return;
    }
    const zoneId = selectedCustomer?.costingZoneId ?? 0;
    let prices: Record<number, number> = {};
    try {
      if (zoneId > 0) for (const z of await getZonePrices(zoneId)) prices[z.productId] = z.price;
    } catch {
      prices = {};
    }
    // Prefer a product the zone prices, so the line looks like a real one.
    const priced = pool.filter((p) => prices[p.id] != null);
    const from = priced.length > 0 ? priced : pool;
    const p = from[Math.floor(Math.random() * from.length)];
    setItems((rows) => [
      ...rows,
      { productId: p.id, quantity: 1 + Math.floor(Math.random() * 4), price: prices[p.id] ?? 1 },
    ]);
  }

  async function removePos() {
    if (!(await confirmDialog("Remove the POS board?"))) return;
    setHasPos(false);
  }

  function productName(id: number) {
    return products.find((p) => p.id === id)?.description ?? `#${id}`;
  }

  async function onSave() {
    if (customerId <= 0) {
      void messageDialog("Customer is required.");
      return;
    }
    if (addresses.length > 0 && !selectedAddress) {
      void messageDialog("Select a delivery address.");
      return;
    }
    const itemInputs: OrderItemInput[] = items
      .filter((i) => i.productId > 0)
      .map((i) => ({ productId: i.productId, quantity: i.quantity, price: i.price }));

    const payload: OrderInput = {
      customerId,
      addressId: selectedAddress?.id ?? 0,
      haulierId,
      deliveryDate: asap ? null : deliveryDate ? new Date(deliveryDate).toISOString() : null,
      poNumber: poNumber.trim() || null,
      comments: comments.trim() || null,
      pickingComments: pickingComments.trim() || null,
      hasPos,
      salesRepId,
      vehicleReg: vehicleReg.trim() || null,
      trailerNo: trailerNo.trim() || null,
      // Sent only when the number is editable; the server re-checks and ignores it otherwise.
      orderNo: orderNoEditable && Number.isInteger(orderNoParsed) ? orderNoParsed : undefined,
      items: itemInputs,
    };

    setBusy(true);
    try {
      if (sales) {
        // A salesperson's draft: the office fields are theirs to set on approval.
        const draft = {
          customerId: payload.customerId,
          addressId: payload.addressId,
          deliveryDate: payload.deliveryDate,
          poNumber: payload.poNumber,
          comments: payload.comments,
          pickingComments: payload.pickingComments,
          hasPos: payload.hasPos,
          items: payload.items,
        };
        if (salesOrder) await updateSalesOrder(salesOrder.id, draft);
        else await createSalesOrder(draft);
      } else if (approve) await approveSalesOrder(approve.id, payload);
      else if (isNew) await createOrder(payload);
      else await updateOrder(existing!.id, payload);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  // The chosen address, a line per part; null while there is none to show, so each mode says
  // in its own way that one is wanted.
  const addressText = selectedAddress
    ? [
        selectedAddress.addressName,
        selectedAddress.address1,
        selectedAddress.address2,
        selectedAddress.address3,
        selectedAddress.address4,
        selectedAddress.address5,
      ]
        .filter(Boolean)
        .join("\n")
    : null;

  // Where the order has got to, riding the ORDER DETAILS title as it does on the page.
  const status = approve
    ? "From sales"
    : sales
    ? !salesOrder
      ? "New"
      : salesOrder.status === "returned"
        ? "Returned"
        : "Draft"
    : !existing
    ? "New"
    : existing.dateDeleted
      ? "Deleted"
      : existing.dateDispatched
        ? "Dispatched"
        : "Open";

  // Where the order's paperwork goes: the chosen customer's addresses, shown under the delivery
  // address. Trimmed and de-duplicated, as the Load dialog shows a haulier's.
  const customerEmails = useMemo(
    () => [...new Set((selectedCustomer?.emails ?? []).map((e) => e.trim()).filter(Boolean))],
    [selectedCustomer],
  );

  return (
    <>
      <Modal
        title={approve ? `Approve Order ${approve.ref}${approve.salesRepName ? ` from ${approve.salesRepName}` : ""}` : `${isNew ? "Add New" : "Edit"} Order`}
        width={ORDER_DIALOG_WIDTH}
        height={ORDER_DIALOG_HEIGHT}
        onClose={() => onClose(false)}
        footer={
          <>
            <button className="toolbar-btn" onClick={() => onClose(false)}>
              Cancel
            </button>
            <button
              className="btn-blue"
              disabled={busy || loading || customerId <= 0 || !orderNoValid}
              onClick={onSave}
            >
              {busy ? "Saving…" : approve ? "Approve as order" : "Save"}
            </button>
          </>
        }
      >
        {loading ? (
          <div className="data-table-empty">Loading…</div>
        ) : (
          <div className="cl-form">
            {/* One white panel — the Order Details page in the Create/Edit Load dialog's frame: the
                order down the left as the page has it, its lines filling the right. The section
                titles and label/value rows are the page's own, with the controls where the values
                go. */}
            <div className="ld-panel">
              <div className="od-body">
                <div className="od-side">
                  <section className={styles.section}>
                    <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
                      <span>Order Details</span>
                      <span
                        className={cx(
                          styles.statusTag,
                          status === "Deleted" || status === "Returned"
                            ? styles.statusDeleted
                            : status === "Dispatched" && styles.statusDispatched,
                        )}
                      >
                        {status}
                      </span>
                    </h3>
                    {sales ? (
                      <div className={cx(styles.field, "ld-row")}>
                        <span className={styles.fieldLabel}>Ref</span>
                        <span className="ld-static">
                          {salesOrder?.ref ?? <span className={styles.muted}>Assigned on save</span>}
                        </span>
                      </div>
                    ) : (
                    <div className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel}>Order No</span>
                      {!isNew && orderNoEditable ? (
                        <div className="od-stack">
                          <input
                            className="ld-input od-orderno"
                            value={orderNo}
                            inputMode="numeric"
                            onChange={(e) => setOrderNo(e.target.value)}
                          />
                          {!orderNoValid && (
                            <span className={cx(styles.bad, "od-hint")}>Whole number, 1–99,999,999.</span>
                          )}
                        </div>
                      ) : (
                        <span className="ld-static">
                          {isNew ? (
                            <span className={styles.muted}>Assigned on save</span>
                          ) : (
                            orderNo || existing?.orderNo || "—"
                          )}
                        </span>
                      )}
                    </div>
                    )}
                    {/* The customer and address are chosen together, in their own dialog. */}
                    <div className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel}>Customer *</span>
                      <button
                        type="button"
                        className="ld-input ld-picker"
                        autoFocus
                        onClick={() => setCustomerPicker(true)}
                      >
                        <span className={cx("ld-picker-value", !selectedCustomer && styles.muted)}>
                          {selectedCustomer?.name ?? "— select —"}
                        </span>
                        <span className="ld-picker-hint">{selectedCustomer ? "Change…" : "Select…"}</span>
                      </button>
                    </div>
                    <div className={cx(styles.field, "ld-row", "ld-row-top")}>
                      <span className={styles.fieldLabel}>Address{addresses.length > 0 ? " *" : ""}</span>
                      <span className="ld-static ld-static-prose">
                        {addressText ?? (
                          <span className={styles.muted}>{addresses.length > 0 ? "— select —" : "—"}</span>
                        )}
                      </span>
                    </div>
                    <div className={cx(styles.field, "ld-row", "ld-row-top")}>
                      <span className={styles.fieldLabel}>Emails</span>
                      <span className="ld-static ld-static-prose">
                        {customerEmails.length > 0 ? (
                          <span className={styles.sentTo}>{customerEmails.join(", ")}</span>
                        ) : selectedCustomer ? (
                          <span className={cx(styles.statusTag, styles.statusDeleted)}>No emails set up</span>
                        ) : (
                          <span className={styles.muted}>—</span>
                        )}
                      </span>
                    </div>
                    {!sales && (
                    <label className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel}>Haulier</span>
                      <select
                        className="ld-input"
                        value={haulierId}
                        onChange={(e) => setHaulierId(Number(e.target.value))}
                      >
                        <option value={0}>— None —</option>
                        {hauliers.map((h) => (
                          <option key={h.id} value={h.id}>
                            {h.name}
                          </option>
                        ))}
                      </select>
                    </label>
                    )}
                    <div className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel}>Delivery</span>
                      <div className="od-delivery">
                        <label className="ld-check">
                          <input type="checkbox" checked={asap} onChange={(e) => setAsap(e.target.checked)} />
                          <span>ASAP</span>
                        </label>
                        {!asap && (
                          <input
                            className="ld-input"
                            type="date"
                            value={deliveryDate}
                            onChange={(e) => setDeliveryDate(e.target.value)}
                          />
                        )}
                      </div>
                    </div>
                    <label className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel}>PO Number</span>
                      <input className="ld-input" value={poNumber} onChange={(e) => setPoNumber(e.target.value)} />
                    </label>
                    {!sales && (
                    <>
                    <label className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel}>Vehicle Reg</span>
                      <input className="ld-input" value={vehicleReg} onChange={(e) => setVehicleReg(e.target.value)} />
                    </label>
                    <label className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel}>Trailer No</span>
                      <input className="ld-input" value={trailerNo} onChange={(e) => setTrailerNo(e.target.value)} />
                    </label>
                    </>
                    )}
                  </section>

                  {/* The two comment fields, in the page's order: the delivery note's first, since
                      picking a customer can put the address's Delivery Note Info straight into it. */}
                  <section className={cx(styles.section, "od-comments")}>
                    <h3 className={styles.sectionTitle}>Comments</h3>
                    <label className={cx(styles.field, "ld-row", "ld-row-top", "od-comment")}>
                      <span className={styles.fieldLabel}>Delivery Note</span>
                      <textarea
                        className="ld-input ld-comments-box"
                        value={comments}
                        onChange={(e) => setComments(e.target.value)}
                      />
                    </label>
                    <label className={cx(styles.field, "ld-row", "ld-row-top", "od-comment")}>
                      <span className={styles.fieldLabel}>Picking</span>
                      <textarea
                        className="ld-input ld-comments-box"
                        value={pickingComments}
                        onChange={(e) => setPickingComments(e.target.value)}
                      />
                    </label>
                    {!sales &&
                      (salesRepFixed ? (
                        <div className={cx(styles.field, "ld-row")}>
                          <span className={styles.fieldLabel}>Sales Rep</span>
                          <span className="ld-static" title="Sent from My Orders: the rep is fixed">
                            {salesRepFixed}
                          </span>
                        </div>
                      ) : (
                        <label className={cx(styles.field, "ld-row")}>
                          <span className={styles.fieldLabel}>Sales Rep</span>
                          <select
                            className="ld-input"
                            value={salesRepId ?? 0}
                            onChange={(e) => setSalesRepId(Number(e.target.value) || null)}
                          >
                            <option value={0}>— None —</option>
                            {/* A disabled rep is offered only while they are the order's rep. */}
                            {salesReps
                              .filter((r) => !r.disabled || r.id === salesRepId)
                              .map((r) => (
                                <option key={r.id} value={r.id}>
                                  {r.name}
                                </option>
                              ))}
                          </select>
                        </label>
                      ))}
                  </section>
                </div>

                {/* Right: the lines — added via the product-picker popup (mirrors the WPF dialog). */}
                <section className={cx(styles.section, "ld-grid")}>
                  <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
                    <span>Items</span>
                    <span className="od-title-btns">
                      <button
                        type="button"
                        className="ld-title-btn"
                        disabled={customerId <= 0}
                        title={customerId <= 0 ? "Select a customer first" : undefined}
                        onClick={() => void onAddItem()}
                      >
                        + Add Item
                      </button>
                      <button
                        type="button"
                        className="ld-title-btn od-pos-btn"
                        disabled={hasPos}
                        title={hasPos ? "This order already has a POS board" : "Add a POS (point-of-sale) board"}
                        onClick={() => setHasPos(true)}
                      >
                        + Add POS
                      </button>
                      {import.meta.env.DEV && (
                        <button
                          type="button"
                          className="ld-title-btn od-dev-btn"
                          disabled={customerId <= 0}
                          title={customerId <= 0 ? "Select a customer first" : "Dev only: add a random product, 1-4 pallets"}
                          onClick={() => void addRandomLine()}
                        >
                          + Random line
                        </button>
                      )}
                    </span>
                  </h3>
                  <DataTable
                    columns={
                      [
                        {
                          key: "product",
                          header: "Product",
                          render: (r: ItemRow) =>
                            r.isPos ? <span className="od-pos-name">POS Board</span> : productName(r.productId),
                        },
                        {
                          key: "size",
                          header: "Size",
                          width: 55,
                          align: "right",
                          render: (r: ItemRow) =>
                            r.isPos ? "" : (products.find((p) => p.id === r.productId)?.size ?? ""),
                        },
                        {
                          key: "quantity",
                          header: "Quantity",
                          width: 90,
                          align: "right",
                          render: (r: ItemRow) => r.quantity ?? "",
                        },
                        {
                          key: "price",
                          header: "Price",
                          width: 80,
                          align: "right",
                          render: (r: ItemRow) => (r.price != null ? r.price.toFixed(2) : ""),
                        },
                        {
                          key: "total",
                          header: "Total",
                          width: 90,
                          align: "right",
                          // Price is per unit with quantity in pallets (scaled by UPP), or per m³ for a cubic meter product.
                          render: (r: ItemRow) =>
                            r.quantity != null && r.price != null
                              ? (r.quantity * priceUnitsOf(products.find((p) => p.id === r.productId)) * r.price).toFixed(2)
                              : "",
                        },
                        {
                          key: "actions",
                          header: " ",
                          width: 80,
                          render: (_r: ItemRow) => {
                            const idx = items.indexOf(_r);
                            return (
                              <button
                                className="link-btn"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  void (_r.isPos ? removePos() : removeItem(idx));
                                }}
                              >
                                Remove
                              </button>
                            );
                          },
                        },
                      ] as Column<ItemRow>[]
                    }
                    rows={gridRows}
                    rowKey={(_r) => (_r.isPos ? "pos" : items.indexOf(_r))}
                    rowClass={(r) => (r.isPos ? "od-pos-row" : undefined)}
                    onDoubleClick={(r) => {
                      if (!r.isPos) setItemModal({ index: items.indexOf(r) });
                    }}
                    emptyText="No items — click Add Item"
                    // Not sortable: the grid stays in the order lines were added, which is the
                    // order they save in, with the POS board (if any) always last.
                  />
                </section>
              </div>
            </div>
          </div>
        )}
      </Modal>

      {customerPicker && (
        <SelectCustomerAddressModal
          initialCustomerId={customerId}
          initialAddressId={addressId}
          salesRepId={repOnly}
          onSelect={onPickCustomer}
          onClose={() => setCustomerPicker(false)}
        />
      )}

      {itemModal && (
        <AddOrderItemModal
          customerId={customerId}
          // A rep building an order in My Orders does not see stock; the office approving it does.
          hideStock={!!sales}
          costingZoneId={selectedCustomer?.costingZoneId ?? 0}
          costingZoneName={selectedCustomer?.costingZone ?? null}
          alreadySelectedProductIds={items
            .filter((_, i) => i !== itemModal.index)
            .map((i) => i.productId)}
          existing={itemModal.index != null ? items[itemModal.index] : null}
          onAdd={applyItem}
          onClose={() => setItemModal(null)}
        />
      )}

      {editCustomer && selectedCustomer && (
        <SetCostingZoneModal
          customer={selectedCustomer}
          onClose={(saved) => {
            setEditCustomer(false);
            if (saved) void reloadCustomerThenAddItem();
          }}
        />
      )}
    </>
  );
}
