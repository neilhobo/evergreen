import { Fragment, MouseEvent, useEffect, useMemo, useState } from "react";
import { messageDialog } from "../../components/confirmDialog";
import { getProductionPlanning, PlanningLine, PlanningProduct } from "../../api/production";
import "../../styles/LedgerGrid.css";

// Production Planning — the open order book totalled per product against what is on the
// floor, for the production manager. Two views of the same numbers, switched in the toolbar:
//
// - LEDGER (design mock option 1): one row per product, worst cover at the top; click a row
//   to open the orders behind it beneath it, in the order the stock is consumed (ASAP first,
//   then by delivery date), each marked whether the stock reaches it. Its horizon picker
//   decides which orders count — delivery dates run months ahead in the busy season, so
//   "short" on the whole book can mean short in April.
// - HORIZON (option 3): products down, delivery weeks across, pallets due in each cell.
//   Reading left to right the cells stay green while the stock covers them and turn red at
//   the week it runs out, so an April order sits harmlessly on the right while this week's
//   problems show on the left. Hover a cell for the orders in it.
//
// Both are hand-built tables on the LedgerGrid kit (the app's look since 2026-09-24) rather
// than DataTable, which has no expand rows and no per-cell colouring.

type View = "ledger" | "horizon";
const VIEW_KEY = "productionPlanning.view"; // per-PC convenience, nothing more

// Days ahead an order may be due and still count on the ledger; 0 = every open order.
const HORIZONS: { days: number; label: string }[] = [
  { days: 0, label: "Any time (all open orders)" },
  { days: 14, label: "Next 2 weeks" },
  { days: 28, label: "Next 4 weeks" },
  { days: 56, label: "Next 8 weeks" },
];

// Which products the horizon grid shows.
type HorizonFilter = "all" | "short" | "8";

type Status = "now" | "soon" | "later" | "tight" | "ok";
const STATUS: Record<Status, { cls: string; label: string }> = {
  now: { cls: "bad", label: "Short now" },
  soon: { cls: "warn", label: "Short soon" },
  later: { cls: "later", label: "Short later" },
  tight: { cls: "warn", label: "Tight" },
  ok: { cls: "good", label: "Covered" },
};

// A line with its place in the product's running total: covered = the stock on the floor
// still reaches this order once every earlier one has taken its share.
interface Line extends PlanningLine {
  cumulative: number;
  covered: boolean;
}

interface Row {
  p: PlanningProduct;
  lines: Line[];
  ordered: number;
  picked: number;
  cover: number; // inStock − ordered; negative = the shortfall
  shortfall: number;
  orders: number; // distinct open orders
  neededBy: string | null; // earliest delivery date; null = ASAP
  // undefined = the stock covers every order; null = runs out on an ASAP order; else the
  // delivery date of the first order it cannot fill.
  runsOut: string | null | undefined;
  runsDays: number; // days until it runs out (Infinity when it never does)
  status: Status;
}

const DAY = 86_400_000;

function startOfToday(): Date {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  return d;
}

// Whole days from today to an ISO date (negative = overdue).
function daysFromToday(iso: string): number {
  const d = new Date(iso);
  d.setHours(0, 0, 0, 0);
  return Math.round((d.getTime() - startOfToday().getTime()) / DAY);
}

function compute(products: PlanningProduct[], horizonDays: number): Row[] {
  const rows: Row[] = [];
  for (const p of products) {
    // The API sends lines ASAP-first then by delivery date — the order stock is used up in.
    const inScope = p.lines.filter(
      (l) => horizonDays === 0 || l.deliveryDate == null || daysFromToday(l.deliveryDate) <= horizonDays,
    );
    if (inScope.length === 0) continue;
    let cumulative = 0;
    let runsOut: string | null | undefined;
    const lines: Line[] = inScope.map((l) => {
      cumulative += l.quantity;
      const covered = cumulative <= p.inStock;
      if (!covered && runsOut === undefined) runsOut = l.deliveryDate;
      return { ...l, cumulative, covered };
    });
    const ordered = lines.reduce((s, l) => s + l.quantity, 0);
    const picked = lines.reduce((s, l) => s + l.picked, 0);
    const cover = p.inStock - ordered;
    const runsDays = runsOut === undefined ? Infinity : runsOut === null ? 0 : Math.max(0, daysFromToday(runsOut));
    const status: Status =
      runsOut === undefined
        ? cover < Math.max(3, ordered * 0.15)
          ? "tight"
          : "ok"
        : runsDays <= 14
          ? "now"
          : runsDays <= 56
            ? "soon"
            : "later";
    rows.push({
      p,
      lines,
      ordered,
      picked,
      cover,
      shortfall: Math.max(0, -cover),
      orders: new Set(lines.map((l) => l.orderId)).size,
      neededBy: lines[0].deliveryDate,
      runsOut,
      runsDays,
      status,
    });
  }
  return rows;
}

