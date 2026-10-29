import ToolbarDate from "../../components/ToolbarDate";
import { useEffect, useMemo, useRef, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  deleteWeighbridgeRecord,
  getCompleteRecords,
  getPendingRecords,
  WeighbridgeComplete,
  WeighbridgePending,
} from "../../api/weighbridge";
import {
  getConfig,
  getLiveWeight,
  getLiveWeight2,
  getState,
  setBarrier,
  setGate,
  RelayState,
  RemoteScreen,
} from "../../api/gateBarrier";
import { fmtDateTime, fmtQty, fmtQty2, todayInput } from "../../format";
import { resolveVncWsUrl } from "../../config";
import { AdminUserDto } from "../../types";
import WeighbridgeEditModal from "./WeighbridgeEditModal";
import RemoteScreenModal from "./RemoteScreenModal";
import DeliveryDetailPanel from "./DeliveryDetailPanel";
import ExportForSupplierModal from "./ExportForSupplierModal";
import cc from "../../styles/ContentCard.module.css";
import styles from "./WeighbridgePage.module.css";
import PageHeader from "../../widgets/PageHeader";
import "./Weighbridge.css";
import LiveWeightBody from "./LiveWeightPanel";
import { STYLE } from "../../api/features";
import { useFeature } from "../../features";

const STATE_POLL_MS = 1500;
const WEIGHT_POLL_MS = 1000;

