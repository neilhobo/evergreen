import { useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import { updateZonePrice, CostingZonePrice } from "../../api/costing";

interface Props {
  existing: CostingZonePrice;
  onClose: (saved: boolean) => void;
}

// Edit a single costing zone price, with price-history audit on the server.
export default function ZonePriceEditModal({ existing, onClose }: Props) {
  const [price, setPrice] = useState<string>(existing.price.toFixed(2));
  const [na, setNa] = useState<boolean>(existing.na);
  const [busy, setBusy] = useState(false);

  async function onSave() {
    const parsed = Number(price);
    if (Number.isNaN(parsed)) {
      void messageDialog("Price must be a number.");
      return;
    }
    setBusy(true);
    try {
      await updateZonePrice(existing.id, { price: parsed, na });
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title="Edit Zone Price"
      width={420}
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
      <div className="form-col">
        <div className="form-field">
          <label className="form-label">Zone</label>
          <input
            className="form-input"
            value={existing.descriptionDisplay || existing.description || ""}
            readOnly
          />
        </div>
        <div className="form-field">
          <label className="form-label">Price</label>
          <input
            className="form-input"
            type="number"
            step="0.01"
            value={price}
            autoFocus
            disabled={na}
            onChange={(e) => setPrice(e.target.value)}
          />
        </div>
        <label className="stock-radio" style={{ marginTop: 6 }}>
          <input type="checkbox" checked={na} onChange={(e) => setNa(e.target.checked)} />
          N/A
        </label>
      </div>
    </Modal>
  );
}
