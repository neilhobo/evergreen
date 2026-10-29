import { useEffect, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  SystemDevice,
  deleteDevices,
  deviceVncWsUrl,
  getAllDevices,
  pingDevices,
  setDeviceHidden,
} from "../../api/devices";
import { GateBarrierConfig, getConfig } from "../../api/gateBarrier";
import { resolveVncWsUrl } from "../../config";
import RemoteScreenModal from "../weighbridge/RemoteScreenModal";
import { AdminUserDto } from "../../types";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";

function fmtDateTime(s: string | null): string {
  if (!s) return "—";
  const d = new Date(s);
  return isNaN(d.getTime()) ? "—" : d.toLocaleString();
}

// Client-facing Devices overview — every device registered on the system (touch
// screens, factory PCs, picking tablets). Master/detail: compact grid on the left,
// the selected device's full details + Remote Connect on the right. Remote Connect
// picks the right VNC path per device: screens named in the gate-barrier remoteScreens
// config (TOUCH-IN / TOUCH-OUT) go via the GlobeweighService bridge; everything else
// goes via the API's per-device bridge (droidVNC-NG on the tablets).
// A dev build adds housekeeping tools (hard Delete, Hide, Show hidden).
export default function SystemDevicesPage(_props: { user: AdminUserDto }) {
  const dev = import.meta.env.DEV;
  const [rows, setRows] = useState<SystemDevice[]>([]);
  const [loading, setLoading] = useState(true);
  const [showHidden, setShowHidden] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [config, setConfig] = useState<GateBarrierConfig | null>(null);
  const [connecting, setConnecting] = useState(false);
  // Live reachability, device id → pingable (undefined = not checked yet).
  const [online, setOnline] = useState<Record<number, boolean>>({});

  async function load(includeHidden = showHidden) {
    try {
      setRows(await getAllDevices(includeHidden));
    } catch (err) {
      void messageDialog("Failed to load devices.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load(showHidden);
    // Versions/last-seen move slowly — a gentle refresh keeps the page current.
    const t = setInterval(() => getAllDevices(showHidden).then(setRows).catch(() => {}), 30000);
    return () => clearInterval(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showHidden]);

  // Remote-screens lookup (TOUCH-IN / TOUCH-OUT service-bridge targets).
  useEffect(() => {
    getConfig().then(setConfig).catch(() => {});
  }, []);

  // Live online dots — the server pings every device's last-known IP in parallel.
  useEffect(() => {
    const ping = () => pingDevices(true).then(setOnline).catch(() => {});
    ping();
    const t = setInterval(ping, 10000);
    return () => clearInterval(t);
  }, []);

  const selected = rows.find((r) => r.id === selectedId) ?? null;

  // Service-bridge screen matching this device (by name), else the API device bridge.
  function wsUrlFor(d: SystemDevice): string {
    const screen = config?.remoteScreens?.find(
      (s) => s.name.toLowerCase() === (d.deviceName ?? "").toLowerCase(),
    );
    return screen ? resolveVncWsUrl(screen.wsUrl) : deviceVncWsUrl(d.id);
  }

  async function run(fn: () => Promise<void>) {
    setBusy(true);
    try {
      await fn();
      await load();
    } catch (err) {
      void messageDialog("Failed.\n" + (err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const onDelete = async () => {
    if (!selected) return;
    if (!(await confirmDialog(
      `Permanently delete "${selected.deviceName ?? `device ${selected.id}`}"?\n` +
      "This is a hard delete — the row is gone; the device re-registers if its app runs again.",
    ))) return;
    void run(async () => {
      await deleteDevices([selected.id]);
      setSelectedId(null);
    });
  };

  const onToggleHidden = () => {
    if (!selected) return;
    void run(() => setDeviceHidden(selected.id, !selected.hidden));
  };

  const dot = (id: number) => {
    const state = online[id];
    const cls = state === undefined ? "dot-unknown" : state ? "dot-on" : "dot-off";
    const title = state === undefined ? "Checking…" : state ? "Online" : "Offline";
    return <span className={`dot ${cls}`} title={title} />;
  };

  const columns: Column<SystemDevice>[] = [
    { key: "online", header: "", width: 26, fit: true, align: "center", render: (r) => dot(r.id) },
    { key: "deviceName", header: "Device", render: (r) => r.deviceName ?? "—" },
    { key: "applicationName", header: "Application", fit: true, render: (r) => r.applicationName ?? "—" },
    { key: "currentVersion", header: "Version", fit: true, render: (r) => r.currentVersion ?? "—" },
  ];

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title="Devices" meta={`${rows.length} devices`} />
        <div className={cc.body}>
      <div className="po-open-layout system-devices">
        <div className="po-main">
          <DataTable
            columns={columns}
            rows={rows}
            rowKey={(r) => r.id}
            selectedKey={selectedId}
            onSelect={(r) => setSelectedId(r.id)}
            rowClass={(r) => (r.hidden ? "sd-hidden-row" : undefined)}
            emptyText={loading ? "Loading…" : "No devices registered"}
          />
          {dev && (
            <div className="toolbar">
              <button className="btn-dev" disabled={busy || !selected} onClick={() => void onDelete()}>
                Delete
              </button>
              <button className="btn-dev" disabled={busy || !selected} onClick={onToggleHidden}>
                {selected?.hidden ? "Unhide" : "Hide"}
              </button>
              <span className="spacer" />
              <label className="dev-check">
                <input
                  type="checkbox"
                  checked={showHidden}
                  onChange={(e) => setShowHidden(e.target.checked)}
                />
                Show hidden
              </label>
            </div>
          )}
        </div>

        <aside className="po-side dev-detail">
          {selected ? (
            <>
              <div className="dev-detail-head">
                {dot(selected.id)}
                <h3 className="dev-detail-name">{selected.deviceName || "(unnamed device)"}</h3>
              </div>
              <dl className="dev-detail-info">
                <dt>Application</dt><dd>{selected.applicationName ?? "—"}</dd>
                <dt>Version</dt><dd>{selected.currentVersion ?? "—"}</dd>
                <dt>IP address</dt><dd>{selected.ipAddress ?? "—"}</dd>
                <dt>Last seen</dt><dd>{fmtDateTime(selected.lastSeen)}</dd>
              </dl>
              <div className="dev-btn-row">
                <button className="btn-primary" onClick={() => setConnecting(true)}>
                  Remote Connect
                </button>
              </div>
            </>
          ) : (
            <div className="dev-detail-empty">Select a device to view details</div>
          )}
        </aside>
      </div>
        </div>
      </div>

      {connecting && selected && (
        <RemoteScreenModal
          screen={{
            name: selected.deviceName || `Device ${selected.id}`,
            wsUrl: wsUrlFor(selected),
          }}
          onClose={() => setConnecting(false)}
        />
      )}
    </div>
  );
}
