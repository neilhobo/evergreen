import { Fragment, useEffect, useMemo, useRef, useState } from "react";
import TopBar from "../components/touch/TopBar";
import { getProductionPlanning, PlanningLine, PlanningProduct } from "../api/touch";

interface Props {
  deviceName: string;
  onHome: () => void;
}

// Production Planner — the Ledger view of TauriAdmin's Production > Production Planning,
// for the factory floor: every product on an open order totalled against what is in stock,
// worst cover first. Tap a product to open the orders behind it, in the order the stock is
// used up (ASAP first, then by delivery date), each marked whether the stock reaches it.
//
// The arithmetic mirrors TauriAdmin's ProductionPlanningPanel.compute() — keep the two in
// step. Read-only, and it refreshes itself: this screen is left open on the floor.

// Days ahead an order may be due and still count; 0 = every open order.
const HORIZONS: { days: number; label: string }[] = [
  { days: 0, label: "All orders" },
  { days: 14, label: "2 weeks" },
  { days: 28, label: "4 weeks" },
  { days: 56, label: "8 weeks" },
];

const REFRESH_MS = 60_000;

type Status = "now" | "soon" | "later" | "tight" | "ok";
const STATUS: Record<Status, { cls: string; label: string }> = {
  now: { cls: "bad", label: "Short now" },
  soon: { cls: "warn", label: "Short soon" },
  later: { cls: "later", label: "Short later" },
  tight: { cls: "warn", label: "Tight" },
  ok: { cls: "good", label: "Covered" },
};

// A line with its place in the product's running total: covered = the stock still reaches
// this order once every earlier one has taken its share.
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
  orders: number;
  neededBy: string | null; // earliest delivery date; null = ASAP
  // undefined = the stock covers every order; null = runs out on an ASAP order; else the
  // delivery date of the first order it cannot fill.
  runsOut: string | null | undefined;
  runsDays: number;
  status: Status;
}

const DAY = 86_400_000;

function daysFromToday(iso: string): number {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const d = new Date(iso);
  d.setHours(0, 0, 0, 0);
  return Math.round((d.getTime() - today.getTime()) / DAY);
}