// Mirrors the WPF WeighbridgeRecordsView: live weight + gate/barrier (top-left),
// Pending (top-right), Completed with a date range (bottom).
export default function WeighbridgeRecordsPage(_props: { user: AdminUserDto }) {
  // ---- live weight + relay ----
  const [weight, setWeight] = useState("—");
  const [weightStale, setWeightStale] = useState(true);
  const [weight2, setWeight2] = useState("—");
  const [weight2Stale, setWeight2Stale] = useState(true);
  const [relay, setRelay] = useState<RelayState>({ connected: false, gateOpen: false, barrierOpen: false });
  // False until the first relay poll answers (either way): until then the IO-card fault box is
  // not shown, only a soft "Connecting" panel, so a page load never flashes the fault.
  const [relayChecked, setRelayChecked] = useState(false);
  const [busy, setBusy] = useState(false);
  const pollingRef = useRef(false);
  // Which skin the Live Weight column draws — Super Admin > Scale/Relay Styles.
  const dockStyle = useFeature(STYLE.scaleRelayDarkDock);

  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      if (pollingRef.current) return;
      pollingRef.current = true;
      try {
        const s = await getState();
        if (!cancelled) setRelay(s);
      } catch {
        if (!cancelled) setRelay((p) => ({ ...p, connected: false }));
      } finally {
        if (!cancelled) setRelayChecked(true);
        pollingRef.current = false;
      }
    };
    void tick();
    const id = setInterval(tick, STATE_POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      const [r, r2] = await Promise.all([getLiveWeight(), getLiveWeight2()]);
      if (cancelled) return;
      if (r && r.isConnected && r.ageMs >= 0 && r.ageMs < 5000) {
        setWeight(r.weight || "0");
        setWeightStale(false);
      } else {
        setWeightStale(true);
      }
      if (r2 && r2.isConnected && r2.ageMs >= 0 && r2.ageMs < 5000) {
        setWeight2(r2.weight || "0");
        setWeight2Stale(false);
      } else {
        setWeight2Stale(true);
      }
    };
    void tick();
    const id = setInterval(tick, WEIGHT_POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);

  async function toggle(which: "gate" | "barrier") {
    setBusy(true);
    try {
      const open = which === "gate" ? !relay.gateOpen : !relay.barrierOpen;
      setRelay(which === "gate" ? await setGate(open) : await setBarrier(open));
    } catch (e) {
      void messageDialog("Command failed.\n" + (e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  // ---- Remote Connect (embedded noVNC to the TOUCH-IN / TOUCH-OUT touchscreens) ----
  const [remoteScreens, setRemoteScreens] = useState<RemoteScreen[]>([]);
  const [openScreen, setOpenScreen] = useState<RemoteScreen | null>(null);

  useEffect(() => {
    getConfig()
      .then((c) => setRemoteScreens(c.remoteScreens ?? []))
      .catch(() => setRemoteScreens([]));
  }, []);

  // Resolve a configured screen by name; allow a localStorage override of the ws URL
  // so it works before the API config is populated.
  function openRemote(name: string) {
    const found = remoteScreens.find((s) => s.name.toLowerCase() === name.toLowerCase());
    const wsUrl = found?.wsUrl || localStorage.getItem(`vncWs:${name}`) || "";
    if (!wsUrl) {
      void messageDialog(
        `No remote screen configured for ${name}.\n` +
          `Set GateBarrier:RemoteScreens on the API (or localStorage "vncWs:${name}").`,
      );
      return;
    }
    // Resolve the WS host (dev → server's Tailscale IP, since "evergreen" won't resolve here).
    // Keep the rest of the configured screen (its lane camera) — only the ws URL is rewritten.
    setOpenScreen({ ...found, name, wsUrl: resolveVncWsUrl(wsUrl) });
  }

  // Add/Edit modal: { id: null } = add new, { id } = edit existing, null = closed.
  const [editing, setEditing] = useState<{ id: number | null } | null>(null);
  // The completed delivery whose detail panel is open over the card (Completed → View).
  const [viewing, setViewing] = useState<WeighbridgeComplete | null>(null);

  // ---- pending ----
  const [pending, setPending] = useState<WeighbridgePending[]>([]);
  const [pendingLoading, setPendingLoading] = useState(false);
  const [selectedPending, setSelectedPending] = useState<WeighbridgePending | null>(null);

  async function refreshPending() {
    setPendingLoading(true);
    try {
      const rows = await getPendingRecords();
      setPending(rows);
      setSelectedPending((cur) => (cur ? rows.find((r) => r.id === cur.id) ?? null : null));
    } catch (e) {
      void messageDialog("Failed to load pending records.\n" + (e as Error).message);
    } finally {
      setPendingLoading(false);
    }
  }

  async function deletePending() {
    if (!selectedPending) return;
    if (!(await confirmDialog("Delete this pending weighbridge record?"))) return;
    try {
      await deleteWeighbridgeRecord(selectedPending.id);
      setSelectedPending(null);
      await refreshPending();
    } catch (e) {
      void messageDialog("Failed to delete.\n" + (e as Error).message);
    }
  }

  // ---- completed ----
  const [from, setFrom] = useState(todayInput(-7));
  const [to, setTo] = useState(todayInput());
  const [completed, setCompleted] = useState<WeighbridgeComplete[]>([]);
  const [completedLoading, setCompletedLoading] = useState(false);
  const [search, setSearch] = useState("");
  const [selectedCompleted, setSelectedCompleted] = useState<WeighbridgeComplete | null>(null);
  const [exporting, setExporting] = useState(false);

  async function refreshCompleted() {
    setCompletedLoading(true);
    try {
      const rows = await getCompleteRecords(from, to);
      setCompleted(rows);
      setSelectedCompleted((cur) => (cur ? rows.find((r) => r.id === cur.id) ?? null : null));
    } catch (e) {
      void messageDialog("Failed to load completed records.\n" + (e as Error).message);
    } finally {
      setCompletedLoading(false);
    }
  }

  async function deleteCompleted() {
    if (!selectedCompleted) return;
    const docket = selectedCompleted.docketNumber ? ` (docket ${selectedCompleted.docketNumber})` : "";
    if (!(await confirmDialog(`Delete this weighbridge record${docket}?`))) return;
    try {
      await deleteWeighbridgeRecord(selectedCompleted.id);
      setSelectedCompleted(null);
      await refreshCompleted();
    } catch (e) {
      void messageDialog("Failed to delete.\n" + (e as Error).message);
    }
  }

  // After an add/edit, refresh both grids — editing can move a record between states.
  function onModalClose(saved: boolean) {
    setEditing(null);
    if (saved) {
      void refreshPending();
      void refreshCompleted();
    }
  }

  useEffect(() => {
    void refreshPending();
  }, []);

  useEffect(() => {
    void refreshCompleted();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [from, to]);

  const term = search.trim().toLowerCase();
  const completedFiltered = useMemo(() => {
    if (!term) return completed;
    return completed.filter((r) =>
      [r.docketNumber, r.haulierName, r.vehicleReg, r.supplier, r.product].some((v) =>
        v?.toLowerCase().includes(term),
      ),
    );
  }, [completed, term]);

  const inOut = (r: { isOutgoing: boolean }) => (
    <span className={`io-badge ${r.isOutgoing ? "out" : "in"}`}>{r.isOutgoing ? "OUT" : "IN"}</span>
  );

  const pendingColumns: Column<WeighbridgePending>[] = [
    { key: "io", header: " ", width: 50, align: "center", render: inOut },
    { key: "pendingWeighDate", header: "Weigh Date", width: 130, render: (r) => fmtDateTime(r.pendingWeighDate) },
    { key: "haulierName", header: "Haulier" },
    { key: "docketNumber", header: "Docket", width: 70 },
    { key: "vehicleReg", header: "Vehicle", width: 90 },
    { key: "supplier", header: "Supplier", width: 130 },
    { key: "pendingWeight", header: "Weight", width: 80, align: "right", render: (r) => fmtQty(r.pendingWeight) },
  ];

  // Columns mirror the WPF WeighbridgeRecordsView completed grid exactly:
  // Haulier, Docket No, Vehicle Reg, Product, Supplier, Bog Name, Weighed In Date,
  // Weight In (Kg), Weigh Out Date, Weight Out (Kg), Net Weight (Kg),
  // Sample Weight (Kg), Cubic M3.
  const completedColumns: Column<WeighbridgeComplete>[] = [
    { key: "haulierName", header: "Haulier", render: (r) => <Highlight text={r.haulierName} term={term} /> },
    { key: "docketNumber", header: "Docket No", width: 80, render: (r) => <Highlight text={r.docketNumber} term={term} /> },
    { key: "vehicleReg", header: "Vehicle Reg", width: 90, render: (r) => <Highlight text={r.vehicleReg} term={term} /> },
    { key: "product", header: "Product", width: 110, render: (r) => <Highlight text={r.product} term={term} /> },
    { key: "supplier", header: "Supplier", width: 120, render: (r) => <Highlight text={r.supplier} term={term} /> },
    { key: "bogName", header: "Bog Name", width: 110 },
    { key: "dateWeightIn", header: "Weighed In Date", width: 130, render: (r) => fmtDateTime(r.dateWeightIn) },
    { key: "weightIn", header: "Weight In (Kg)", width: 105, align: "right", render: (r) => fmtQty(r.weightIn) },
    { key: "dateWeightOut", header: "Weigh Out Date", width: 130, render: (r) => fmtDateTime(r.dateWeightOut) },
    { key: "weightOut", header: "Weight Out (Kg)", width: 105, align: "right", render: (r) => fmtQty(r.weightOut) },
    { key: "netWeight", header: "Net Weight (Kg)", width: 105, align: "right", render: (r) => fmtQty(r.netWeight) },
    { key: "sampleBoxWeight", header: "Sample Weight (Kg)", width: 120, align: "right", render: (r) => fmtQty2(r.sampleBoxWeight) },
    { key: "cubicM3", header: "Cubic M3", width: 90, align: "right", render: (r) => fmtQty(r.cubicM3) },
    {
      key: "view",
      header: "",
      width: 50,
      render: (r) => (
        <button
          className="link-btn"
          onClick={(e) => {
            e.stopPropagation();
            setViewing(r);
          }}
        >
          View
        </button>
      ),
    },
  ];

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader
          title="Deliveries/Incoming"
          meta={`${pending.length} pending · ${completedFiltered.length} completed`}
        />

        <div className={cc.body}>
          <div className={styles.split}>
            {/* TOP ROW — the bridge as it reads right now, beside the loads still to weigh out. */}
            <div className={styles.topRow}>
              <aside className={styles.liveCol}>
                <div className={`${cc.colHeader} ${cc.colHeaderLedger}`}>
                  <h2 className={cc.colTitle}>Live Weight</h2>
                </div>
                <div className={styles.liveBody}>
                  <LiveWeightBody
                    weight={weight}
                    weightStale={weightStale}
                    weight2={weight2}
                    weight2Stale={weight2Stale}
                    relay={relay}
                    relayChecked={relayChecked}
                    busy={busy}
                    onToggle={(w) => void toggle(w)}
                    onRemote={openRemote}
                    style={dockStyle ? "dock" : "current"}
                  />
                </div>
              </aside>

              <section className={styles.pendingCol}>
                <div className={`${cc.colHeader} ${cc.colHeaderLedger}`}>
                  <h2 className={cc.colTitle}>Pending <span className={cc.colCount}>{pending.length}</span></h2>
                  <div className={cc.colActions}>
                    {pendingLoading && <span className="toolbar-note">Loading…</span>}
                    <button
                      className="toolbar-btn"
                      onClick={() => selectedPending && setEditing({ id: selectedPending.id })}
                      disabled={!selectedPending}
                    >
                      Edit
                    </button>
                    <button className="toolbar-btn" onClick={() => void deletePending()} disabled={!selectedPending}>
                      Delete
                    </button>
                    <button className="toolbar-btn" onClick={() => void refreshPending()} disabled={pendingLoading}>
                      Refresh
                    </button>
                  </div>
                </div>
                <div className={cc.colBody}>
                  <DataTable
                    columns={pendingColumns}
                    rows={pending}
                    rowKey={(r) => r.id}
                    selectedKey={selectedPending?.id ?? null}
                    onSelect={setSelectedPending}
                    onDoubleClick={(r) => setEditing({ id: r.id })}
                    emptyText={pendingLoading ? "Loading…" : "No pending records"}
                  />
                </div>
              </section>
            </div>

            {/* BOTTOM ROW — every record weighed out in the date range. */}
            <section className={styles.bottomRow}>
              <div className={`${cc.colHeader} ${cc.colHeaderLedger}`}>
                <h2 className={cc.colTitle}>Completed <span className={cc.colCount}>{completedFiltered.length}</span></h2>
                <input
                  className="form-input toolbar-search"
                  placeholder="Search…"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                />
                <span className={cc.spacer} />
                <div className={cc.colActions}>
                  <ToolbarDate label="From" value={from} onChange={setFrom} />
                  <ToolbarDate label="To" value={to} onChange={setTo} />
                  {completedLoading && <span className="toolbar-note">Loading…</span>}
                  <button className="toolbar-btn" onClick={() => setEditing({ id: null })}>
                    Add
                  </button>
                  <button
                    className="toolbar-btn"
                    onClick={() => selectedCompleted && setEditing({ id: selectedCompleted.id })}
                    disabled={!selectedCompleted}
                  >
                    Edit
                  </button>
                  <button className="toolbar-btn" onClick={() => void deleteCompleted()} disabled={!selectedCompleted}>
                    Delete
                  </button>
                  <button
                    className="toolbar-btn"
                    onClick={() => setExporting(true)}
                    disabled={completedFiltered.length === 0}
                  >
                    Export for Supplier
                  </button>
                  <button className="toolbar-btn" onClick={() => void refreshCompleted()} disabled={completedLoading}>
                    Refresh
                  </button>
                </div>
              </div>
              <div className={cc.colBody}>
                <DataTable
                  columns={completedColumns}
                  rows={completedFiltered}
                  rowKey={(r) => r.id}
                  selectedKey={selectedCompleted?.id ?? null}
                  onSelect={setSelectedCompleted}
                  onDoubleClick={setViewing}
                  emptyText={completedLoading ? "Loading…" : "No completed records"}
                />
              </div>
            </section>
          </div>
        </div>
      </div>

      {/* Completed → View: one delivery read-only, flying in over the whole card (cc.page is the
          positioned ancestor). */}
      {viewing && <DeliveryDetailPanel row={viewing} onClose={() => setViewing(null)} />}

      {editing && <WeighbridgeEditModal id={editing.id} onClose={onModalClose} />}

      {openScreen && <RemoteScreenModal screen={openScreen} onClose={() => setOpenScreen(null)} />}

      {exporting && (
        <ExportForSupplierModal rows={completedFiltered} onClose={() => setExporting(false)} />
      )}
    </div>
  );
}
