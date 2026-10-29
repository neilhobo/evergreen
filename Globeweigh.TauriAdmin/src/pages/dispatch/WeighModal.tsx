import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import { weighIn, weighOut } from "../../api/dispatch";
import { getLiveWeight } from "../../api/gateBarrier";

const WEIGHT_POLL_MS = 1000;

interface Props {
  recordId: number;
  mode: "in" | "out";
  onClose: (saved: boolean) => void;
}

// Weigh-in / weigh-out: shows the live weighbridge reading (polled like the
// Weighbridge Records page) with a "Use live weight" button + manual entry.
export default function WeighModal({ recordId, mode, onClose }: Props) {
  const [live, setLive] = useState("—");
  const [liveStale, setLiveStale] = useState(true);
  const [weight, setWeight] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      const r = await getLiveWeight();
      if (cancelled) return;
      if (r && r.isConnected && r.ageMs >= 0 && r.ageMs < 5000) {
        setLive(r.weight || "0");
        setLiveStale(false);
      } else {
        setLiveStale(true);
      }
    };
    void tick();
    const id = setInterval(tick, WEIGHT_POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);

  function useLive() {
    if (liveStale) return;
    setWeight(String(Math.round(Number(live)) || 0));
  }

  async function onSave() {
    const w = parseInt(weight, 10);
    if (!Number.isFinite(w) || weight.trim() === "") {
      void messageDialog("Please enter a weight.");
      return;
    }
    setBusy(true);
    try {
      if (mode === "in") await weighIn(recordId, w);
      else await weighOut(recordId, w);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={mode === "in" ? "Weigh In" : "Weigh Out"}
      width={440}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div className="form-field">
        <label className="form-label">Live Weight</label>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <span className={`wb-weight-value${liveStale ? " stale" : ""}`} style={{ fontSize: 22 }}>
            {live}
          </span>
          <span className="wb-weight-unit">Kg</span>
          <span className="spacer" />
          <button className="toolbar-btn" disabled={liveStale} onClick={useLive}>
            Use live weight
          </button>
        </div>
      </div>
      <div className="form-field">
        <label className="form-label">Weight (Kg) *</label>
        <input
          className="form-input"
          type="number"
          autoFocus
          value={weight}
          onChange={(e) => setWeight(e.target.value)}
        />
      </div>
    </Modal>
  );
}