function compute(products: PlanningProduct[], horizonDays: number): Row[] {
  const rows: Row[] = [];
  for (const p of products) {
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

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
// "13 Oct", with the year only when it is not this year ("1 Apr 27"); no date is ASAP.
function fmtD(when: string | null): string {
  if (when == null) return "ASAP";
  const d = new Date(when);
  const yr = d.getFullYear() !== new Date().getFullYear() ? ` ${String(d.getFullYear()).slice(2)}` : "";
  return `${d.getDate()} ${MONTHS[d.getMonth()]}${yr}`;
}
const fmtN = (n: number) => n.toLocaleString("en-GB");
const fmtCover = (n: number) => (n < 0 ? `−${fmtN(-n)}` : fmtN(n));

export default function ProductionPlannerScreen({ deviceName, onHome }: Props) {
  const [products, setProducts] = useState<PlanningProduct[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [horizon, setHorizon] = useState(0);
  const [shortOnly, setShortOnly] = useState(false);
  const [open, setOpen] = useState<Set<number>>(new Set());
  // Drops the answer to a refresh that was overtaken, or that lands after the screen closed.
  const seq = useRef(0);

  async function refresh() {
    const mine = ++seq.current;
    setLoading(true);
    try {
      const data = await getProductionPlanning(deviceName);
      if (mine !== seq.current) return;
      setProducts(data.products);
      setError(null);
      setLoaded(true);
    } catch (err) {
      // Keep the last good figures on screen; the error line says they may be stale.
      if (mine === seq.current) setError((err as Error).message);
    } finally {
      if (mine === seq.current) setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
    const t = window.setInterval(() => void refresh(), REFRESH_MS);
    return () => {
      window.clearInterval(t);
      seq.current++;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deviceName]);

  // Cut to the horizon, worst cover first; then whichever runs out soonest, then by name.
  const rows = useMemo(
    () =>
      compute(products, horizon)
        .filter((r) => !shortOnly || r.cover < 0)
        .sort(
          (a, b) =>
            a.cover - b.cover ||
            a.runsDays - b.runsDays ||
            (a.p.description ?? "").localeCompare(b.p.description ?? "") ||
            (a.p.size ?? 0) - (b.p.size ?? 0),
        ),
    [products, horizon, shortOnly],
  );

  const openOrders = useMemo(() => new Set(products.flatMap((p) => p.lines.map((l) => l.orderId))).size, [products]);

  const totals = {
    stock: rows.reduce((s, r) => s + r.p.inStock, 0),
    ordered: rows.reduce((s, r) => s + r.ordered, 0),
    picked: rows.reduce((s, r) => s + r.picked, 0),
    short: rows.reduce((s, r) => s + r.shortfall, 0),
    shortCount: rows.filter((r) => r.cover < 0).length,
  };

  function toggle(id: number) {
    setOpen((cur) => {
      const next = new Set(cur);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  return (
    <div className="tt-app">
      <TopBar
        title="Production Planner"
        onBack={onHome}
        hideBrand
        right={loaded && <span className="tt-note" style={{ fontSize: 15 }}>{fmtN(openOrders)} open orders</span>}
      />
      <div className="tt-body">
        <div className="tt-panel" style={{ display: "flex", flexDirection: "column", height: "100%", minHeight: 0 }}>
          <div className="tt-pp-bar">
            <span className="tt-note">Orders due</span>
            <div className="tt-pp-seg" role="group" aria-label="Orders due">
              {HORIZONS.map((h) => (
                <button key={h.days} type="button" aria-pressed={horizon === h.days} onClick={() => setHorizon(h.days)}>
                  {h.label}
                </button>
              ))}
            </div>
            <div className="tt-pp-seg" role="group" aria-label="Show">
              <button type="button" aria-pressed={!shortOnly} onClick={() => setShortOnly(false)}>
                All products
              </button>
              <button type="button" aria-pressed={shortOnly} onClick={() => setShortOnly(true)}>
                Short only
              </button>
            </div>
            <span style={{ flex: 1 }} />
            {loaded && (
              <span className="tt-note">
                {totals.shortCount} of {rows.length} products short · {fmtN(totals.short)} pallets to make
              </span>
            )}
            <button
              className="tt-btn tt-ghost"
              style={{ minHeight: 48, padding: "0 20px" }}
              disabled={loading}
              onClick={() => void refresh()}
            >
              Refresh
            </button>
          </div>
          {error && <div className="tt-error" style={{ marginBottom: 10 }}>{error}</div>}
          <div className="tt-grid-wrap">
            <table className="tt-grid tt-pp-grid">
              <thead>
                <tr>
                  <th className="tt-col-grow">Product</th>
                  <th className="tt-r">In stock</th>
                  <th className="tt-r">Ordered</th>
                  <th className="tt-r">Picked</th>
                  <th className="tt-r">To pick</th>
                  <th className="tt-r">Cover</th>
                  <th className="tt-r">Orders</th>
                  <th>Needed by</th>
                  <th>Runs out</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => {
                  const isOpen = open.has(r.p.productId);
                  return (
                    <Fragment key={r.p.productId}>
                      <tr className={isOpen ? "tt-pp-open" : undefined} onClick={() => toggle(r.p.productId)}>
                        <td className="tt-col-grow">
                          <span className="tt-pp-caret">{isOpen ? "▾" : "▸"}</span>
                          {r.p.description} <span className="tt-pp-dim">{r.p.size}L</span>
                        </td>
                        <td className="tt-r">{fmtN(r.p.inStock)}</td>
                        <td className="tt-r">{fmtN(r.ordered)}</td>
                        <td className={r.picked ? "tt-r" : "tt-r tt-pp-dim"}>{fmtN(r.picked)}</td>
                        <td className="tt-r">{fmtN(r.ordered - r.picked)}</td>
                        <td className={r.cover < 0 ? "tt-r tt-pp-neg" : "tt-r"}>{fmtCover(r.cover)}</td>
                        <td className="tt-r">{r.orders}</td>
                        <td>{fmtD(r.neededBy)}</td>
                        <td>
                          {r.runsOut === undefined ? (
                            <span className="tt-pp-dim">—</span>
                          ) : r.runsOut === null ? (
                            <b className="tt-pp-neg">Now</b>
                          ) : (
                            fmtD(r.runsOut)
                          )}
                        </td>
                        <td>
                          <span className={`tt-pp-pill ${STATUS[r.status].cls}`}>{STATUS[r.status].label}</span>
                        </td>
                      </tr>
                      {isOpen &&
                        r.lines.map((l, i) => (
                          <tr className="tt-pp-sub" key={`${r.p.productId}-${l.orderId}-${i}`} onClick={() => toggle(r.p.productId)}>
                            <td className="tt-col-grow" colSpan={2}>
                              {l.orderNo} · {l.customerName}
                            </td>
                            <td className="tt-r">{l.quantity}</td>
                            <td className="tt-r">{l.picked || ""}</td>
                            <td className="tt-r">{l.quantity - l.picked}</td>
                            <td className={l.covered ? "tt-r tt-pp-pos" : "tt-r tt-pp-neg"}>
                              {l.covered ? "✓ from stock" : "✗ short"}
                            </td>
                            <td className="tt-r tt-pp-dim">cum {l.cumulative}</td>
                            <td colSpan={3}>
                              Due {fmtD(l.deliveryDate)} · placed {fmtD(l.dateCreated)}
                            </td>
                          </tr>
                        ))}
                    </Fragment>
                  );
                })}
              </tbody>
              {rows.length > 0 && (
                <tfoot>
                  <tr>
                    <td>{rows.length} products</td>
                    <td className="tt-r">{fmtN(totals.stock)}</td>
                    <td className="tt-r">{fmtN(totals.ordered)}</td>
                    <td className="tt-r">{fmtN(totals.picked)}</td>
                    <td className="tt-r">{fmtN(totals.ordered - totals.picked)}</td>
                    <td className="tt-r tt-pp-neg">{totals.short ? `−${fmtN(totals.short)}` : ""}</td>
                    <td colSpan={3}></td>
                    <td>{totals.shortCount} short</td>
                  </tr>
                </tfoot>
              )}
            </table>
            {rows.length === 0 && loaded && (
              <div className="tt-note" style={{ padding: 18 }}>
                {shortOnly ? "Nothing short." : "No products on open orders."}
              </div>
            )}
            {!loaded && loading && <div className="tt-note" style={{ padding: 18 }}>Loading…</div>}
          </div>
        </div>
      </div>
    </div>
  );
}