// Whichever runs out soonest first, then the worst cover, then by name.
const byUrgency = (a: Row, b: Row) =>
  a.runsDays - b.runsDays ||
  a.cover - b.cover ||
  (a.p.description ?? "").localeCompare(b.p.description ?? "") ||
  (a.p.size ?? 0) - (b.p.size ?? 0);

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
// Dates as the mock writes them: "13 Oct", with the year only when it is not this year
// ("1 Apr 27"); no date is ASAP.
function fmtD(when: Date | string | null): string {
  if (when == null) return "ASAP";
  const d = typeof when === "string" ? new Date(when) : when;
  const yr = d.getFullYear() !== new Date().getFullYear() ? ` ${String(d.getFullYear()).slice(2)}` : "";
  return `${d.getDate()} ${MONTHS[d.getMonth()]}${yr}`;
}
const fmtN = (n: number) => n.toLocaleString("en-GB");
const fmtCover = (n: number) => (n < 0 ? `−${fmtN(-n)}` : fmtN(n));
const runsOutText = (r: Row) => (r.runsOut === undefined ? "—" : r.runsOut === null ? "Now" : fmtD(r.runsOut));

// The Monday on or before a date.
function weekStart(d: Date): Date {
  const m = new Date(d);
  m.setHours(0, 0, 0, 0);
  m.setDate(m.getDate() - ((m.getDay() + 6) % 7));
  return m;
}
const addDays = (d: Date, n: number) => new Date(d.getTime() + n * DAY);

// Horizon columns: ASAP/overdue, this week and the seven after it, then Later.
const WEEKS = 8;
const BUCKETS = WEEKS + 2;
function bucketOf(iso: string | null, w0: Date): number {
  if (iso == null) return 0;
  const d = new Date(iso);
  if (d < w0) return 0;
  const i = Math.floor((d.getTime() - w0.getTime()) / (7 * DAY));
  return i < WEEKS ? i + 1 : WEEKS + 1;
}

function readView(): View {
  try {
    return localStorage.getItem(VIEW_KEY) === "horizon" ? "horizon" : "ledger";
  } catch {
    return "ledger";
  }
}

interface Props {
  // The count for the page header's meta slot ("191 open orders").
  onMeta?: (text: string | undefined) => void;
}

