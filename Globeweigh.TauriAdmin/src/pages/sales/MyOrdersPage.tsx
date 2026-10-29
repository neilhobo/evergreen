import { useCallback, useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import { OrderItemInput } from "../../api/dispatch";
import { FINISHED_PRODUCT_TYPE_ID, getProducts, priceUnitsOf, Product } from "../../api/products";
import { getAddresses, getCustomersSuppliers } from "../../api/customersSuppliers";
import { getZonePrices } from "../../api/costing";
import "../dispatch/Dispatch.css";
import {
  SalesOrderDetail,
  SalesOrderRow,
  SalesOrderStatus,
  createSalesOrder,
  deleteSalesOrder,
  getSalesOrder,
  getSalesOrders,
  recallSalesOrder,
  sendSalesOrder,
} from "../../api/salesOrders";
import { AdminUserDto, UserRole, hasRole } from "../../types";
import { fmtDateTime } from "../../format";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import DispatchOrderEditModal from "../dispatch/DispatchOrderEditModal";
import { StatusPill, ago, dayLabel, gbp, isDraft, titleCase } from "./myOrdersShared";
import MyOrdersBoard from "./MyOrdersBoard";
import CompletedOrdersPage from "../completed-orders/CompletedOrdersPage";
import OrderDetailPanel from "../completed-orders/OrderDetailPanel";
import LoadDetailPanel from "../dispatch/LoadDetailPanel";
import { DispatchOrder } from "../../api/dispatch";
import PageTabs, { PageTab } from "../../widgets/PageTabs";
import "./MyOrders.css";

// My Orders — the Sales role's one tab (design C of the 2026-10-05 mock-ups: list + tracker).
// The salesperson's orders down the left, filtered by where they have got to; the picked one
// on the right with a five-step tracker (Draft → Awaiting approval → Approved → Transport allocated →
// Dispatched), its lines and its history, and the actions that fit its status along the foot.
// Orders are built in the Add/Edit Order dialog (sales mode) and stay drafts, seen by nobody
// else, until the salesperson sends them to the office.

// "Copy as new order" on an approved / finished order. Hidden for now (2026-10-16, the user's
// call — it may come back; View Load Detail took its place); set true to show it again beside
// it. The API side is untouched.
const SHOW_COPY_AS_NEW = false;

type View = "list" | "board" | "completed";
const VIEW_KEY = "myOrders.view";
const VIEW_TABS: PageTab<View>[] = [
  { key: "list", label: "List" },
  { key: "board", label: "Board" },
  // The Orders > Completed Orders page, narrowed to this rep's orders and read-only.
  { key: "completed", label: "Completed" },
];

const STEPS = ["Draft","Awaiting approval", "Approved", "Transport allocated", "Dispatched"];

const STEP_OF: Record<SalesOrderStatus, number> = {
  draft: 0,
  returned: 1,
  pending: 1,
  approved: 2,
  planned: 3,
  dispatched: 4,
  cancelled: 2,
};

type Filter = "all" | "drafts" | "office" | "live" | "done";

const FILTERS: { key: Filter; label: string; match: (o: SalesOrderRow) => boolean }[] = [
  { key: "all", label: "All", match: () => true },
  { key: "drafts", label: "Drafts", match: (o) => o.status === "draft" || o.status === "returned" },
  { key: "office", label: "With office", match: (o) => o.status === "pending" },
  { key: "live", label: "Live", match: (o) => o.status === "approved" || o.status === "planned" },
  { key: "done", label: "Finished", match: (o) => o.status === "dispatched" || o.status === "cancelled" },
];

// What happens next, in a line — under the tracker and in the action bar.
function nextStep(o: SalesOrderRow): string {
  switch (o.status) {
    case "draft":
      return "Only you can see this order until you send it to the office.";
    case "returned":
      return "The office sent this back. Change it and resend.";
    case "pending":
      return o.dateSent ? `With the office since ${ago(o.dateSent)}.` : "With the office.";
    case "approved":
      return "Approved. Waiting for transport to be allocated.";
    case "planned":
      return `Transport allocated for ${dayLabel(o.loadDate)}${o.loadHaulier ? " with " + titleCase(o.loadHaulier) : ""}.`;
    case "dispatched":
      return `Dispatched ${dayLabel(o.dateDispatched)}.`;
    case "cancelled":
      return "The office cancelled this order.";
  }
}

export default function MyOrdersPage(_props: { user: AdminUserDto }) {
  const [orders, setOrders] = useState<SalesOrderRow[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<Filter>("all");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [detail, setDetail] = useState<SalesOrderDetail | null>(null);
  const [editing, setEditing] = useState<SalesOrderRow | null | "new">(null);
  const [busy, setBusy] = useState(false);
  // History entries opened to show their changes (keyed by time + text).
  const [openEvents, setOpenEvents] = useState<Set<string>>(new Set());
  function toggleEvent(key: string) {
    setOpenEvents((cur) => {
      const next = new Set(cur);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }
  // Completed > View Order: the order's detail panel, flown in over the card as on Orders.
  const [viewingOrder, setViewingOrder] = useState<DispatchOrder | null>(null);
  // View Load Detail: the load the selected order is on, flown in the same way.
  const [viewingLoad, setViewingLoad] = useState<{ id: number; pre: boolean } | null>(null);
  // List (+ tracker), Board or Completed, chosen top right and remembered on this PC.
  const [view, setView] = useState<View>(() => {
    try {
      const v = localStorage.getItem(VIEW_KEY);
      return v === "board" || v === "completed" ? v : "list";
    } catch {
      return "list";
    }
  });
  function changeView(v: View) {
    setView(v);
    try {
      localStorage.setItem(VIEW_KEY, v);
    } catch {
      /* not remembered; the view still changes */
    }
  }
  // A board card opens its order in the List view, whatever filter was last in use there.
  function openInList(o: SalesOrderRow) {
    setFilter("all");
    setSelectedId(o.id);
    changeView("list");
  }

  // Reloads the list; `select` picks an order afterwards (a new or copied draft).
  const refresh = useCallback(async (select?: "newest" | number) => {
    try {
      const rows = await getSalesOrders();
      setOrders(rows);
      if (select === "newest" && rows.length) setSelectedId(Math.max(...rows.map((r) => r.id)));
      else if (typeof select === "number") setSelectedId(select);
      else setSelectedId((cur) => (cur != null && rows.some((r) => r.id === cur) ? cur : rows[0]?.id ?? null));
    } catch (err) {
      void messageDialog("Failed to load your orders.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
    getProducts()
      .then(setProducts)
      .catch(() => setProducts([]));
    // The office moves orders on (approves, returns, loads, dispatches): keep the list current.
    const t = window.setInterval(() => void refresh(), 60000);
    return () => window.clearInterval(t);
  }, [refresh]);

  const selected = orders.find((o) => o.id === selectedId) ?? null;

  // The history comes with the detail call; re-read whenever the order itself changes.
  useEffect(() => {
    if (!selected) {
      setDetail(null);
      return;
    }
    let cancelled = false;
    getSalesOrder(selected.id)
      .then((d) => !cancelled && setDetail(d))
      .catch(() => !cancelled && setDetail(null));
    return () => {
      cancelled = true;
    };
  }, [selected]);

  const productOf = useCallback((id: number) => products.find((p) => p.id === id), [products]);
  const pallets = (o: SalesOrderRow) => o.items.reduce((n, i) => n + (i.quantity ?? 0), 0);
  const lineTotal = (i: OrderItemInput) =>
    i.quantity != null && i.price != null ? i.quantity * priceUnitsOf(productOf(i.productId)) * i.price : 0;
  const value = (o: SalesOrderRow) => o.items.reduce((n, i) => n + lineTotal(i), 0);

  const term = search.trim().toLowerCase();
  // The Board narrows by the search alone: its columns are the stages the chips pick.
  const searched = useMemo(
    () =>
      !term
        ? orders
        : orders.filter((o) =>
            [o.customerName, o.poNumber, o.ref, o.orderNo != null ? String(o.orderNo) : null].some((v) =>
              v?.toLowerCase().includes(term),
            ),
          ),
    [orders, term],
  );

  const visible = useMemo(() => {
    const f = FILTERS.find((x) => x.key === filter)!;
    return orders.filter(
      (o) =>
        f.match(o) &&
        (!term ||
          [o.customerName, o.poNumber, o.ref, o.orderNo != null ? String(o.orderNo) : null].some((v) =>
            v?.toLowerCase().includes(term),
          )),
    );
  }, [orders, filter, term]);

  async function act(run: () => Promise<unknown>, after?: "newest" | number) {
    setBusy(true);
    try {
      await run();
      await refresh(after);
    } catch (err) {
      void messageDialog((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function onDelete(o: SalesOrderRow) {
    if (!(await confirmDialog(`Delete the draft for ${o.customerName ?? "this customer"}?`))) return;
    await act(() => deleteSalesOrder(o.id));
  }

  // Sending hands the order to the office, so it is asked first, with what is going.
  async function onSend(o: SalesOrderRow) {
    const what = `${o.customerName ?? "this customer"} (${o.ref}): ${pallets(o)} pallets, ${gbp(value(o))}`;
    const question =
      o.status === "returned"
        ? `Resend this order to the office?\n\n${what}`
        : `Send this order to the office for approval?\n\n${what}\n\nYou can take it back until the office approves it.`;
    if (!(await confirmDialog(question))) return;
    await act(() => sendSalesOrder(o.id));
  }

  async function onRecall(o: SalesOrderRow) {
    if (!(await confirmDialog("Take this order back from the office? It goes back to being a draft."))) return;
    await act(() => recallSalesOrder(o.id));
  }

  // A new draft with the same customer, address, comments and lines; the delivery date and PO
  // are left for the new order.
  // DEV ONLY ("Add Order" at the foot of the list): a filled-in draft in one click — a random
  // customer (one with a costing zone where there are any) and its first address, 2–3 finished
  // products at 1–4 pallets priced from the zone, ASAP or a date in the next fortnight, a DEV PO.
  const [devAdding, setDevAdding] = useState(false);
  async function onDevAddOrder() {
    setDevAdding(true);
    try {
      const rand = (n: number) => Math.floor(Math.random() * n);
      // A Sales user only orders for their own customers (Customers grid, Sales Rep).
      const all = await getCustomersSuppliers(1);
      const customers = hasRole(_props.user, UserRole.Sales) ? all.filter((c) => c.salesRepId === _props.user.id) : all;
      const zoned = customers.filter((c) => (c.costingZoneId ?? 0) > 0);
      const pickFrom = zoned.length > 0 ? zoned : customers;
      if (pickFrom.length === 0) {
        void messageDialog("Need at least one customer.");
        return;
      }
      const customer = pickFrom[rand(pickFrom.length)];
      const [addresses, prices] = await Promise.all([
        getAddresses(customer.id),
        (customer.costingZoneId ?? 0) > 0 ? getZonePrices(customer.costingZoneId!) : Promise.resolve([]),
      ]);
      const priceOf: Record<number, number> = {};
      prices.forEach((z) => (priceOf[z.productId] = z.price));
      const finished = products.filter((p) => p.productTypeId === FINISHED_PRODUCT_TYPE_ID && !p.dateDeleted);
      const priced = finished.filter((p) => priceOf[p.id] != null);
      const pool = [...(priced.length >= 2 ? priced : finished)];
      const items: OrderItemInput[] = [];
      for (let n = Math.min(2 + rand(2), pool.length); n > 0; n--) {
        const p = pool.splice(rand(pool.length), 1)[0];
        items.push({ productId: p.id, quantity: 1 + rand(4), price: priceOf[p.id] ?? 1 });
      }
      const when = rand(3) === 0 ? null : new Date(Date.now() + (1 + rand(14)) * 86400000);
      await createSalesOrder({
        customerId: customer.id,
        addressId: addresses[0]?.id ?? 0,
        deliveryDate: when ? new Date(when.getFullYear(), when.getMonth(), when.getDate()).toISOString() : null,
        poNumber: `DEV-${1000 + rand(9000)}`,
        comments: "Dev quick-add",
        pickingComments: null,
        hasPos: false,
        items,
      });
      setFilter("all");
      await refresh("newest");
    } catch (err) {
      void messageDialog("Failed to add order.\n" + (err as Error).message);
    } finally {
      setDevAdding(false);
    }
  }

  async function onCopy(o: SalesOrderRow) {
    await act(async () => {
      await createSalesOrder({
        customerId: o.customerId,
        addressId: o.addressId,
        deliveryDate: null,
        poNumber: null,
        comments: o.comments,
        pickingComments: o.pickingComments,
        hasPos: o.hasPos,
        copiedFromId: o.id,
        items: o.items,
      });
      setFilter("all");
    }, "newest");
  }

  const lineColumns: Column<OrderItemInput>[] = [
    {
      key: "product",
      header: "Product",
      render: (i) => productOf(i.productId)?.description ?? `#${i.productId}`,
      footer: () => <b>Total</b>,
    },
    { key: "size", header: "Size", width: 60, align: "right", render: (i) => productOf(i.productId)?.size ?? "" },
    {
      key: "quantity",
      header: "Pallets",
      width: 80,
      align: "right",
      render: (i) => i.quantity ?? "",
      footer: (rows) => <b>{rows.reduce((n, i) => n + (i.quantity ?? 0), 0)}</b>,
    },
    { key: "price", header: "Price", width: 80, align: "right", render: (i) => (i.price != null ? i.price.toFixed(2) : "") },
    {
      key: "total",
      header: "Total",
      width: 100,
      align: "right",
      render: (i) => (i.quantity != null && i.price != null ? gbp(lineTotal(i)) : ""),
      footer: (rows) => <b>{gbp(rows.reduce((n, i) => n + lineTotal(i), 0))}</b>,
    },
  ];

  // The dates under each step of the tracker, where the order has them.
  function stepNote(o: SalesOrderRow, k: number): string {
    switch (k) {
      case 0:
        return fmtDateTime(o.dateCreated);
      case 1:
        if (o.fromOffice) return "Entered by the office";
        if (o.status === "returned") return "Sent back to you";
        return o.dateSent ? fmtDateTime(o.dateSent) : "";
      case 2:
        if (o.status === "cancelled") return "Cancelled";
        return o.dateApproved ? `${fmtDateTime(o.dateApproved)}${o.orderNo ? " · No " + o.orderNo : ""}` : "";
      case 3:
        return o.loadDate ? `${dayLabel(o.loadDate)}${o.loadHaulier ? " · " + titleCase(o.loadHaulier) : ""}` : "";
      case 4:
        return o.dateDispatched ? fmtDateTime(o.dateDispatched) : "";
    }
    return "";
  }

  function stepClass(o: SalesOrderRow, k: number): string {
    const at = STEP_OF[o.status];
    if (o.status === "dispatched" || k < at) return "mo-step done";
    if (k > at) return "mo-step";
    if (o.status === "returned" || o.status === "cancelled") return "mo-step bad";
    if (o.status === "pending") return "mo-step wait";
    return "mo-step cur";
  }

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader
          title="My Orders"
          meta={`${orders.length} ${orders.length === 1 ? "order" : "orders"}`}
        />
        <PageTabs tabs={VIEW_TABS} active={view} onChange={changeView} />
        <div className={cc.body}>
          {view === "completed" ? (
            <CompletedOrdersPage user={_props.user} mine onViewOrder={setViewingOrder} />
          ) : view === "board" ? (
            <div className="mob-wrap">
              <div className="mob-tools">
                <input
                  className="form-input mo-search"
                  placeholder="Search customer, PO or order no"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                />
                <span className="mob-tools-sp" />
                {/* DEV ONLY, as at the foot of the List view's orders. */}
                {import.meta.env.DEV && (
                  <button
                    type="button"
                    className="btn-dev"
                    disabled={devAdding}
                    title="Add a filled-in draft in one click"
                    onClick={() => void onDevAddOrder()}
                  >
                    {devAdding ? "Adding…" : "Add Order"}
                  </button>
                )}
                <button type="button" className="btn-blue mo-new" onClick={() => setEditing("new")}>
                  + New Order
                </button>
              </div>
              {loading ? (
                <div className="mo-empty">Loading…</div>
              ) : (
                <MyOrdersBoard
                  orders={searched}
                  term={term}
                  pallets={pallets}
                  value={value}
                  busy={busy}
                  onOpen={openInList}
                  onSend={(o) => void onSend(o)}
                />
              )}
            </div>
          ) : (
          <>
          {/* Left: the orders, filtered by where they have got to. */}
          <div className={`${cc.listCol} mo-list`}>
            <div className="mo-list-tools">
              <input
                className="form-input mo-search"
                placeholder="Search customer, PO or order no"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
              <button type="button" className="btn-blue mo-new" onClick={() => setEditing("new")}>
                + New Order
              </button>
            </div>
            <div className="mo-chips">
              {FILTERS.map((f) => (
                <button
                  key={f.key}
                  type="button"
                  className={`mo-chip${filter === f.key ? " on" : ""}`}
                  onClick={() => setFilter(f.key)}
                >
                  {f.label}
                  <b>{orders.filter(f.match).length}</b>
                </button>
              ))}
            </div>
            <div className="mo-items">
              {loading ? (
                <div className="mo-empty">Loading…</div>
              ) : visible.length === 0 ? (
                <div className="mo-empty">
                  {orders.length === 0 ? "No orders yet. + New Order starts one." : "Nothing in this view."}
                </div>
              ) : (
                visible.map((o) => (
                  <button
                    key={o.id}
                    type="button"
                    className={`mo-li${o.id === selectedId ? " on" : ""}`}
                    onClick={() => setSelectedId(o.id)}
                  >
                    <span className="mo-li-cust">
                      <Highlight text={o.customerName ?? "—"} term={term} />
                    </span>
                    <StatusPill status={o.status} />
                    <span className="mo-li-meta">
                      {o.orderNo ? <b>{o.orderNo} · </b> : null}
                      {o.ref} · {pallets(o)} pallets · {gbp(value(o))}
                    </span>
                    {/* A dispatched order shows the day it went out; anything else the delivery it
                        is for ("ASAP" when none was asked for). */}
                    {o.status === "dispatched" && o.dateDispatched ? (
                      <span className="mo-li-meta mo-li-date" title="Dispatched">
                        {dayLabel(o.dateDispatched)}
                      </span>
                    ) : (
                      <span className="mo-li-meta mo-li-date" title="Delivery">
                        {dayLabel(o.deliveryDate)}
                      </span>
                    )}
                  </button>
                ))
              )}
            </div>
            {/* DEV ONLY: a section at the foot of the list it adds to, as on the Dispatch page.
                Compiled out of a release build (import.meta.env.DEV is a literal false there). */}
            {import.meta.env.DEV && (
              <div className="dsp-dev-section">
                <span className="dsp-dev-label">Dev</span>
                <button
                  type="button"
                  className="btn-dev"
                  disabled={devAdding}
                  title="Add a filled-in draft in one click"
                  onClick={() => void onDevAddOrder()}
                >
                  {devAdding ? "Adding…" : "Add Order"}
                </button>
              </div>
            )}
          </div>

          {/* Right: the picked order. */}
          <div className={`${cc.mainCol} mo-detail`}>
            {!selected ? (
              <div className="mo-empty mo-empty-big">
                {orders.length === 0 ? "Start your first order with + New Order." : "Pick an order on the left."}
              </div>
            ) : (
              <>
                {/* The order's header and tracker across the top; below them the detail on the
                    left (scrolls) and a fixed-width panel of actions on the right (stays put). */}
                <div className="mo-top">
                  <div className="mo-head">
                    <div className="mo-head-main">
                      {/* The order's numbers first, on the left where the eye starts. */}
                      <div className="mo-head-ref">
                        {selected.orderNo != null && (
                          <span>
                            Order No <b>{selected.orderNo}</b>
                          </span>
                        )}
                        {selected.fromOffice ? (
                          <span>Entered by the office</span>
                        ) : (
                          <span>
                            Ref <b>{selected.ref}</b>
                          </span>
                        )}
                      </div>
                      <h3>{selected.customerName ?? "—"}</h3>
                      <div className="mo-head-sub">
                        {selected.addressText ? selected.addressText.split("\n").slice(0, 3).join(", ") : "No address"}
                      </div>
                      <div className="mo-head-sub">
                        Delivery <b>{dayLabel(selected.deliveryDate)}</b> · PO <b>{selected.poNumber || "—"}</b>
                        {selected.hasPos && <span className="mo-pos">POS board</span>}
                      </div>
                    </div>
                  </div>

                  <div className="mo-stepper">
                    {STEPS.map((label, k) => (
                      <div key={label} className={stepClass(selected, k)}>
                        <span className="mo-dot" />
                        <span className="mo-step-name">
                          {k === 1 && selected.status === "returned"
                            ? "Returned"
                            : k === 2 && selected.status === "cancelled"
                              ? "Cancelled"
                              : label}
                        </span>
                        <span className="mo-step-note">{stepNote(selected, k)}</span>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="mo-split">
                  <div className="mo-scroll">
                    {selected.status === "returned" && (
                      <div className="mo-sec">
                        <div className="mo-note">
                          <b>Returned by the office:</b> {selected.officeNote || "No reason given."}
                        </div>
                      </div>
                    )}

                    <div className="mo-sec">
                      <h4 className="mo-sec-title">Lines</h4>
                      <DataTable
                        columns={lineColumns}
                        rows={selected.items}
                        rowKey={(i) => `${i.productId}-${selected.items.indexOf(i)}`}
                        emptyText={selected.hasPos ? "No product lines. Just the POS board." : "No lines yet. Edit the order to add some."}
                      />
                    </div>

                    {(selected.comments || selected.pickingComments) && (
                      <div className="mo-sec mo-comments">
                        {selected.comments && (
                          <div>
                            <h4 className="mo-sec-title">Delivery note comments</h4>
                            <p>{selected.comments}</p>
                          </div>
                        )}
                        {selected.pickingComments && (
                          <div>
                            <h4 className="mo-sec-title">Picking comments</h4>
                            <p>{selected.pickingComments}</p>
                          </div>
                        )}
                      </div>
                    )}

                    <div className="mo-sec">
                      <h4 className="mo-sec-title">History</h4>
                      <ul className="mo-hist">
                        {(detail?.order.id === selected.id ? detail.events : [])
                          .slice()
                          .reverse()
                          .map((e) => {
                            // An edit keeps what it changed, folded away until its line is clicked.
                            const key = `${e.date}|${e.text}`;
                            const open = openEvents.has(key);
                            return (
                              <li key={key}>
                                <span className="mo-hist-t">{fmtDateTime(e.date)}</span>
                                <span>
                                  <b>{e.who}</b> ·{" "}
                                  {e.changes?.length ? (
                                    <button
                                      type="button"
                                      className="mo-hist-link"
                                      aria-expanded={open}
                                      onClick={() => toggleEvent(key)}
                                    >
                                      {e.text}
                                      <span className="mo-hist-count">
                                        {e.changes.length} {e.changes.length === 1 ? "change" : "changes"}
                                      </span>
                                      <span className={`mo-hist-caret${open ? " open" : ""}`}>▸</span>
                                    </button>
                                  ) : (
                                    e.text
                                  )}
                                  {open && e.changes && (
                                    <ul className="mo-hist-changes">
                                      {e.changes.map((c, k) => (
                                        <li key={k}>{c}</li>
                                      ))}
                                    </ul>
                                  )}
                                </span>
                              </li>
                            );
                          })}
                      </ul>
                    </div>
                  </div>

                  {/* The actions that fit where the order is: the order in two figures, the next
                      step in words, then the buttons, the main one first. */}
                  <aside className="mo-side">
                    <div className="mo-figs">
                      <div>
                        <span>Pallets</span>
                        <b>{pallets(selected)}</b>
                      </div>
                      <div>
                        <span>Value</span>
                        <b>{gbp(value(selected))}</b>
                      </div>
                    </div>
                    <h4 className="mo-sec-title">Next step</h4>
                    <p className="mo-side-note">{nextStep(selected)}</p>
                    <div className="mo-side-btns">
                      {isDraft(selected) ? (
                        <>
                          <button
                            type="button"
                            className="mo-send"
                            disabled={busy}
                            onClick={() => void onSend(selected)}
                          >
                            {selected.status === "returned" ? "Resend to office" : "Send to office for approval"}
                          </button>
                          <button type="button" className="toolbar-btn" disabled={busy} onClick={() => setEditing(selected)}>
                            Edit order
                          </button>
                          <button type="button" className="toolbar-btn mo-danger" disabled={busy} onClick={() => void onDelete(selected)}>
                            Delete draft
                          </button>
                        </>
                      ) : selected.status === "pending" ? (
                        <button type="button" className="toolbar-btn" disabled={busy} onClick={() => void onRecall(selected)}>
                          Take back to draft
                        </button>
                      ) : (
                        <>
                          <button
                            type="button"
                            className="toolbar-btn"
                            disabled={!selected.loadId}
                            title={selected.loadId ? undefined : "Not on a load yet"}
                            onClick={() =>
                              selected.loadId &&
                              setViewingLoad({ id: selected.loadId, pre: selected.status !== "dispatched" })
                            }
                          >
                            View Load Detail
                          </button>
                          {SHOW_COPY_AS_NEW && (
                            <button type="button" className="toolbar-btn" disabled={busy} onClick={() => void onCopy(selected)}>
                              Copy as new order
                            </button>
                          )}
                        </>
                      )}
                    </div>
                  </aside>
                </div>
              </>
            )}
          </div>
          </>
          )}
        </div>
      </div>

      {editing && (
        <DispatchOrderEditModal
          existing={null}
          sales={{ order: editing === "new" ? null : editing }}
          onClose={(saved) => {
            const wasNew = editing === "new";
            setEditing(null);
            if (saved) void refresh(wasNew ? "newest" : undefined);
          }}
        />
      )}
      {viewingOrder && <OrderDetailPanel order={viewingOrder} onClose={() => setViewingOrder(null)} />}
      {viewingLoad && (
        <LoadDetailPanel loadId={viewingLoad.id} preCommercial={viewingLoad.pre} onClose={() => setViewingLoad(null)} />
      )}
    </div>
  );
}
