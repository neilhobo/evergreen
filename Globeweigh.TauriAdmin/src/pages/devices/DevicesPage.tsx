import { useEffect, useRef, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import { AdminUserDto } from "../../types";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import DeviceDetailPanel from "./DeviceDetailPanel";
import AddDeviceModal from "./AddDeviceModal";
import {
  DeviceRow,
  PackageInfo,
  getDevices,
  getPackages,
  pingDevices,
  uploadPackage,
  pushPackage,
  cancelDevicePush,
  deleteDevices,
} from "../../api/devices";

function fmtDateTime(s: string | null): string {
  if (!s) return "—";
  const d = new Date(s);
  return isNaN(d.getTime()) ? "—" : d.toLocaleString();
}

function fmtSize(bytes: number): string {
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// Mobile-app deployment console (dev-only). Lists registered Picking devices, uploads
// release APKs, and pushes a release to the per-device update queue the app polls.
export default function DevicesPage(_props: { user: AdminUserDto }) {
  const [devices, setDevices] = useState<DeviceRow[]>([]);
  const [activeId, setActiveId] = useState<number | null>(null);
  const [online, setOnline] = useState<Record<number, boolean>>({});
  const [packages, setPackages] = useState<PackageInfo[]>([]);
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [selectedPackage, setSelectedPackage] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [showAdd, setShowAdd] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  async function load() {
    const [d, p] = await Promise.all([getDevices(), getPackages()]);
    setDevices(d);
    setPackages(p);
    setSelectedPackage((cur) => cur || (p[0]?.fileName ?? ""));
  }

  useEffect(() => {
    // Background auto-refresh every 5s: devices + releases (push status, versions, newly
    // uploaded APKs) and a ping for the Online column. Each runs at most one request at a
    // time so a slow server or unreachable tablets can't stack calls up, and ticks are
    // skipped while the window is hidden.
    let loading = false;
    let pinging = false;
    const refresh = () => {
      if (loading) return;
      loading = true;
      load()
        .catch(() => {})
        .finally(() => (loading = false));
    };
    const ping = () => {
      if (pinging) return;
      pinging = true;
      pingDevices()
        .then(setOnline)
        .catch(() => {})
        .finally(() => (pinging = false));
    };
    load().catch((e) => setMessage(String(e)));
    ping();
    const t = setInterval(() => {
      if (document.hidden) return;
      refresh();
      ping();
    }, 5000);
    return () => clearInterval(t);
  }, []);

  function toggle(id: number) {
    setSelectedIds((prev) => {
      const n = new Set(prev);
      if (n.has(id)) n.delete(id);
      else n.add(id);
      return n;
    });
  }

  const allSelected = devices.length > 0 && selectedIds.size === devices.length;
  function toggleAll() {
    setSelectedIds(allSelected ? new Set() : new Set(devices.map((d) => d.id)));
  }

  async function onUploadFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (fileRef.current) fileRef.current.value = "";
    if (!file) return;
    setBusy(true);
    setMessage(`Uploading ${file.name}…`);
    try {
      const bytes = new Uint8Array(await file.arrayBuffer());
      const pkg = await uploadPackage(file.name, bytes);
      await load();
      setSelectedPackage(pkg.fileName);
      setMessage(`Uploaded ${pkg.fileName} (v${pkg.version ?? "?"})`);
    } catch (err) {
      setMessage(`Upload failed: ${err}`);
    } finally {
      setBusy(false);
    }
  }

  async function onPush() {
    if (!selectedPackage || selectedIds.size === 0) return;
    const pkg = packages.find((p) => p.fileName === selectedPackage);
    const ok = await confirmDialog(
      `Push ${selectedPackage} (v${pkg?.version ?? "?"}) to ${selectedIds.size} device(s)?`,
    );
    if (!ok) return;
    setBusy(true);
    try {
      await pushPackage(selectedPackage, pkg?.version ?? null, [...selectedIds]);
      setSelectedIds(new Set());
      await load();
      setMessage(`Queued ${selectedPackage} to selected devices.`);
    } catch (err) {
      setMessage(`Push failed: ${err}`);
    } finally {
      setBusy(false);
    }
  }

  async function onCancelRow(rowId: number) {
    await cancelDevicePush(rowId);
    await load();
  }

  async function onDelete() {
    if (selectedIds.size === 0) return;
    if (!(await confirmDialog(`Delete ${selectedIds.size} device record(s)?`))) return;
    setBusy(true);
    try {
      await deleteDevices([...selectedIds]);
      setSelectedIds(new Set());
      await load();
    } catch (err) {
      void messageDialog(`Failed to delete.\n${err}`);
    } finally {
      setBusy(false);
    }
  }

  const cols: Column<DeviceRow>[] = [
    {
      key: "pick",
      header: <input type="checkbox" checked={allSelected} onChange={toggleAll} />,
      width: 34,
      fit: true,
      align: "center",
      render: (r) => (
        <input
          type="checkbox"
          checked={selectedIds.has(r.id)}
          onChange={() => toggle(r.id)}
          onClick={(e) => e.stopPropagation()}
        />
      ),
    },
    { key: "deviceName", header: "Device" },
    {
      key: "online",
      header: "Online",
      width: 64,
      fit: true,
      align: "center",
      render: (r) => {
        const state = online[r.id];
        const cls = state === undefined ? "dot-unknown" : state ? "dot-on" : "dot-off";
        const title = state === undefined ? "Checking…" : state ? "Online" : "Offline";
        return <span className={`dot ${cls}`} title={title} />;
      },
    },
    { key: "currentVersion", header: "Version", width: 90, fit: true },
    {
      // Which Shorebird patch the app is on: the version stays put across patches, so this
      // is the only way to see from here that a tablet has taken one.
      key: "currentPatch",
      header: "Patch",
      width: 60,
      fit: true,
      align: "center",
      render: (r) => (r.currentPatch == null ? "—" : String(r.currentPatch)),
    },
    { key: "ipAddress", header: "IP Address", width: 130, fit: true },
    { key: "lastSeen", header: "Last Seen", width: 160, fit: true, render: (r) => fmtDateTime(r.lastSeen) },
    {
      key: "latestPushStatus",
      header: "Push Status",
      width: 120,
      fit: true,
      render: (r) =>
        r.latestPushStatus ? (
          <span className={`push-status ps-${r.latestPushStatus.toLowerCase().replace(/\s+/g, "")}`}>
            {r.latestPushStatus}
          </span>
        ) : (
          "—"
        ),
    },
    { key: "latestPushDetail", header: "Detail", render: (r) => r.latestPushDetail ?? "" },
    {
      key: "cancel",
      header: "",
      width: 70,
      fit: true,
      align: "center",
      render: (r) =>
        r.latestPushStatus === "Pending" ? (
          <button
            className="toolbar-btn"
            onClick={(e) => {
              e.stopPropagation();
              void onCancelRow(r.latestPushRowId);
            }}
          >
            Cancel
          </button>
        ) : null,
    },
  ];

  const activeDevice = devices.find((d) => d.id === activeId) ?? null;

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title="Device Manager" meta={`${devices.length} devices`} />
        <div className={cc.body}>
      <div className="po-open-layout">
        <div className="po-main">
        <div className="toolbar">
          <span className="toolbar-note">{message}</span>
          <div className="toolbar-right">
            <button className="toolbar-btn" disabled={busy} onClick={() => void load()}>
              Refresh
            </button>
            <button className="toolbar-btn" disabled={busy} onClick={() => setShowAdd(true)}>
              Add Device
            </button>
            <button className="toolbar-btn" disabled={busy} onClick={() => fileRef.current?.click()}>
              Upload APK
            </button>
            <input
              ref={fileRef}
              type="file"
              accept=".apk"
              style={{ display: "none" }}
              onChange={onUploadFile}
            />
          </div>
        </div>

        <DataTable
          columns={cols}
          rows={devices}
          rowKey={(r) => r.id}
          onSelect={(r) => setActiveId(r.id)}
          rowClass={(r) =>
            [selectedIds.has(r.id) ? "selected" : "", r.id === activeId ? "active" : ""]
              .filter(Boolean)
              .join(" ") || undefined
          }
          emptyText="No Picking devices registered yet"
        />

        <div className="toolbar">
          <label className="form-label" style={{ alignSelf: "center" }}>
            Release:
          </label>
          <select
            className="form-input"
            style={{ width: 340 }}
            value={selectedPackage}
            onChange={(e) => setSelectedPackage(e.target.value)}
          >
            {packages.length === 0 && <option value="">No releases uploaded</option>}
            {packages.map((p) => (
              <option key={p.fileName} value={p.fileName}>
                {p.fileName} — v{p.version ?? "?"} ({fmtSize(p.sizeBytes)})
              </option>
            ))}
          </select>
          <div className="toolbar-right">
            <button
              className="btn-primary"
              disabled={busy || !selectedPackage || selectedIds.size === 0}
              onClick={onPush}
            >
              Push to {selectedIds.size} device{selectedIds.size === 1 ? "" : "s"}
            </button>
            <button
              className="toolbar-btn"
              disabled={busy || selectedIds.size === 0}
              onClick={onDelete}
            >
              Delete
            </button>
          </div>
        </div>
        </div>
        <DeviceDetailPanel
          device={activeDevice}
          online={activeId != null ? online[activeId] : undefined}
          onSaved={load}
        />
      </div>
        </div>
      </div>

      {showAdd && (
        <AddDeviceModal
          onClose={() => setShowAdd(false)}
          onAdded={(d) => {
            setActiveId(d.id);
            void load();
          }}
        />
      )}
    </div>
  );
}
