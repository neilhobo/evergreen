import { useEffect, useState } from "react";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import { getOrderNumbering, setOrderNumbering, OrderNumbering } from "../../api/settings";

// Admin > Settings. One section so far: order numbering, which carries on the
// incremental order number from the system they ran before this one. Until it is
// switched on, orders are numbered as they always have been — highest so far + 1 —
// so the hand-over number can be entered ahead of the day and turned on on the day.
export default function SettingsPanel() {
  const [loaded, setLoaded] = useState<OrderNumbering | null>(null);
  const [value, setValue] = useState("");
  const [enabled, setEnabled] = useState(false);
  const [busy, setBusy] = useState(false);

  function apply(s: OrderNumbering) {
    setLoaded(s);
    setValue(s.nextOrderNo === null ? "" : String(s.nextOrderNo));
    setEnabled(s.enabled);
  }

  async function load() {
    try {
      apply(await getOrderNumbering());
    } catch (err) {
      void messageDialog("Failed to load settings.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const text = value.trim();
  const parsed = /^\d+$/.test(text) ? parseInt(text, 10) : NaN;
  const number = Number.isInteger(parsed) ? parsed : null;
  const inRange = number !== null && number >= 1 && number <= 99_999_999;
  // Re-using a number that is already on an order — and on a printed delivery note —
  // is the one thing that can't be undone, so the switch stays disabled below it.
  const clearsHighest = loaded !== null && number !== null && number > loaded.highestOrderNo;

  const numberValid = text === "" ? !enabled : inRange;
  const valid = numberValid && (!enabled || clearsHighest);
  const dirty =
    loaded !== null && (number !== loaded.nextOrderNo || enabled !== loaded.enabled);

  async function onSave() {
    if (!valid || !loaded) return;
    // Switching it on, or moving the number while it is on, changes what every new order
    // is numbered from — and a typo there jumps the series permanently. Worth a beat.
    if (enabled && (!loaded.enabled || number !== loaded.nextOrderNo)) {
      const ok = await confirmDialog(
        loaded.enabled
          ? `The next order created will be numbered ${number?.toLocaleString()}, instead of ${loaded.nextOrderNo?.toLocaleString()}.\n\nChange it?`
          : `New orders will be numbered from ${number?.toLocaleString()}, continuing the old system's numbering.\n\nSwitch over now?`,
      );
      if (!ok) return;
    }
    setBusy(true);
    try {
      apply(await setOrderNumbering(number, enabled));
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="po-main">
      <div style={{ padding: 18, maxWidth: 620 }}>
        <h2 style={{ fontSize: 15, fontWeight: 600, margin: "0 0 4px" }}>Order Numbering</h2>
        <p style={{ color: "#64748b", fontSize: 13, margin: "0 0 16px", lineHeight: 1.5 }}>
          Carries on the order number from the old system. Leave this switched off until
          they hand the number over — new orders keep numbering from the highest used here.
        </p>

        <div className="form-field">
          <label className="form-label">Next order number</label>
          <input
            className="form-input"
            style={{ width: 160 }}
            value={value}
            inputMode="numeric"
            placeholder="e.g. 48120"
            onChange={(e) => setValue(e.target.value)}
          />
        </div>

        <label className="stock-radio" style={{ marginTop: 10 }}>
          <input
            type="checkbox"
            checked={enabled}
            onChange={(e) => setEnabled(e.target.checked)}
          />
          Use this numbering for new orders
        </label>

        {text !== "" && !inRange && (
          <p style={{ color: "#dc2626", fontSize: 13, margin: "10px 0 0" }}>
            Enter a whole number between 1 and 99,999,999.
          </p>
        )}
        {enabled && text === "" && (
          <p style={{ color: "#dc2626", fontSize: 13, margin: "10px 0 0" }}>
            Enter the next order number before switching it on.
          </p>
        )}
        {enabled && inRange && !clearsHighest && loaded && (
          <p style={{ color: "#dc2626", fontSize: 13, margin: "10px 0 0" }}>
            Must be above {loaded.highestOrderNo.toLocaleString()} — the highest order
            number already used on this system.
          </p>
        )}

        <button
          className="btn-blue"
          style={{ width: 160, marginTop: 16 }}
          disabled={!dirty || !valid || busy}
          onClick={onSave}
        >
          {busy ? "Saving…" : "Save"}
        </button>

        {loaded && (
          <p style={{ color: "#64748b", fontSize: 13, marginTop: 16, lineHeight: 1.5 }}>
            {loaded.enabled && loaded.nextOrderNo !== null ? (
              <>
                <strong>On.</strong> The next order created takes number{" "}
                <strong>{loaded.nextOrderNo.toLocaleString()}</strong>, and it counts up from
                there.
              </>
            ) : (
              <>
                <strong>Off.</strong> Orders are numbered from the highest used here —{" "}
                {loaded.highestOrderNo.toLocaleString()}, so the next would be{" "}
                <strong>{(loaded.highestOrderNo + 1).toLocaleString()}</strong>.
              </>
            )}
            <br />
            The order number is what prints on the delivery note.
          </p>
        )}
      </div>
    </div>
  );
}
