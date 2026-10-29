import { useEffect, useState } from "react";
import { DeviceRow, deviceVncWsUrl, saveDeviceRemote } from "../../api/devices";
import RemoteScreenModal from "../weighbridge/RemoteScreenModal";
import {
  adbConnect,
  adbDevices,
  adbPair,
  adbTcpip,
  canRemote,
  scrcpyView,
  serialOf,
} from "../../api/scrcpy";

function fmtDateTime(s: string | null): string {
  if (!s) return "—";
  const d = new Date(s);
  return isNaN(d.getTime()) ? "—" : d.toLocaleString();
}

// Left-hand detail + remote-view setup for the device selected in the grid.
// Remote view is VNC: droidVNC-NG runs on the tablet, the API bridges
// browser-WebSocket→device-TCP (see DeviceVncProxy), and the embedded noVNC modal
// renders it — same pattern as the weighbridge Remote Connect, works from anywhere
// the admin UI runs. The older adb/scrcpy tooling is kept underneath as
// provisioning-time tools (they run adb locally, desktop shell only).
export default function DeviceDetailPanel({
  device,
  online,
  onSaved,
}: {
  device: DeviceRow | null;
  online: boolean | undefined;
  onSaved: () => void;
}) {
  const [host, setHost] = useState("");
  const [port, setPort] = useState("");
  const [notes, setNotes] = useState("");
  const [adbPort, setAdbPort] = useState("");
  const [pairPort, setPairPort] = useState("");
  const [pairCode, setPairCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState("");
  const [viewing, setViewing] = useState(false);

  // Reload form fields whenever a different device is selected (keyed on id, so the grid's
  // 5s auto-refresh doesn't clobber what the user is typing for the same device).
  useEffect(() => {
    setHost(device?.remoteHost ?? "");
    setPort(device?.remotePort != null ? String(device.remotePort) : "");
    setNotes(device?.remoteNotes ?? "");
    setAdbPort("");
    setPairPort("");
    setPairCode("");
    setStatus("");
    setViewing(false);
  }, [device?.id]);

  if (!device) {
    return (
      <aside className="po-side dev-detail">
        <div className="dev-detail-empty">Select a device to view details</div>
      </aside>
    );
  }

  const effectiveHost = host.trim() || device.ipAddress || "";
  const hasHost = effectiveHost !== "";
  const effectiveAdbPort = parseInt(adbPort, 10) || 5555;
  const serial = serialOf(effectiveHost, effectiveAdbPort);

  async function run(label: string, fn: () => Promise<{ output: string } | void>) {
    setBusy(true);
    setStatus(`${label}…`);
    try {
      const res = await fn();
      setStatus(res && "output" in res ? `${label}: ${res.output}` : `${label} ✓`);
    } catch (e) {
      setStatus(`${label} failed: ${e instanceof Error ? e.message : String(e)}`);
    } finally {
      setBusy(false);
    }
  }

  const onSave = () =>
    run("Saved", async () => {
      await saveDeviceRemote(device.id, {
        remoteHost: host.trim() || null,
        remotePort: port.trim() ? parseInt(port, 10) : null,
        remoteNotes: notes.trim() || null,
      });
      onSaved();
    });

  const onPair = () =>
    run("Pair", () => adbPair(effectiveHost, parseInt(pairPort, 10), pairCode.trim()));
  const onConnect = () => run("Connect", () => adbConnect(effectiveHost, effectiveAdbPort));
  const onEnableUsb = () => run("Enable wireless (USB)", () => adbTcpip());
  const onAdbDevices = () => run("adb devices", () => adbDevices());
  const onScrcpy = () => run("Launching scrcpy", () => scrcpyView(serial, device.deviceName ?? undefined));

  const dot = online === undefined ? "dot-unknown" : online ? "dot-on" : "dot-off";
  const dotTitle = online === undefined ? "Checking…" : online ? "Online" : "Offline";

  return (
    <aside className="po-side dev-detail">
      <div className="dev-detail-head">
        <span className={`dot ${dot}`} title={dotTitle} />
        <h3 className="dev-detail-name">{device.deviceName || "(unnamed device)"}</h3>
      </div>

      <dl className="dev-detail-info">
        <dt>Platform</dt><dd>{device.platform ?? "—"}</dd>
        <dt>Version</dt><dd>{device.currentVersion ?? "—"}</dd>
        <dt>Patch</dt><dd>{device.currentPatch ?? "—"}</dd>
        <dt>IP address</dt><dd>{device.ipAddress ?? "—"}</dd>
        <dt>Last seen</dt><dd>{fmtDateTime(device.lastSeen)}</dd>
        <dt>Push</dt><dd>{device.latestPushStatus ?? "—"}</dd>
      </dl>

      <div className="dev-detail-section">
        <h4 className="dev-detail-title">Remote view (VNC)</h4>

        <div className="dev-field">
          <label className="form-label">Host</label>
          <input
            className="form-input"
            value={host}
            placeholder={device.ipAddress ?? "device IP"}
            onChange={(e) => setHost(e.target.value)}
          />
        </div>
        <div className="dev-field">
          <label className="form-label">VNC port</label>
          <input
            className="form-input"
            type="number"
            value={port}
            placeholder="5900"
            onChange={(e) => setPort(e.target.value)}
          />
        </div>

        <div className="dev-btn-row">
          <button className="btn-primary" disabled={busy || !hasHost} onClick={() => setViewing(true)}>
            Remote View
          </button>
        </div>
        <p className="dev-detail-hint">
          Needs droidVNC-NG running on the device (provisioned once per device). The
          connection goes via the API server, so it works from anywhere the admin runs.
        </p>

        <div className="dev-field dev-field-col">
          <label className="form-label">Notes</label>
          <textarea className="form-input" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </div>

        <div className="dev-btn-row">
          <button className="toolbar-btn" disabled={busy} onClick={onSave}>Save</button>
        </div>

        <details className="dev-pair">
          <summary>Provisioning tools (adb / scrcpy)</summary>

          <p className="dev-detail-hint">
            These run adb/scrcpy on this PC — desktop app only. Used for one-time device
            provisioning (see Globeweigh.Picking/scripts/provision-vnc.ps1 for the full setup).
          </p>

          <div className="dev-setup-method">
            <div className="dev-setup-label">Android 9 / 10 — USB once</div>
            <p className="dev-detail-hint">
              Plug the device into this PC by USB and accept the debugging prompt on the device, then:
            </p>
            <div className="dev-btn-row">
              <button className="toolbar-btn" disabled={busy || !canRemote} onClick={onAdbDevices}>
                Check USB
              </button>
              <button className="toolbar-btn" disabled={busy || !canRemote} onClick={onEnableUsb}
                title="adb tcpip 5555 — switch the USB device to wireless on port 5555">
                Enable wireless (USB)
              </button>
            </div>
          </div>

          <div className="dev-setup-method">
            <div className="dev-setup-label">Android 11+ — Wireless debugging</div>
            <div className="dev-field">
              <label className="form-label">Pair port</label>
              <input className="form-input" type="number" value={pairPort} onChange={(e) => setPairPort(e.target.value)} />
            </div>
            <div className="dev-field">
              <label className="form-label">Pair code</label>
              <input className="form-input" value={pairCode} onChange={(e) => setPairCode(e.target.value)} />
            </div>
            <div className="dev-btn-row">
              <button
                className="toolbar-btn"
                disabled={busy || !canRemote || !hasHost || !pairPort || !pairCode.trim()}
                onClick={onPair}
              >
                Pair
              </button>
            </div>
          </div>

          <div className="dev-setup-method">
            <div className="dev-setup-label">ADB connect / scrcpy fallback</div>
            <div className="dev-field">
              <label className="form-label">ADB port</label>
              <input
                className="form-input"
                type="number"
                value={adbPort}
                placeholder="5555"
                onChange={(e) => setAdbPort(e.target.value)}
              />
            </div>
            <div className="dev-btn-row">
              <button className="toolbar-btn" disabled={busy || !canRemote || !hasHost} onClick={onConnect}>
                Connect
              </button>
              <button className="toolbar-btn" disabled={busy || !canRemote || !hasHost} onClick={onScrcpy}>
                scrcpy View
              </button>
            </div>
          </div>

          {!canRemote && (
            <div className="dev-detail-hint">Open the desktop app to use the adb tools.</div>
          )}
        </details>

        {status && <div className="dev-detail-status">{status}</div>}
      </div>

      {viewing && (
        <RemoteScreenModal
          screen={{
            name: device.deviceName || `Device ${device.id}`,
            wsUrl: deviceVncWsUrl(device.id),
          }}
          onClose={() => setViewing(false)}
        />
      )}
    </aside>
  );
}
