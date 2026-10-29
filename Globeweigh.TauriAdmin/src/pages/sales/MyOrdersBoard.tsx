import { SalesOrderRow } from "../../api/salesOrders";
import Highlight from "../../components/Highlight";
import { StatusPill, ago, dayLabel, gbp, isDraft, titleCase } from "./myOrdersShared";

// My Orders, Board view (design B of the 2026-10-05 mock-ups): one column per stage, Drafts on
// the left to Dispatched on the right, so an order is seen to move across as the office acts
// on it. A card opens the order in the List view (the tracker, lines and history); a draft's
// card carries Send so the commonest step needs no detour.

interface Props {
  orders: SalesOrderRow[]; // already narrowed by the search box
  term: string;
  pallets: (o: SalesOrderRow) => number;
  value: (o: SalesOrderRow) => number;
  busy: boolean;
  onOpen: (o: SalesOrderRow) => void;
  onSend: (o: SalesOrderRow) => void;
}

// Finished orders stay on the board this long, so the last column does not grow for ever.
const DONE_DAYS = 30;

interface BoardColumn {
  key: string;
  label: string;
  swatch: string;
  match: (o: SalesOrderRow) => boolean;
  order: (a: SalesOrderRow, b: SalesOrderRow) => number;
  empty: string;
}

const time = (iso: string | null) => (iso ? new Date(iso).getTime() : 0);

const COLUMNS: BoardColumn[] = [
  {
    key: "drafts",
    label: "Drafts",
    swatch: "#94a3b8",
    match: isDraft,
    // Returned ones first: the office is waiting on them.
    order: (a, b) => Number(b.status === "returned") - Number(a.status === "returned") || b.id - a.id,
    empty: "No drafts. + New Order starts one.",
  },
  {
    key: "pending",
    label: "Awaiting approval",
    swatch: "#e0a526",
    match: (o) => o.status === "pending",
    order: (a, b) => time(a.dateSent) - time(b.dateSent), // longest waiting first
    empty: "Nothing with the office.",
  },
  {
    key: "approved",
    label: "Approved",
    swatch: "#3b82f6",
    match: (o) => o.status === "approved",
    order: (a, b) => time(a.deliveryDate) - time(b.deliveryDate),
    empty: "None waiting for a load.",
  },
  {
    key: "planned",
    label: "Transport allocated",
    swatch: "#7c5cd6",
    match: (o) => o.status === "planned",
    order: (a, b) => time(a.loadDate) - time(b.loadDate), // soonest load first
    empty: "None with transport allocated.",
  },
  {
    key: "done",
    label: "Dispatched",
    swatch: "#16a34a",
    match: (o) =>
      (o.status === "dispatched" || o.status === "cancelled") &&
      Date.now() - time(o.dateDispatched ?? o.dateApproved) < DONE_DAYS * 86400000,
    order: (a, b) => time(b.dateDispatched) - time(a.dateDispatched), // latest first
    empty: `Nothing dispatched in the last ${DONE_DAYS} days.`,
  },
];

export default function MyOrdersBoard({ orders, term, pallets, value, busy, onOpen, onSend }: Props) {
  return (
    <div className="mob-board">
      {COLUMNS.map((c) => {
        const list = orders.filter(c.match).sort(c.order);
        return (
          <section key={c.key} className="mob-col">
            <header className="mob-colh" style={{ borderBottomColor: c.swatch }}>
              <span className="mob-sw" style={{ background: c.swatch }} />
              <span className="mob-colt">{c.label}</span>
              <span className="mob-ct">{list.length}</span>
              <span className="mob-colv">
                {list.reduce((n, o) => n + pallets(o), 0)} pallets · {gbp(list.reduce((n, o) => n + value(o), 0))}
              </span>
            </header>
            <div className="mob-cards">
              {list.length === 0 ? (
                <div className="mob-empty">{c.empty}</div>
              ) : (
                list.map((o) => (
                  <div
                    key={o.id}
                    className={`mob-card${o.status === "returned" ? " ret" : ""}${o.status === "cancelled" ? " can" : ""}`}
                    role="button"
                    tabIndex={0}
                    title="Open in the List view"
                    onClick={() => onOpen(o)}
                    onKeyDown={(e) => e.key === "Enter" && onOpen(o)}
                  >
                    <div className="mob-top">
                      <span>
                        {o.orderNo != null && <b>{o.orderNo} · </b>}
                        {o.ref}
                      </span>
                      {o.status === "returned" || o.status === "cancelled" ? (
                        <StatusPill status={o.status} />
                      ) : (
                        <span>{cardAge(o)}</span>
                      )}
                    </div>
                    <div className="mob-cust">
                      <Highlight text={o.customerName ?? "—"} term={term} />
                    </div>
                    <div className="mob-meta">
                      <span>
                        {pallets(o)} pallets · {gbp(value(o))}
                      </span>
                      <span>{dayLabel(o.deliveryDate)}</span>
                    </div>
                    {o.status === "returned" && <div className="mob-note">{o.officeNote || "No reason given."}</div>}
                    <div className="mob-foot">
                      <span className="mob-foot-txt">{footText(o)}</span>
                      {isDraft(o) && (
                        <button
                          type="button"
                          className="mob-send"
                          disabled={busy}
                          onClick={(e) => {
                            e.stopPropagation();
                            onSend(o);
                          }}
                        >
                          {o.status === "returned" ? "Resend" : "Send to office"}
                        </button>
                      )}
                    </div>
                  </div>
                ))
              )}
            </div>
          </section>
        );
      })}
    </div>
  );
}

// The card's corner: how long it has sat where it is.
function cardAge(o: SalesOrderRow): string {
  switch (o.status) {
    case "pending":
      return o.dateSent ? `sent ${ago(o.dateSent)}` : "";
    case "approved":
    case "planned":
      return o.dateApproved ? `approved ${ago(o.dateApproved)}` : "";
    case "dispatched":
      return o.dateDispatched ? ago(o.dateDispatched) : "";
    default:
      return `started ${ago(o.dateCreated)}`;
  }
}

// The card's foot: the one fact that matters next at this stage.
function footText(o: SalesOrderRow): string {
  switch (o.status) {
    case "pending":
      return "With the office";
    case "approved":
      return "Waiting for a load";
    case "planned":
      return `${dayLabel(o.loadDate)}${o.loadHaulier ? " · " + titleCase(o.loadHaulier) : ""}`;
    case "dispatched":
      return `Left ${dayLabel(o.dateDispatched)}`;
    case "cancelled":
      return "Cancelled by the office";
    default:
      return o.poNumber ? `PO ${o.poNumber}` : "No PO";
  }
}