export default function ProductionPlanningPanel({ onMeta }: Props) {
  const [products, setProducts] = useState<PlanningProduct[]>([]);
  const [loading, setLoading] = useState(false);
  const [view, setView] = useState<View>(readView);
  const [search, setSearch] = useState("");
  // Ledger: which orders count, short rows only, and the products opened out.
  const [horizon, setHorizon] = useState(0);
  const [shortOnly, setShortOnly] = useState(false);
  const [open, setOpen] = useState<Set<number>>(new Set());
  // Horizon grid: which products, and the cell under the mouse.
  const [filter, setFilter] = useState<HorizonFilter>("all");
  const [tip, setTip] = useState<{ x: number; y: number; productId: number; bucket: number } | null>(null);

  async function refresh() {
    setLoading(true);
    try {
      const data = await getProductionPlanning();
      setProducts(data.products);
    } catch (err) {
      setProducts([]);
      void messageDialog("Failed to load production planning.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  function switchView(v: View) {
    setView(v);
    setTip(null);
    try {
      localStorage.setItem(VIEW_KEY, v);
    } catch {
      // A private window or blocked storage: the switch still works, it just isn't remembered.
    }
  }

  const openOrders = useMemo(() => new Set(products.flatMap((p) => p.lines.map((l) => l.orderId))).size, [products]);
  useEffect(() => {
    onMeta?.(`${fmtN(openOrders)} open orders`);
    return () => onMeta?.(undefined);
  }, [openOrders, onMeta]);

  const term = search.trim().toLowerCase();
  const matches = (r: Row) => !term || [r.p.description, r.p.gtin].some((v) => v?.toLowerCase().includes(term));

  // ── Ledger rows: cut to the horizon, worst cover first ──
  const ledgerRows = useMemo(
    () =>
      compute(products, horizon)
        .filter((r) => !shortOnly || r.cover < 0)
        .filter(matches)
        .sort((a, b) => a.cover - b.cover || byUrgency(a, b)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [products, horizon, shortOnly, term],
  );

  // ── Horizon rows: always the whole book (the weeks ARE the horizon), soonest to run out first ──
  const horizonRows = useMemo(
    () =>
      compute(products, 0)
        .filter((r) => (filter === "all" ? true : filter === "short" ? r.runsOut !== undefined : r.runsDays <= 56))
        .filter(matches)
        .sort(byUrgency),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [products, filter, term],
  );
  const w0 = useMemo(() => weekStart(startOfToday()), []);
  const weeks = useMemo(() => Array.from({ length: WEEKS }, (_, i) => addDays(w0, i * 7)), [w0]);

  function toggle(id: number) {
    setOpen((cur) => {
      const next = new Set(cur);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  // ── Ledger ──
  const ledgerTotals = {
    stock: ledgerRows.reduce((s, r) => s + r.p.inStock, 0),
    ordered: ledgerRows.reduce((s, r) => s + r.ordered, 0),
    picked: ledgerRows.reduce((s, r) => s + r.picked, 0),
    short: ledgerRows.reduce((s, r) => s + r.shortfall, 0),
    shortCount: ledgerRows.filter((r) => r.cover < 0).length,
  };
  const ledgerNote = loading
    ? "Loading…"
    : `${ledgerTotals.shortCount} of ${ledgerRows.length} products short${
        horizon ? ` on orders due in the next ${horizon / 7} weeks` : ""
      } · ${fmtN(ledgerTotals.short)} pallets to make`;

  function renderLedger() {
    return (
      <table className="lg-grid">
        <thead>
          <tr>
            <th>Product</th>
            <th className="r">Size</th>
            <th className="r">In stock</th>
            <th className="r">Ordered</th>
            <th className="r">Picked</th>
            <th className="r">To pick</th>
            <th className="r">Cover</th>
            <th className="r">Orders</th>
            <th>Needed by</th>
            <th>Runs out</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody>
          {ledgerRows.length === 0 && (
            <tr>
              <td className="lg-empty" colSpan={11}>
                {loading ? "Loading…" : shortOnly ? "Nothing short" : "No products on open orders"}
              </td>
            </tr>
          )}
          {ledgerRows.map((r) => {
            const isOpen = open.has(r.p.productId);
            return (
              <Fragment key={r.p.productId}>
                <tr className={isOpen ? "lg-exp open" : "lg-exp"} onClick={() => toggle(r.p.productId)}>
                  <td>
                    {r.p.description} <span className="lg-muted">{r.p.size}L</span>
                  </td>
                  <td className="r">{r.p.size}L</td>
                  <td className="r">{fmtN(r.p.inStock)}</td>
                  <td className="r">{fmtN(r.ordered)}</td>
                  <td className={r.picked ? "r" : "r dim"}>{fmtN(r.picked)}</td>
                  <td className="r">{fmtN(r.ordered - r.picked)}</td>
                  <td className={r.cover < 0 ? "r neg" : "r"}>{fmtCover(r.cover)}</td>
                  <td className="r">{r.orders}</td>
                  <td>{fmtD(r.neededBy)}</td>
                  <td>{r.runsOut === undefined ? <span style={{ color: "#94a3b8" }}>—</span> : runsOutText(r)}</td>
                  <td>
                    <span className={`lg-pill ${STATUS[r.status].cls}`}>{STATUS[r.status].label}</span>
                  </td>
                </tr>
                {isOpen &&
                  r.lines.map((l) => (
                    <tr className="lg-sub" key={`${r.p.productId}-${l.orderId}`}>
                      <td colSpan={3}>
                        {l.orderNo} · {l.customerName}
                      </td>
                      <td className="r">{l.quantity}</td>
                      <td className="r">{l.picked || ""}</td>
                      <td className="r">{l.quantity - l.picked}</td>
                      <td className="r" style={{ color: l.covered ? "#15803d" : "#b91c1c" }}>
                        {l.covered ? "✓ from stock" : "✗ short"}
                      </td>
                      <td className="r" style={{ color: "#94a3b8" }}>
                        cum {l.cumulative}
                      </td>
                      <td colSpan={3}>
                        Due {fmtD(l.deliveryDate)} · placed {fmtD(l.dateCreated)}
                      </td>
                    </tr>
                  ))}
              </Fragment>
            );
          })}
        </tbody>
        <tfoot>
          <tr>
            <td colSpan={2}>{ledgerRows.length} products</td>
            <td className="r">{fmtN(ledgerTotals.stock)}</td>
            <td className="r">{fmtN(ledgerTotals.ordered)}</td>
            <td className="r">{fmtN(ledgerTotals.picked)}</td>
            <td className="r">{fmtN(ledgerTotals.ordered - ledgerTotals.picked)}</td>
            <td className="r neg">{ledgerTotals.short ? `−${fmtN(ledgerTotals.short)}` : ""}</td>
            <td colSpan={3}></td>
            <td>{ledgerTotals.shortCount} short</td>
          </tr>
        </tfoot>
      </table>
    );
  }

  // ── Horizon ──
  // Pallets due per bucket for one product, and the lines behind each.
  function cellsFor(r: Row) {
    const cells = Array.from({ length: BUCKETS }, () => ({ qty: 0, lines: [] as Line[] }));
    for (const l of r.lines) {
      const b = bucketOf(l.deliveryDate, w0);
      cells[b].qty += l.quantity;
      cells[b].lines.push(l);
    }
    return cells;
  }
  const bucketTotals = Array.from({ length: BUCKETS }, () => 0);
  for (const r of horizonRows) for (const l of r.lines) bucketTotals[bucketOf(l.deliveryDate, w0)] += l.quantity;
  const horizonShort = horizonRows.reduce((s, r) => s + r.shortfall, 0);

  function onCellMove(e: MouseEvent<HTMLTableElement>) {
    const td = (e.target as HTMLElement).closest<HTMLElement>("td.wk[data-p]");
    if (!td) {
      if (tip) setTip(null);
      return;
    }
    setTip({ x: e.clientX, y: e.clientY, productId: Number(td.dataset.p), bucket: Number(td.dataset.b) });
  }

  function renderHorizon() {
    return (
      <table className="lg-grid" onMouseMove={onCellMove} onMouseLeave={() => setTip(null)}>
        <thead>
          <tr>
            <th className="name">Product</th>
            <th className="r stk">In stock</th>
            <th className="wk">ASAP</th>
            {weeks.map((w, i) => (
              <th className="wk" key={i}>
                {i === 0 ? "This wk" : fmtD(w)}
              </th>
            ))}
            <th className="wk">Later</th>
            <th className="r">Ordered</th>
            <th className="r">Cover</th>
            <th>Runs out</th>
          </tr>
        </thead>
        <tbody>
          {horizonRows.length === 0 && (
            <tr>
              <td className="lg-empty" colSpan={BUCKETS + 5}>
                {loading ? "Loading…" : filter === "all" ? "No products on open orders" : "Nothing runs out"}
              </td>
            </tr>
          )}
          {horizonRows.map((r) => {
            const cells = cellsFor(r);
            let cumulative = 0;
            let broke = false;
            return (
              <tr key={r.p.productId}>
                <td className="name">
                  {r.p.description} <span className="lg-muted">{r.p.size}L</span>
                </td>
                <td className="r stk">{fmtN(r.p.inStock)}</td>
                {cells.map((c, i) => {
                  if (!c.qty)
                    return (
                      <td className="wk z" key={i}>
                        ·
                      </td>
                    );
                  cumulative += c.qty;
                  let cls = "ok";
                  let short: number | null = null;
                  if (!broke && cumulative > r.p.inStock) {
                    broke = true;
                    cls = "brk";
                    short = cumulative - r.p.inStock;
                  } else if (broke) cls = "sh";
                  return (
                    <td className={`wk ${cls}`} key={i} data-p={r.p.productId} data-b={i}>
                      {c.qty}
                      {short != null && <small>−{fmtN(short)}</small>}
                    </td>
                  );
                })}
                <td className="r">{fmtN(r.ordered)}</td>
                <td className={r.cover < 0 ? "r neg" : "r"}>{fmtCover(r.cover)}</td>
                <td>
                  {r.runsOut === undefined ? (
                    <span style={{ color: "#94a3b8" }}>—</span>
                  ) : r.runsOut === null ? (
                    <b style={{ color: "#b91c1c" }}>Now</b>
                  ) : (
                    fmtD(r.runsOut)
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
        <tfoot>
          <tr>
            <td>{horizonRows.length} products</td>
            <td className="r">{fmtN(horizonRows.reduce((s, r) => s + r.p.inStock, 0))}</td>
            {bucketTotals.map((n, i) => (
              <td className="wk" key={i}>
                {n ? fmtN(n) : ""}
              </td>
            ))}
            <td className="r">{fmtN(horizonRows.reduce((s, r) => s + r.ordered, 0))}</td>
            <td className="r neg">{horizonShort ? `−${fmtN(horizonShort)}` : ""}</td>
            <td>{horizonRows.filter((r) => r.runsOut !== undefined).length} run out</td>
          </tr>
        </tfoot>
      </table>
    );
  }

  // The tip: the orders inside the hovered horizon cell.
  const tipRow = tip ? horizonRows.find((r) => r.p.productId === tip.productId) : undefined;
  const tipLines = tip && tipRow ? tipRow.lines.filter((l) => bucketOf(l.deliveryDate, w0) === tip.bucket) : [];

  return (
    <div className="lg-page">
      <div className="lg-tb">
        <div className="lg-seg" role="group" aria-label="View">
          <button type="button" aria-pressed={view === "ledger"} onClick={() => switchView("ledger")}>
            Ledger
          </button>
          <button type="button" aria-pressed={view === "horizon"} onClick={() => switchView("horizon")}>
            Horizon
          </button>
        </div>
        {view === "ledger" ? (
          <>
            <label>
              Orders due
              <select value={horizon} onChange={(e) => setHorizon(Number(e.target.value))}>
                {HORIZONS.map((h) => (
                  <option key={h.days} value={h.days}>
                    {h.label}
                  </option>
                ))}
              </select>
            </label>
            <label>
              <input type="checkbox" checked={shortOnly} onChange={(e) => setShortOnly(e.target.checked)} /> Short only
            </label>
          </>
        ) : (
          <label>
            Show
            <select value={filter} onChange={(e) => setFilter(e.target.value as HorizonFilter)}>
              <option value="all">All products on order</option>
              <option value="short">Runs out at some point</option>
              <option value="8">Runs out within 8 weeks</option>
            </select>
          </label>
        )}
        <input type="search" placeholder="Search products…" value={search} onChange={(e) => setSearch(e.target.value)} />
        <span className="lg-sp" />
        {view === "ledger" ? (
          <span className="lg-tb-note">{ledgerNote}</span>
        ) : (
          <div className="lg-legend">
            <span>
              <i style={{ background: "#ecfdf5" }} />
              Covered by stock
            </span>
            <span>
              <i style={{ background: "#fee2e2", boxShadow: "inset 3px 0 0 #dc2626" }} />
              Runs out here (− pallets short)
            </span>
            <span>
              <i style={{ background: "#fff1f2" }} />
              Not covered
            </span>
          </div>
        )}
        <button className="lg-btn" onClick={() => void refresh()} disabled={loading}>
          Refresh
        </button>
      </div>
      <div className="lg-body">
        <div className="lg-grid-wrap">{view === "ledger" ? renderLedger() : renderHorizon()}</div>
      </div>

      {tip && tipRow && tipLines.length > 0 && (
        <div className="lg-tip" style={{ left: Math.min(tip.x + 14, window.innerWidth - 280), top: tip.y + 14 }}>
          <b>
            {tipRow.p.description} {tipRow.p.size}L · {fmtN(tipLines.reduce((s, l) => s + l.quantity, 0))} pallets
          </b>
          {tipLines.slice(0, 8).map((l) => (
            <div key={l.orderId}>
              {l.orderNo} · {l.customerName} · {l.quantity}
              {l.picked ? ` (${l.picked} picked)` : ""} · {fmtD(l.deliveryDate)}
              {l.covered ? "" : " · short"}
            </div>
          ))}
          {tipLines.length > 8 && <div>+ {tipLines.length - 8} more</div>}
        </div>
      )}
    </div>
  );
}
