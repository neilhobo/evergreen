import { useEffect, useState } from "react";
import { messageDialog } from "../../components/confirmDialog";
import { getBulkDensity, setBulkDensity } from "../../api/settings";

// Admin "Bulk Density" — edits the single system-wide standard density (kg/m³) held on
// dbo.Setting. The Touch Manual Stock module uses it to convert a weighed pallet's kg
// into sold volume: Volume (m³) = Weight (kg) ÷ Bulk Density (kg/m³).
export default function BulkDensityPanel() {
  const [value, setValue] = useState("");
  const [loaded, setLoaded] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);

  async function load() {
    try {
      const c = await getBulkDensity();
      setLoaded(c.bulkDensity);
      setValue(String(c.bulkDensity));
    } catch (err) {
      void messageDialog("Failed to load bulk density.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const parsed = parseFloat(value);
  const valid = Number.isFinite(parsed) && parsed > 0 && parsed <= 9999;
  const dirty = loaded !== null && valid && parsed !== loaded;

  async function onSave() {
    if (!valid) return;
    setBusy(true);
    try {
      const saved = await setBulkDensity(parsed);
      setLoaded(saved.bulkDensity);
      setValue(String(saved.bulkDensity));
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="po-main">
      <div style={{ padding: 18, maxWidth: 620 }}>
        <div className="form-field">
          <label className="form-label">Bulk Density (kg/m³)</label>
          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            <input
              className="form-input"
              style={{ width: 140 }}
              value={value}
              onChange={(e) => setValue(e.target.value)}
            />
            <button className="btn-blue" style={{ width: 140 }} disabled={!dirty || busy} onClick={onSave}>
              {busy ? "Saving…" : "Save"}
            </button>
          </div>
        </div>
        {!valid && value.trim() !== "" && (
          <p style={{ color: "#dc2626", fontSize: 13, margin: "6px 0 0" }}>
            Enter a number between 0 and 9999.
          </p>
        )}
        <p style={{ color: "#64748b", fontSize: 13, marginTop: 14, lineHeight: 1.5 }}>
          The standard bulk density used by Manual Stock on the Touch screens:
          <br />
          <strong>Volume (m³) = Weight (kg) ÷ Bulk Density (kg/m³)</strong>
          <br />
          Changing it affects the volume shown for pallets weighed from now on. Operators can
          also adjust it from the weigh screen.
        </p>
      </div>
    </div>
  );
}
