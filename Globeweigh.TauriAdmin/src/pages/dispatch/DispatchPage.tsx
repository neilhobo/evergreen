import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  DispatchOrder,
  WeighbridgePreLoad,
  WbPending,
  WbCompleted,
  getOpenOrders,
  getLoads,
  getDispatchPending,
  getDispatchCompleted,
  deleteOrder,
  deleteLoad,
  deletePending,
  getPendingLinkedOrders,
  returnPendingToLoads,
  uncompleteLoad,
  getUncompleteCheck,
  LinkedOrder,
  weighIn,
  createOrder,
  createLoad,
  OrderItemInput,
  LoadEmailKind,
} from "../../api/dispatch";
import { getCustomersSuppliers, getAddresses } from "../../api/customersSuppliers";
import { getHauliers } from "../../api/hauliers";
import { getProducts } from "../../api/products";
import { fmtDate, fmtDateTime, fmtQty, todayInput } from "../../format";
import { AdminUserDto } from "../../types";
import DispatchOrderEditModal from "./DispatchOrderEditModal";
import SalesOrdersSection from "./SalesOrdersSection";
import { useRowMenu } from "./useRowMenu";
import CreateLoadModal from "./CreateLoadModal";
import SendLoadEmailModal from "./SendLoadEmailModal";
import EmailedMark from "../../components/EmailedMark";
import ToolbarDate from "../../components/ToolbarDate";
import WeighModal from "./WeighModal";
import LoadDetailPanel from "./LoadDetailPanel";
import WeighbridgeEditModal from "../weighbridge/WeighbridgeEditModal";
import CompletedOrdersPage from "../completed-orders/CompletedOrdersPage";
import CompletedLoadsPage from "./CompletedLoadsPage";
import OrderDetailPanel from "../completed-orders/OrderDetailPanel";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import PageTabs, { PageTab } from "../../widgets/PageTabs";
import "./Dispatch.css";
// io-badge classes (also redefined in Dispatch.css, imported here too to be safe).
import "../weighbridge/Weighbridge.css";

const DISPATCH_TABS: PageTab<"open" | "completed" | "loads">[] = [
  { key: "open", label: "Open Orders" },
  { key: "completed", label: "Completed Orders" },
  { key: "loads", label: "Completed Loads" },
];

