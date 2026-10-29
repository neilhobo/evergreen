import { useState } from "react";
import Modal from "../../components/Modal";
import { ApiError } from "../../api/client";
import { DeviceRow, addManualDevice } from "../../api/devices";

// Add a device by name + IP for one that has never run the app (so never self-registered).
// The new row can then be pinged and pushed a release like any registered device.
export default function AddDeviceModal({
  onClose,
  onAdded,
}: {
  onClose: () => void;
  onAdded: (device: DeviceRow) => void;
}) {
  const [name, setName] = useState("");
  const [ip, setIp] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const canAdd = !busy && name.trim() !== "" && ip.trim() !== "";

  async function onAdd() {
    if (!canAdd) return;
    setBusy(true);
    setError(null);
    try {
      const device = await addManualDevice(name.trim(), ip.trim());
      onAdded(device);
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to add device.");
      setBusy(false);
    }
  }

  return (
    <Modal
      title="Add Device"
      width={460}
      onClose={onClose}
      footer={
        <>
          <button className="toolbar-btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-primary" disabled={!canAdd} onClick={onAdd}>
            {busy ? "Adding…" : "Add"}
          </button>
        </>
      }
    >
      <p className="dev-detail-hint" style={{ marginTop: 0 }}>
        For a device that has never run the app. It will appear in the grid so you can ping
        and push a release. If it later runs the app it merges on the device name.
      </p>

      <div className="form-field form-field-inline" style={{ marginTop: 8 }}>
        <label className="form-label">Device name</label>
        <input
          className="form-input"
          value={name}
          autoFocus
          placeholder="e.g. TC72-WAREHOUSE-1"
          onChange={(e) => setName(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && onAdd()}
        />
      </div>

      <div className="form-field form-field-inline" style={{ marginTop: 8 }}>
        <label className="form-label">IP address</label>
        <input
          className="form-input"
          value={ip}
          placeholder="e.g. 192.168.250.40"
          onChange={(e) => setIp(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && onAdd()}
        />
      </div>

      {error && <p style={{ color: "var(--error-text)", marginTop: 12 }}>{error}</p>}
    </Modal>
  );
}
