import { SalesOrderRow, SalesOrderStatus } from "../../api/salesOrders";

// The pieces My Orders' two views (MyOrdersPage's list + tracker, MyOrdersBoard) both use.

export const STATUS_LABEL: Record<SalesOrderStatus, string> = {
  draft: "Draft",
  returned: "Returned",
  pending: "Awaiting approval",
  approved: "Approved",
  planned: "Transport allocated",
  dispatched: "Dispatched",
  cancelled: "Cancelled",
};

// Still the salesperson's to change: a draft, or one the office sent back.
export const isDraft = (o: SalesOrderRow) => o.status === "draft" || o.status === "returned";

const DOW = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const MON = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
// "Wed 8 Oct" — the way the office talks about a delivery or a load.
export function dayLabel(iso: string | null): string {
  if (!iso) return "ASAP";
  const d = new Date(iso);
  return `${DOW[d.getDay()]} ${d.getDate()} ${MON[d.getMonth()]}`;
}

export function ago(iso: string): string {
  const mins = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins} min ago`;
  const h = Math.round(mins / 60);
  if (h < 24) return `${h} h ago`;
  const d = Math.round(h / 24);
  return d === 1 ? "yesterday" : `${d} days ago`;
}

export const gbp = (n: number) =>
  "£" + n.toLocaleString("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function titleCase(s: string | null): string {
  return (s ?? "").toLowerCase().replace(/\b\w/g, (c) => c.toUpperCase());
}

export function StatusPill({ status }: { status: SalesOrderStatus }) {
  return (
    <span className={`mo-pill mo-st-${status}`}>
      <i />
      {STATUS_LABEL[status]}
    </span>
  );
}