// The Monday on or before a date, as yyyy-MM-dd. A load's collectionDate is read as "week
// commencing" (the Loads grid heads that column "Week Comm."), so a date is snapped back to it.
//
// Returned as a date-only STRING, never a Date. toISOString() on a local midnight is an hour
// earlier in British Summer Time — Monday 21st goes out as "2026-09-20T23:00:00Z" and the grid
// then reads it as the Sunday. A date-only string parses as UTC midnight instead, which is the
// same shape the date picker's value has and the same thing CreateLoadModal sends.
function weekCommencing(d: Date): string {
  const m = new Date(d);
  m.setHours(0, 0, 0, 0);
  // getDay() is 0 on Sunday, so shift the week to start on Monday before subtracting.
  m.setDate(m.getDate() - ((m.getDay() + 6) % 7));
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${m.getFullYear()}-${pad(m.getMonth() + 1)}-${pad(m.getDate())}`;
}

// A load is overdue once today is 7 or more days past its week-commencing date, i.e. its
// week has ended and it still hasn't gone. Only the date part of collectionDate is read, so
// the UTC-vs-BST shift described above can't move it a day.
function isLoadOverdue(collectionDate: string | null): boolean {
  if (!collectionDate) return false;
  const [y, m, d] = collectionDate.slice(0, 10).split("-").map(Number);
  if (!y || !m || !d) return false;
  const due = new Date(y, m - 1, d + 7);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return today >= due;
}

// DEV ONLY strip under Open Orders (one-click random order). Hidden for now (2026-10-06) to
// make room for Orders (from sales); set back to import.meta.env.DEV to show it again.
const SHOW_DEV_ADD_ORDER = false;

// Mirrors the WPF DispatchOrderListView: Open Orders (col 1), Loads (col 2),
// Pending over Completed Weighbridge Records (col 3). A second tab shows the
// Completed Orders master/detail.
export default function DispatchPage(_props: { user: AdminUserDto }) {
  const [tab, setTab] = useState<"open" | "completed" | "loads">("open");
  // The completed order whose detail panel is flown in over the card (Completed Orders > View
  // Order). It lives here rather than on that tab because the panel covers the whole card, the
  // page header and its tabs included.
  const [viewingOrder, setViewingOrder] = useState<DispatchOrder | null>(null);
  // ---- open orders ----
  const [orders, setOrders] = useState<DispatchOrder[]>([]);
  const [selected, setSelected] = useState<DispatchOrder | null>(null);
  const [search, setSearch] = useState("");
  const [ordersLoading, setOrdersLoading] = useState(false);
  const [editing, setEditing] = useState<DispatchOrder | null>(null);
  const [adding, setAdding] = useState(false);

  async function refreshOrders() {
    setOrdersLoading(true);
    try {
      setOrders(await getOpenOrders());
      setSelected(null);
    } catch (err) {
      void messageDialog("Failed to load orders.\n" + (err as Error).message);
    } finally {
      setOrdersLoading(false);
    }
  }

  async function onDelete() {
    if (!selected) return;
    if (!(await confirmDialog(`Delete order ${selected.orderNo}?`))) return;
    try {
      await deleteOrder(selected.id);
      await refreshOrders();
    } catch (err) {
      void messageDialog("Failed to delete order.\n" + (err as Error).message);
    }
  }

  // ---- loads ----
  const [loads, setLoads] = useState<WeighbridgePreLoad[]>([]);
  const [loadsLoading, setLoadsLoading] = useState(false);
  const [selectedLoad, setSelectedLoad] = useState<WeighbridgePreLoad | null>(null);
  async function refreshLoads() {
    setLoadsLoading(true);
    try {
      setLoads(await getLoads());
      setSelectedLoad(null);
    } catch (err) {
      void messageDialog("Failed to load loads.\n" + (err as Error).message);
    } finally {
      setLoadsLoading(false);
    }
  }

  // Dev helper: quick-add an order with a random customer + haulier and 2-4
  // random products whose pallet quantities total at most 24, then submit.
  const [devAddingOrder, setDevAddingOrder] = useState(false);
  async function onDevAddOrder() {
    setDevAddingOrder(true);
    try {
      const [customers, hauliers, products] = await Promise.all([
        getCustomersSuppliers(1),
        getHauliers(),
        getProducts(),
      ]);
      if (customers.length === 0 || hauliers.length === 0 || products.length === 0) {
        void messageDialog("Need at least one customer, haulier and product.");
        return;
      }
      const rand = (n: number) => Math.floor(Math.random() * n);
      const customer = customers[rand(customers.length)];
      const haulier = hauliers[rand(hauliers.length)];
      // 2-4 distinct products (fewer if the product list is short).
      const pool = [...products];
      const itemCount = Math.min(2 + rand(3), pool.length);
      let palletsLeft = 24;
      const items: OrderItemInput[] = [];
      for (let i = 0; i < itemCount; i++) {
        const product = pool.splice(rand(pool.length), 1)[0];
        // Leave at least 1 pallet for each remaining line, cap lines at 8.
        const maxQty = Math.min(8, palletsLeft - (itemCount - 1 - i));
        const quantity = 1 + rand(maxQty);
        palletsLeft -= quantity;
        items.push({ productId: product.id, quantity, price: null });
      }
      // Orders now carry a delivery address — use the customer's first, if any.
      const addresses = await getAddresses(customer.id);
      await createOrder({
        customerId: customer.id,
        addressId: addresses[0]?.id ?? 0,
        haulierId: haulier.id,
        deliveryDate: null, // ASAP
        poNumber: `DEV-${1000 + rand(9000)}`,
        comments: "Dev quick-add",
        pickingComments: null,
        hasPos: false,
        vehicleReg: null,
        trailerNo: null,
        items,
      });
      await refreshOrders();
    } catch (err) {
      void messageDialog("Failed to add order.\n" + (err as Error).message);
    } finally {
      setDevAddingOrder(false);
    }
  }

  // Dev helper: build a load out of two unassigned open orders and a random haulier, so the
  // Loads → Pending → weigh-out path can be exercised without setting one up by hand. Left
  // unweighed, which is where Create Load leaves a real one — "Book in" is the next step.
  const [devAddingLoad, setDevAddingLoad] = useState(false);
  async function onDevAddLoad() {
    setDevAddingLoad(true);
    try {
      // Read the orders FRESH rather than trusting the grid behind us: one of them may have been
      // put on a load since the last refresh, and the load would then be built round an order
      // that is already spoken for.
      const [open, hauliers] = await Promise.all([getOpenOrders(), getHauliers()]);
      // The same rule Create Load offers: an order with no weighbridge record is unassigned.
      const free = open.filter((o) => o.weighbridgeRecordId <= 0);
      if (free.length < 2) {
        void messageDialog(
          `A load needs two unassigned orders — ${free.length === 0 ? "none are" : "only 1 is"} free. ` +
            "Add Order makes more.",
        );
        return;
      }
      if (hauliers.length === 0) {
        void messageDialog("Need at least one haulier.");
        return;
      }
      const rand = (n: number) => Math.floor(Math.random() * n);
      // Two DIFFERENT orders: splice out of a copy so the same one cannot be drawn twice.
      const pool = [...free];
      const picked = [pool.splice(rand(pool.length), 1)[0], pool.splice(rand(pool.length), 1)[0]];
      await createLoad({
        haulierId: hauliers[rand(hauliers.length)].id,
        collectionDate: new Date(weekCommencing(new Date())).toISOString(),
        trailerNo: String(10 + rand(90)),
        comments: "Dev quick-add",
        weightIn: null, // not on the bridge yet
        // 1 is the first drop. The order they came out of the pool in is as good as any.
        orders: picked.map((o, i) => ({ orderId: o.id, deliveryOrder: i + 1 })),
      });
      await refreshLoads();
      await refreshOrders();
    } catch (err) {
      void messageDialog("Failed to add load.\n" + (err as Error).message);
    } finally {
      setDevAddingLoad(false);
    }
  }

  // Dev helper: weigh the selected load in at a default 10,000 Kg / trailer 56
  // so it lands in Pending.
  async function onDevBookIn() {
    if (!selectedLoad) return;
    try {
      await weighIn(selectedLoad.id, 10000, "56");
      await refreshLoads();
      await refreshPending();
    } catch (err) {
      void messageDialog("Failed to book in.\n" + (err as Error).message);
    }
  }

  // Deleting a load (from Loads or Pending) releases its orders — tell the user which ones
  // before asking, so it is clear what goes back into the unassigned pool.
  async function confirmDeleteFreeingOrders(id: number, question: string): Promise<boolean> {
    let freed: LinkedOrder[] = [];
    try {
      freed = await getPendingLinkedOrders(id);
    } catch (err) {
      void messageDialog("Failed to read the load's orders.\n" + (err as Error).message);
      return false;
    }
    const list = freed.map((o) => `    ${o.orderNo}  ${o.customerName ?? ""}`.trimEnd()).join("\n");
    return confirmDialog(
      `${question}\n\n` +
        (freed.length === 0
          ? "It has no orders on it."
          : `${freed.length === 1 ? "This order" : `These ${freed.length} orders`} will be unassigned ` +
            `and available for another load:\n\n${list}`),
    );
  }

  // Reached from the row menu only (the toolbar Delete moved into it).
  async function onDeleteLoad(target: WeighbridgePreLoad) {
    if (!(await confirmDeleteFreeingOrders(target.id, `Delete load ${target.idRef ?? target.id}?`))) return;
    try {
      await deleteLoad(target.id);
      await refreshLoads();
      await refreshOrders();
    } catch (err) {
      void messageDialog("Failed to delete load.\n" + (err as Error).message);
    }
  }

  // ---- load / weigh / outgoing / view modals ----
  const [loadModal, setLoadModal] = useState<{ id: number | null } | null>(null);
  // Send Email: the "ready for collection" message from the Loads grid, the dispatch
  // confirmation from the Completed Loads grid, or an order's confirmation from Open Orders
  // (id is then the order's) — the one dialog, told which.
  const [emailLoad, setEmailLoad] = useState<{ id: number; kind: LoadEmailKind } | null>(null);
  const [weighModal, setWeighModal] = useState<{ id: number; mode: "in" | "out" } | null>(null);
  const [viewLoad, setViewLoadId] = useState<number | null>(null);
  // Opened from the Loads grid (not yet in Pending): nothing picked yet, so the panel offers the Pre-Commercial Invoice.
  const [viewLoadPre, setViewLoadPre] = useState(false);
  const setViewLoad = (id: number | null, pre = false) => {
    setViewLoadId(id);
    setViewLoadPre(pre);
  };

  // ---- pending ----
  const [pending, setPending] = useState<WbPending[]>([]);
  const [pendingLoading, setPendingLoading] = useState(false);
  const [selectedPending, setSelectedPending] = useState<WbPending | null>(null);
  async function refreshPending() {
    setPendingLoading(true);
    try {
      setPending(await getDispatchPending());
      setSelectedPending(null);
    } catch (err) {
      void messageDialog("Failed to load pending records.\n" + (err as Error).message);
    } finally {
      setPendingLoading(false);
    }
  }

  // Silent background poll: refresh the pending list without the loading spinner,
  // keep the current selection (by id), and swallow transient errors.
  async function backgroundRefreshPending() {
    try {
      const rows = await getDispatchPending();
      setPending(rows);
      setSelectedPending((cur) => (cur ? rows.find((r) => r.id === cur.id) ?? null : null));
    } catch {
      /* ignore — next tick will retry */
    }
  }

  // Same silent poll for Loads — a load weighed in on the Touch app moves to
  // Pending and must drop off the Loads grid without a manual refresh.
  async function backgroundRefreshLoads() {
    try {
      const rows = await getLoads();
      setLoads(rows);
      setSelectedLoad((cur) => (cur ? rows.find((r) => r.id === cur.id) ?? null : null));
    } catch {
      /* ignore — next tick will retry */
    }
  }

  // ---- per-row "⋮" menus on the Loads, Pending and Completed Loads grids (see useRowMenu) ----
  const [loadMenu, loadMenuApi] = useRowMenu<WeighbridgePreLoad>();
  const [pendingMenu, pendingMenuApi] = useRowMenu<WbPending>();
  const [completedMenu, completedMenuApi] = useRowMenu<WbCompleted>();

  // "Remove from pending": undo the weigh-in so the load steps back to the Loads
  // grid. Its orders stay linked and any picking already done is kept.
  async function onRemoveFromPending(row: WbPending) {
    pendingMenuApi.close();
    const ok = await confirmDialog(
      `Remove load ${row.idRef ?? row.id} from Pending?\n\n` +
        "The in-weight will be cleared and the load will go back to the Loads list. " +
        "Its orders stay on the load.",
    );
    if (!ok) return;
    try {
      await returnPendingToLoads(row.id);
      await refreshPending();
      await refreshLoads();
    } catch (err) {
      void messageDialog("Failed to remove from pending.\n" + (err as Error).message);
    }
  }

  // Reached from the row menu only (the toolbar Delete moved into it).
  async function onDeletePending(target: WbPending) {
    if (!(await confirmDeleteFreeingOrders(target.id, "Delete this pending record?"))) return;
    try {
      await deletePending(target.id);
      await refreshPending();
      await refreshOrders();
    } catch (err) {
      void messageDialog("Failed to delete record.\n" + (err as Error).message);
    }
  }

  // ---- completed weighbridge ----
  const [completed, setCompleted] = useState<WbCompleted[]>([]);
  const [completedLoading, setCompletedLoading] = useState(false);
  const [selectedCompleted, setSelectedCompleted] = useState<WbCompleted | null>(null);
  // The panel shows the past week by default; the Completed Loads tab is for going further back.
  const [completedFrom, setCompletedFrom] = useState(todayInput(-7));
  const [completedTo, setCompletedTo] = useState(todayInput());
  // Completed loads are WeighbridgeRecords — edited with the shared weighbridge modal.
  const [editCompletedId, setEditCompletedId] = useState<number | null>(null);
  async function refreshCompleted() {
    setCompletedLoading(true);
    try {
      setCompleted(await getDispatchCompleted(completedFrom, completedTo));
      setSelectedCompleted(null);
    } catch (err) {
      void messageDialog("Failed to load completed records.\n" + (err as Error).message);
    } finally {
      setCompletedLoading(false);
    }
  }

  // Reached from the row menu (Edit and Delete moved into it; the toolbar keeps Email).
  async function onDeleteCompleted(target: WbCompleted) {
    if (!(await confirmDialog(`Delete completed load ${target.id}?`))) return;
    try {
      // Same soft delete (DateDeleted) as pending records.
      await deletePending(target.id);
      await refreshCompleted();
    } catch (err) {
      void messageDialog("Failed to delete record.\n" + (err as Error).message);
    }
  }

  // "Un-complete": undo the weigh-out so the load steps back to Pending — the out-weight, the
  // orders' dispatch and the stock it took. Picks and photos are kept. Sage cannot be undone
  // here, so any order it has already taken is named before asking.
  async function onUncompleteLoad(target: WbCompleted) {
    let sageSent: number[] = [];
    try {
      sageSent = (await getUncompleteCheck(target.id)).sageSentOrderNos;
    } catch (err) {
      void messageDialog("Failed to check the load.\n" + (err as Error).message);
      return;
    }
    const ok = await confirmDialog(
      `Un-complete load ${target.id}?\n\n` +
        "The load will go back to the Pending list. The weigh-out date/time and weight are " +
        "cleared, its orders go back to undispatched and their pallets go back into stock, " +
        "so pallets can be unpicked and the load weighed out again. The picks and photos are kept." +
        (sageSent.length > 0
          ? `\n\nAlready sent to Sage, and NOT undone there: order${sageSent.length === 1 ? "" : "s"} ` +
            `${sageSent.join(", ")}. Correct ${sageSent.length === 1 ? "it" : "them"} in Sage by hand.`
          : ""),
    );
    if (!ok) return;
    try {
      await uncompleteLoad(target.id);
      await refreshCompleted();
      await refreshPending();
    } catch (err) {
      void messageDialog("Failed to un-complete the load.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    void refreshOrders();
    void refreshLoads();
    void refreshPending();
    void refreshCompleted();
  }, []);

  // Keep the grids live: Pending polls fast (users keep this screen open while
  // picking runs — the progress bars should move near-live); Loads every 10s.
  useEffect(() => {
    const pendingId = setInterval(() => void backgroundRefreshPending(), 3_000);
    const loadsId = setInterval(() => void backgroundRefreshLoads(), 10_000);
    return () => {
      clearInterval(pendingId);
      clearInterval(loadsId);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const term = search.trim().toLowerCase();
  // Ids of the overdue loads (red in the Loads grid); their orders go red in Open Orders too.
  const overdueLoadIds = useMemo(
    () => new Set(loads.filter((l) => isLoadOverdue(l.collectionDate)).map((l) => l.id)),
    [loads],
  );

  const filteredOrders = useMemo(() => {
    if (!term) return orders;
    return orders.filter((r) =>
      [String(r.orderNo), r.customerName, r.haulierName, r.poNumber].some((v) =>
        v?.toLowerCase().includes(term),
      ),
    );
  }, [orders, term]);

  // Best-fit fixed columns; the flexible Customer column absorbs the slack and
  // truncates with an ellipsis, so the grid never scrolls horizontally.
  const orderColumns: Column<DispatchOrder>[] = [
    { key: "orderNo", header: "Order", fit: true, render: (r) => r.orderNo },
    { key: "customerName", header: "Customer", render: (r) => <Highlight text={r.customerName} term={term} /> },
    {
      key: "deliveryDate",
      header: "Delivery",
      fit: true,
      render: (r) => (r.deliveryDate ? fmtDate(r.deliveryDate) : <span className="dsp-asap">ASAP</span>),
    },
    {
      key: "dateCreated",
      header: "Created",
      fit: true,
      render: (r) => <span title={fmtDateTime(r.dateCreated)}>{fmtDate(r.dateCreated)}</span>,
    },
    { key: "assigned", header: "Assigned", fit: true, align: "center", render: (r) => (r.assigned ? "✓" : "") },
    {
      // The order confirmation has gone to the customer (the toolbar's Send Email); the tooltip
      // says when and to whom.
      key: "confirmationSentAt",
      header: "Emailed",
      fit: true,
      align: "center",
      render: (r) => (
        <EmailedMark
          sentAt={r.confirmationSentAt ?? null}
          sentTo={r.confirmationSentTo ?? null}
          label="Order confirmation emailed"
        />
      ),
    },
  ];

  const loadColumns: Column<WeighbridgePreLoad>[] = [
    // Best-fit ID + best-fit trailing columns: the flexible Haulier column absorbs
    // the slack and truncates with an ellipsis, so the grid never scrolls horizontally.
    { key: "idRef", header: "ID", fit: true, render: (r) => r.idRef },
    // no render: the plain-string path also gives the full name as a tooltip when truncated
    { key: "haulierName", header: "Haulier" },
    { key: "collectionDate", header: "Week Comm.", fit: true, render: (r) => fmtDate(r.collectionDate) },
    { key: "orderCount", header: "Orders", fit: true, align: "right", render: (r) => r.orderCount },
    {
      // The load's "ready for collection" email has gone to the haulier (Send Email); the
      // tooltip says when and to whom.
      key: "emailSentAt",
      header: "Emailed",
      fit: true,
      align: "center",
      render: (r) => <EmailedMark sentAt={r.emailSentAt} sentTo={r.emailSentTo} />,
    },
    {
      // Far-right "⋮" — opens the per-row actions menu (see loadMenu).
      key: "menu",
      header: "",
      fit: true,
      align: "center",
      noSort: true,
      render: (r) => (
        <button
          type="button"
          className="dsp-row-menu-btn"
          title="More actions"
          aria-haspopup="menu"
          aria-expanded={loadMenu?.row.id === r.id}
          onMouseDown={(e) => e.stopPropagation()}
          onDoubleClick={(e) => e.stopPropagation()}
          onClick={(e) => {
            e.stopPropagation();
            setSelectedLoad(r);
            loadMenuApi.open(r, e.currentTarget);
          }}
        >
          ⋮
        </button>
      ),
    },
  ];

  const pendingColumns: Column<WbPending>[] = [
    // Same 3-digit ref as the Loads grid (the number keyed in on the touch/picking screens).
    { key: "idRef", header: "ID", fit: true, render: (r) => r.idRef },
    { key: "pendingWeighDate", header: "Weigh Date", width: 130, render: (r) => fmtDateTime(r.pendingWeighDate) },
    { key: "haulierName", header: "Haulier", render: (r) => r.haulierName },
    { key: "trailerNo", header: "Trailer", width: 90, render: (r) => r.trailerNo },
    { key: "weightIn", header: "Weight", width: 70, align: "right", render: (r) => fmtQty(r.weightIn) },
    { key: "orderCount", header: "Orders", width: 56, align: "right", render: (r) => r.orderCount },
    {
      key: "picking",
      header: "Picking",
      width: 110,
      render: (r) => {
        const pct =
          r.totalQuantityOrdered > 0
            ? Math.min(100, Math.round((r.pickedQuantity / r.totalQuantityOrdered) * 100))
            : 0;
        return (
          <div style={{ display: "flex", alignItems: "center", gap: 6 }} title={`${r.pickedQuantity} of ${r.totalQuantityOrdered} pallets picked`}>
            <div
              style={{
                flex: 1,
                height: 8,
                background: "var(--border)",
                borderRadius: 4,
                overflow: "hidden",
              }}
            >
              <div
                style={{
                  width: `${pct}%`,
                  height: "100%",
                  background: pct >= 100 ? "#16a34a" : "var(--primary)",
                }}
              />
            </div>
            <span style={{ fontSize: 10, width: 30, textAlign: "right" }}>{pct}%</span>
          </div>
        );
      },
    },
    {
      // Far-right "⋮" — opens the per-row actions menu (see pendingMenu).
      key: "menu",
      header: "",
      fit: true,
      align: "center",
      noSort: true,
      render: (r) => (
        <button
          type="button"
          className="dsp-row-menu-btn"
          title="More actions"
          aria-haspopup="menu"
          aria-expanded={pendingMenu?.row.id === r.id}
          onMouseDown={(e) => e.stopPropagation()}
          onDoubleClick={(e) => e.stopPropagation()}
          onClick={(e) => {
            e.stopPropagation();
            setSelectedPending(r);
            pendingMenuApi.open(r, e.currentTarget);
          }}
        >
          ⋮
        </button>
      ),
    },
  ];

  const completedColumns: Column<WbCompleted>[] = [
    // The full record number — the Load Ref the load panel shows — not the 3-digit ref the
    // Loads and Pending grids key on.
    { key: "id", header: "Load Ref", fit: true, render: (r) => r.id },
    { key: "haulierName", header: "Haulier", render: (r) => r.haulierName },
    { key: "trailerNo", header: "Trailer No", fit: true, render: (r) => r.trailerNo },
    { key: "weightIn", header: "Weight In", fit: true, align: "right", render: (r) => fmtQty(r.weightIn) },
    { key: "dateWeightOut", header: "Weighed Out", fit: true, render: (r) => fmtDateTime(r.dateWeightOut) },
    { key: "weightOut", header: "Weight Out", fit: true, align: "right", render: (r) => fmtQty(r.weightOut) },
    {
      // The dispatch confirmation has gone to the haulier (the toolbar's Send Email); the
      // "ready for collection" one is history by now and the Loads grid showed it.
      key: "dispatchEmailSentAt",
      header: "Emailed",
      fit: true,
      align: "center",
      render: (r) => (
        <EmailedMark sentAt={r.dispatchEmailSentAt} sentTo={r.dispatchEmailSentTo} label="Dispatch confirmation emailed" />
      ),
    },
    {
      key: "view",
      header: "",
      width: 76,
      render: (r) => (
        <button className="link-btn" onClick={() => setViewLoad(r.id)}>
          View Load
        </button>
      ),
    },
    {
      // Far-right "⋮" — opens the per-row actions menu (see completedMenu).
      key: "menu",
      header: "",
      fit: true,
      align: "center",
      noSort: true,
      render: (r) => (
        <button
          type="button"
          className="dsp-row-menu-btn"
          title="More actions"
          aria-haspopup="menu"
          aria-expanded={completedMenu?.row.id === r.id}
          onMouseDown={(e) => e.stopPropagation()}
          onDoubleClick={(e) => e.stopPropagation()}
          onClick={(e) => {
            e.stopPropagation();
            setSelectedCompleted(r);
            completedMenuApi.open(r, e.currentTarget);
          }}
        >
          ⋮
        </button>
      ),
    },
  ];

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader
          title="Orders / Dispatch"
        />
        <PageTabs tabs={DISPATCH_TABS} active={tab} onChange={setTab} />
        <div className={cc.body}>
      {tab === "completed" ? (
        <CompletedOrdersPage user={_props.user} onViewOrder={setViewingOrder} />
      ) : tab === "loads" ? (
        <CompletedLoadsPage onViewLoad={setViewLoad} />
      ) : (
    <div className="dsp-page">
      {/* col 1: Open Orders */}
      <div className="dsp-panel">
        <div className="dsp-grid-head">
          <h3 className="dsp-panel-title">Open Orders</h3>
          <span className="spacer" />
          {ordersLoading && <span className="toolbar-note">Loading…</span>}
          <button className="btn-blue" onClick={() => setAdding(true)}>
            Add
          </button>
          <button className="toolbar-btn" disabled={!selected} onClick={() => setEditing(selected)}>
            Edit
          </button>
          <button className="toolbar-btn" disabled={!selected} onClick={() => void onDelete()}>
            Delete
          </button>
          <button
            className="toolbar-btn toolbar-btn-icon"
            disabled={!selected}
            title="Send Email — order confirmation: send the customer the order to check, with the order confirmation attached"
            aria-label="Send Email"
            onClick={() => selected && setEmailLoad({ id: selected.id, kind: "confirmation" })}
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
              <rect x="3" y="5" width="18" height="14" rx="2" />
              <path d="M3 7l9 6 9-6" />
            </svg>
          </button>
          <button className="toolbar-btn" onClick={() => void refreshOrders()} disabled={ordersLoading}>
            Refresh
          </button>
        </div>
        <input
          className="form-input toolbar-search"
          placeholder="Search orders…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <DataTable
          columns={orderColumns}
          rows={filteredOrders}
          sortable
          rowKey={(r) => r.id}
          // The orders on the load selected in the Loads grid (order.weighbridgeRecordId is the
          // load's id) — a blue tint, distinct from the orange of the selected order. Orders on
          // an overdue load are red, as the load is; the blue tint wins while its load is selected.
          rowClass={(r) =>
            [
              overdueLoadIds.has(r.weighbridgeRecordId) ? "row-overdue" : "",
              selectedLoad && r.weighbridgeRecordId === selectedLoad.id ? "row-on-load" : "",
            ]
              .filter(Boolean)
              .join(" ") || undefined
          }
          selectedKey={selected?.id ?? null}
          onSelect={(r) => setSelected(r)}
          onDoubleClick={(r) => setEditing(r)}
          emptyText={ordersLoading ? "Loading…" : "No orders"}
        />
        {/* DEV ONLY, and a section of the column rather than a button loose at the foot of it:
            ruled off and given its own padding so it reads as scaffolding and is not pressed
            against the bottom of the screen. The whole branch is compiled out of a release
            build, because import.meta.env.DEV is a literal false there. */}
        {SHOW_DEV_ADD_ORDER && (
          <div className="dsp-dev-section">
            <span className="dsp-dev-label">Dev</span>
            <button
              className="btn-dev"
              disabled={devAddingOrder}
              title="Submit a randomly-filled order in one click"
              onClick={() => void onDevAddOrder()}
            >
              {devAddingOrder ? "Adding…" : "Add Order"}
            </button>
          </div>
        )}
        {/* The orders the sales reps have sent from My Orders, waiting for the office. */}
        <SalesOrdersSection onApproved={() => void refreshOrders()} />
      </div>

      {/* col 2: Loads */}
      <div className="dsp-panel">
        <div className="dsp-grid-head">
          <h3 className="dsp-panel-title">Loads</h3>
          <span className="spacer" />
          {loadsLoading && <span className="toolbar-note">Loading…</span>}
          <button className="btn-blue" onClick={() => setLoadModal({ id: null })}>
            Add
          </button>
          <button
            className="toolbar-btn"
            disabled={!selectedLoad}
            onClick={() => selectedLoad && setLoadModal({ id: selectedLoad.id })}
          >
            Edit
          </button>
          <button
            className="toolbar-btn toolbar-btn-icon"
            disabled={!selectedLoad}
            title="Send Email — tell the haulier this load is ready for collection"
            aria-label="Send Email"
            onClick={() => selectedLoad && setEmailLoad({ id: selectedLoad.id, kind: "ready" })}
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
              <rect x="3" y="5" width="18" height="14" rx="2" />
              <path d="M3 7l9 6 9-6" />
            </svg>
          </button>
          <button className="toolbar-btn" onClick={() => void refreshLoads()} disabled={loadsLoading}>
            Refresh
          </button>
        </div>
        <DataTable
          columns={loadColumns}
          rows={loads}
          rowKey={(r) => r.id}
          rowClass={(r) => (isLoadOverdue(r.collectionDate) ? "row-overdue" : undefined)}
          selectedKey={selectedLoad?.id ?? null}
          onSelect={(r) => setSelectedLoad(r)}
          onDoubleClick={(r) => setViewLoad(r.id, true)}
          emptyText={loadsLoading ? "Loading…" : "No loads"}
        />
        {/* DEV ONLY — the load-side helpers, in the column they act on. */}
        {import.meta.env.DEV && (
          <div className="dsp-dev-section">
            <span className="dsp-dev-label">Dev</span>
            <button
              className="btn-dev"
              disabled={devAddingLoad}
              title="Build a load from two random unassigned open orders"
              onClick={() => void onDevAddLoad()}
            >
              {devAddingLoad ? "Adding…" : "Add Load"}
            </button>
            {/* Disabled rather than hidden, so the section keeps its height and the buttons
                keep their places as the selection changes. */}
            <button
              className="btn-dev"
              disabled={!selectedLoad}
              title={
                selectedLoad
                  ? `Weigh load ${selectedLoad.idRef ?? selectedLoad.id} in at 10,000 Kg, trailer 56`
                  : "Select a load first"
              }
              onClick={() => void onDevBookIn()}
            >
              Book in
            </button>
          </div>
        )}
      </div>

      {/* col 3: Pending (top) over Completed Weighbridge Records (bottom) */}
      <div className="dsp-col-split">
        <div className="dsp-panel">
          <div className="dsp-grid-head">
            <h3 className="dsp-panel-title">Pending</h3>
            <span className="spacer" />
            {pendingLoading && <span className="toolbar-note">Loading…</span>}
            <button
              className="toolbar-btn"
              onClick={() => {
                void refreshPending();
                void refreshCompleted();
              }}
              disabled={pendingLoading}
            >
              Refresh
            </button>
          </div>
          <DataTable
            columns={pendingColumns}
            rows={pending}
            rowKey={(r) => r.id}
            selectedKey={selectedPending?.id ?? null}
            onSelect={(r) => setSelectedPending(r)}
            onDoubleClick={(r) => setViewLoad(r.id)}
            emptyText={pendingLoading ? "Loading…" : "No pending records"}
          />
        </div>

        <div className="dsp-panel">
          <div className="dsp-grid-head">
            <h3 className="dsp-panel-title">Completed Loads</h3>
            <span className="spacer" />
            <ToolbarDate label="From" value={completedFrom} onChange={setCompletedFrom} />
            <ToolbarDate label="To" value={completedTo} onChange={setCompletedTo} />
            {completedLoading && <span className="toolbar-note">Loading…</span>}
            <button
              className="toolbar-btn toolbar-btn-icon"
              disabled={!selectedCompleted}
              title="Send Email — dispatch confirmation: tell the haulier this load is complete and has left"
              aria-label="Send Email"
              onClick={() => selectedCompleted && setEmailLoad({ id: selectedCompleted.id, kind: "dispatched" })}
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                <rect x="3" y="5" width="18" height="14" rx="2" />
                <path d="M3 7l9 6 9-6" />
              </svg>
            </button>
            <button className="toolbar-btn" onClick={() => void refreshCompleted()} disabled={completedLoading}>
              Refresh
            </button>
          </div>
          <DataTable
            columns={completedColumns}
            rows={completed}
            rowKey={(r) => r.id}
            selectedKey={selectedCompleted?.id ?? null}
            onSelect={(r) => setSelectedCompleted(r)}
            onDoubleClick={(r) => setViewLoad(r.id)}
            emptyText={completedLoading ? "Loading…" : "No completed records"}
          />
        </div>
      </div>

      {adding && (
        <DispatchOrderEditModal
          existing={null}
          onClose={(saved) => {
            setAdding(false);
            if (saved) void refreshOrders();
          }}
        />
      )}
      {editing && (
        <DispatchOrderEditModal
          existing={editing}
          onClose={(saved) => {
            setEditing(null);
            if (saved) void refreshOrders();
          }}
        />
      )}

      {loadMenu && (
        <div
          ref={loadMenuApi.ref}
          className="dsp-row-menu"
          role="menu"
          style={{ top: loadMenu.top, right: loadMenu.right }}
        >
          <button
            type="button"
            role="menuitem"
            className="dsp-row-menu-item danger"
            onClick={() => {
              loadMenuApi.close();
              void onDeleteLoad(loadMenu.row);
            }}
          >
            Delete
          </button>
        </div>
      )}

      {completedMenu && (
        <div
          ref={completedMenuApi.ref}
          className="dsp-row-menu"
          role="menu"
          style={{ top: completedMenu.top, right: completedMenu.right }}
        >
          <button
            type="button"
            role="menuitem"
            className="dsp-row-menu-item"
            onClick={() => {
              completedMenuApi.close();
              setEditCompletedId(completedMenu.row.id);
            }}
          >
            Edit Load
          </button>
          <button
            type="button"
            role="menuitem"
            className="dsp-row-menu-item"
            onClick={() => {
              completedMenuApi.close();
              void onUncompleteLoad(completedMenu.row);
            }}
          >
            Un-complete
          </button>
          <button
            type="button"
            role="menuitem"
            className="dsp-row-menu-item danger"
            onClick={() => {
              completedMenuApi.close();
              void onDeleteCompleted(completedMenu.row);
            }}
          >
            Delete
          </button>
        </div>
      )}

      {pendingMenu && (
        <div
          ref={pendingMenuApi.ref}
          className="dsp-row-menu"
          role="menu"
          style={{ top: pendingMenu.top, right: pendingMenu.right }}
        >
          <button
            type="button"
            role="menuitem"
            className="dsp-row-menu-item"
            onClick={() => {
              pendingMenuApi.close();
              setLoadModal({ id: pendingMenu.row.id });
            }}
          >
            Edit Load
          </button>
          <button
            type="button"
            role="menuitem"
            className="dsp-row-menu-item"
            onClick={() => {
              pendingMenuApi.close();
              setWeighModal({ id: pendingMenu.row.id, mode: "out" });
            }}
          >
            Weigh Out
          </button>
          <button
            type="button"
            role="menuitem"
            className="dsp-row-menu-item"
            onClick={() => void onRemoveFromPending(pendingMenu.row)}
          >
            Remove from pending
          </button>
          <button
            type="button"
            role="menuitem"
            className="dsp-row-menu-item danger"
            onClick={() => {
              pendingMenuApi.close();
              void onDeletePending(pendingMenu.row);
            }}
          >
            Delete
          </button>
        </div>
      )}

      {loadModal && (
        <CreateLoadModal
          loadId={loadModal.id}
          onClose={(saved) => {
            setLoadModal(null);
            if (saved) {
              void refreshLoads();
              void refreshPending();
              void refreshOrders();
            }
          }}
        />
      )}

      {weighModal && (
        <WeighModal
          recordId={weighModal.id}
          mode={weighModal.mode}
          onClose={(saved) => {
            setWeighModal(null);
            if (saved) {
              void refreshLoads();
              void refreshPending();
              void refreshCompleted();
            }
          }}
        />
      )}


      {editCompletedId != null && (
        <WeighbridgeEditModal
          id={editCompletedId}
          onClose={(saved) => {
            setEditCompletedId(null);
            if (saved) void refreshCompleted();
          }}
        />
      )}
    </div>
      )}
        </div>
      </div>

      {/* View Order and View Load fly in over the whole card (cc.page is the positioned
          ancestor), so the orders list, the filters and the page tabs are all put aside while one
          record is read. */}
      {viewingOrder && (
        <OrderDetailPanel
          order={viewingOrder}
          onClose={() => setViewingOrder(null)}
          onViewLoad={setViewLoad}
          covered={viewLoad != null}
          allowUnpick
        />
      )}

      {emailLoad != null && (
        <SendLoadEmailModal
          loadId={emailLoad.id}
          kind={emailLoad.kind}
          onClose={(sent) => {
            const { kind } = emailLoad;
            setEmailLoad(null);
            // The grid the load (or order) is on shows the envelope.
            if (sent)
              void (kind === "ready" ? refreshLoads() : kind === "confirmation" ? refreshOrders() : refreshCompleted());
          }}
        />
      )}

      {viewLoad != null && (
        <LoadDetailPanel loadId={viewLoad} preCommercial={viewLoadPre} allowUnpick onClose={() => setViewLoad(null)} />
      )}
    </div>
  );
}
