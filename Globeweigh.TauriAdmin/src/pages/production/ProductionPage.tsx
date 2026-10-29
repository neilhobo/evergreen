import ToolbarDate from "../../components/ToolbarDate";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import { fmtDateTime, todayInput } from "../../format";
import { getProductionPallets, getNewProductionPallets, CodicoPallet } from "../../api/production";
import ProductionSummaryPanel from "./ProductionSummaryPanel";
import ProductionPlanningPanel from "./ProductionPlanningPanel";
import { AdminUserDto } from "../../types";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import PageTabs, { PageTab } from "../../widgets/PageTabs";

import { FEATURE } from "../../api/features";
import { useFeature } from "../../features";

type ProdTab = "pallets" | "summary" | "planning";

const TABS: PageTab<ProdTab>[] = [
  { key: "pallets", label: "Production" },
  { key: "summary", label: "Production Summary" },
];
// Staged (Super Admin > Staging): shown only while Production Planning is switched on.
const PLANNING_TAB: PageTab<ProdTab> = { key: "planning", label: "Production Planning" };

// How often the live feed polls for newly-created pallets, and how long an arrived
// row stays highlighted as "new".
const POLL_MS = 2000;
const FLASH_MS = 6000;

// Production pallet list — mirrors the WPF ProductionPalletListView, plus a live feed
// that streams pallets in as the line scans them (newest first).
export default function ProductionPage(_props: { user: AdminUserDto }) {
  const [tabState, setTab] = useState<ProdTab>("pallets");
  // What the Production Planning tab puts in the header meta slot ("N open orders").
  const [planningMeta, setPlanningMeta] = useState<string | undefined>();
  const planningOn = useFeature(FEATURE.productionPlanning);
  const tabs = planningOn ? [...TABS, PLANNING_TAB] : TABS;
  // Switched off while its tab is open: fall back to the pallet list rather than a missing tab.
  const tab = tabState === "planning" && !planningOn ? "pallets" : tabState;
  const [rows, setRows] = useState<CodicoPallet[]>([]);
  const [selected, setSelected] = useState<CodicoPallet | null>(null);
  const [from, setFrom] = useState(todayInput(-7));
  const [to, setTo] = useState(todayInput());
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(false);

  // Live feed state.
  const [live, setLive] = useState(true);
  const [newCount, setNewCount] = useState(0);
  const [flashIds, setFlashIds] = useState<Set<number>>(new Set());
  const maxIdRef = useRef(0); // highest pallet id we've shown (the live cursor)
  const initialized = useRef(false); // true once the first load has set the cursor
  const inFlight = useRef(false);
  const flashTimers = useRef<number[]>([]);

  async function refresh() {
    setLoading(true);
    initialized.current = false; // don't let the live poll run against a stale cursor
    try {
      const data = await getProductionPallets(from, to);
      setRows(data);
      setSelected(null);
      // Reset the live cursor and any pending highlights to the freshly loaded set.
      maxIdRef.current = data.reduce((m, r) => Math.max(m, r.id), 0);
      setNewCount(0);
      setFlashIds(new Set());
      initialized.current = true;
    } catch (err) {
      void messageDialog("Failed to load production pallets.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [from, to]);

  // Poll for pallets created since our cursor and prepend them newest-first.
  const poll = useCallback(async () => {
    if (inFlight.current || !initialized.current) return;
    inFlight.current = true;
    try {
      const incoming = await getNewProductionPallets(from, to, maxIdRef.current);
      // Only rows genuinely past the cursor count as new — guards against a server
      // that ignores sinceId (it would otherwise re-send and re-flash the whole list).
      const fresh = incoming.filter((r) => r.id > maxIdRef.current);
      if (fresh.length) {
        const newestFirst = [...fresh].reverse(); // API returns oldest-first
        maxIdRef.current = Math.max(maxIdRef.current, ...fresh.map((r) => r.id));
        setRows((prev) => [...newestFirst, ...prev]);
        setNewCount((c) => c + newestFirst.length);

        const ids = newestFirst.map((r) => r.id);
        setFlashIds((prev) => new Set([...prev, ...ids]));
        const t = window.setTimeout(() => {
          setFlashIds((prev) => {
            const next = new Set(prev);
            ids.forEach((i) => next.delete(i));
            return next;
          });
        }, FLASH_MS);
        flashTimers.current.push(t);
      }
    } catch {
      // Transient poll failure — stay live and try again on the next tick.
    } finally {
      inFlight.current = false;
    }
  }, [from, to]);

  useEffect(() => {
    if (!live || tab !== "pallets") return;
    const id = window.setInterval(() => void poll(), POLL_MS);
    return () => window.clearInterval(id);
  }, [live, poll, tab]);

  // Clean up any outstanding flash timers on unmount.
  useEffect(() => {
    return () => {
      flashTimers.current.forEach((t) => window.clearTimeout(t));
    };
  }, []);

  // Missed scan: an unscanned pallet where a later pallet (higher id) HAS been scanned —
  // the scanner went past it, and that pallet is what set the alarm off. Newer pallets
  // simply not reached yet don't qualify, and manual pallets are outside the rule on both
  // sides (they never pass the conveyor scanner), so they are neither missed nor evidence
  // that the scanner has moved on. Mirrors the API's alarm rule in
  // ProductionController.UnackedMissedTodayAsync and the touch Production screen.
  // Computed over every loaded row, never the search-filtered subset — otherwise typing
  // in the search box could drop the scanned pallet that proves an older one was missed.
  const maxScannedId = useMemo(
    () => rows.reduce((m, r) => (r.isScanned && !r.isManual && r.id > m ? r.id : m), 0),
    [rows],
  );
  const isMissed = (p: CodicoPallet) => !p.isScanned && !p.isManual && p.id < maxScannedId;
  const missedCount = useMemo(
    () => rows.filter((r) => !r.isScanned && !r.isManual && r.id < maxScannedId).length,
    [rows, maxScannedId],
  );

  const term = search.trim().toLowerCase();

  const filtered = useMemo(() => {
    if (!term) return rows;
    return rows.filter((r) =>
      [r.codicoCode, r.uniqueCode, r.description, r.gtin].some((v) => v?.toLowerCase().includes(term)),
    );
  }, [rows, term]);

  const columns: Column<CodicoPallet>[] = [
    { key: "codicoCode", header: "Codico", width: 110, render: (r) => <Highlight text={r.codicoCode} term={term} /> },
    { key: "uniqueCode", header: "Unique Code", fit: true, render: (r) => <Highlight text={r.uniqueCode} term={term} /> },
    { key: "description", header: "Description", render: (r) => <Highlight text={r.description} term={term} /> },
    { key: "gtin", header: "GTIN", width: 120, render: (r) => <Highlight text={r.gtin} term={term} /> },
    { key: "size", header: "Size", width: 60, align: "right" },
    { key: "upp", header: "UPP", width: 60, align: "right" },
    { key: "weight", header: "Weight", width: 80, align: "right", render: (r) => r.weight.toFixed(2) },
    { key: "lineNumber", header: "Line", width: 60, align: "right" },
    { key: "dateCreated", header: "Created", width: 130, render: (r) => fmtDateTime(r.dateCreated) },
    {
      key: "isScanned",
      header: "Scanned",
      width: 80,
      align: "center",
      render: (r) =>
        r.isScanned ? (
          <span className="scan-badge" title={r.dateScanned ? fmtDateTime(r.dateScanned) : "Scanned"}>
            ✓
          </span>
        ) : isMissed(r) ? (
          // The pallet that set the alarm off — 🔕 once a touch PC has silenced it.
          <span
            className="alarm-badge"
            title={r.alarmAcked ? "Missed scan — alarm silenced" : "Missed scan — alarm sounding"}
          >
            {r.alarmAcked ? "🔕" : "🚨"}
          </span>
        ) : (
          ""
        ),
    },
  ];

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader
          title="Production"
          meta={tab === "pallets" ? `${filtered.length} pallets` : tab === "planning" ? planningMeta : undefined}
        />
        <PageTabs tabs={tabs} active={tab} onChange={setTab} />
        <div className={cc.body}>
      {tab === "planning" ? (
        <ProductionPlanningPanel onMeta={setPlanningMeta} />
      ) : tab === "summary" ? (
        <ProductionSummaryPanel />
      ) : (
        <div className="po-main">
          <div className="toolbar">
            <ToolbarDate label="From" value={from} onChange={setFrom} />
          <ToolbarDate label="To" value={to} onChange={setTo} />
          <button className="toolbar-btn" onClick={() => void refresh()} disabled={loading}>
            Refresh
          </button>
          <input
            className="form-input toolbar-search"
            placeholder="Search pallets…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {loading && <span className="toolbar-note">Loading…</span>}
          {/* Colour key — only worth the toolbar space when there's a missed pallet to explain. */}
          {missedCount > 0 && (
            <div className="grid-legend" title="Pallets that passed the scanner without being scanned">
              <span className="legend-count">{missedCount} missed</span>
              <span className="legend-swatch" /> alarm
              <span className="legend-swatch legend-swatch-dim" /> silenced
            </div>
          )}

          <div className="toolbar-right">
            <div className="live-status" title={live ? "Live — new pallets appear automatically" : "Live feed paused"}>
              <span className={`live-dot ${live ? "live-dot-on" : "live-dot-off"}`} />
              <span className="live-status-text">{live ? "Live" : "Paused"}</span>
              {live && newCount > 0 && <span className="live-new-badge">+{newCount} new</span>}
            </div>
            <button className="toolbar-btn" onClick={() => setLive((v) => !v)}>
              {live ? "Pause" : "Resume"}
            </button>
          </div>
        </div>

        <DataTable
          columns={columns}
          rows={filtered}
          rowKey={(r) => r.id}
          selectedKey={selected?.id ?? null}
          onSelect={setSelected}
          rowClass={(r) =>
            [
              isMissed(r) ? (r.alarmAcked ? "row-missed row-alarm-acked" : "row-missed") : "",
              flashIds.has(r.id) ? "row-flash" : "",
            ]
              .filter(Boolean)
              .join(" ") || undefined
          }
          emptyText={loading ? "Loading…" : "No pallets"}
        />
        </div>
      )}
        </div>
      </div>
    </div>
  );
}
