import { useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import {
  createCostingZone,
  updateCostingZone,
  CostingZoneInput,
  CostingZoneRow,
} from "../../api/costingZones";

interface Props {
  existing: CostingZoneRow | null; // null = new
  onClose: (saved: boolean) => void;
}

type Currency = "euro" | "sterling" | "both";

// null = Both, true = Euro, false = Sterling.
function toCurrency(isEuro: boolean | null): Currency {
  if (isEuro === null) return "both";
  return isEuro ? "euro" : "sterling";
}
function fromCurrency(c: Currency): boolean | null {
  if (c === "both") return null;
  return c === "euro";
}

// Mirrors AddEditCostingZoneView — add/edit a costing zone.
export default function CostingZoneEditModal({ existing, onClose }: Props) {
  const isNew = existing === null;
  const [description, setDescription] = useState(existing?.description ?? "");
  const [secondaryDescription, setSecondaryDescription] = useState(
    existing?.secondaryDescription ?? "",
  );
  const [currency, setCurrency] = useState<Currency>(toCurrency(existing?.isEuro ?? null));
  const [busy, setBusy] = useState(false);

  async function onSave() {
    if (!description.trim()) {
      void messageDialog("Description is required.");
      return;
    }
    const input: CostingZoneInput = {
      description: description.trim(),
      secondaryDescription: secondaryDescription.trim() || null,
      isEuro: fromCurrency(currency),
    };
    setBusy(true);
    try {
      if (isNew) await createCostingZone(input);
      else await updateCostingZone(existing!.id, input);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={`${isNew ? "Add New" : "Edit"} Zone`}
      width={480}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || !description.trim()} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div className="form-cols">
        <div className="form-col">
          <div className="form-field">
            <label className="form-label">Description *</label>
            <input
              className="form-input"
              value={description}
              autoFocus
              onChange={(e) => setDescription(e.target.value)}
            />
          </div>
          <div className="form-field">
            <label className="form-label">Extra Info</label>
            <input
              className="form-input"
              value={secondaryDescription}
              onChange={(e) => setSecondaryDescription(e.target.value)}
            />
          </div>
          <div className="form-field">
            <label className="form-label">Currency</label>
            <div className="radio-row">
              {(["euro", "sterling", "both"] as Currency[]).map((c) => (
                <label key={c} className="stock-radio">
                  <input
                    type="radio"
                    name="zone-currency"
                    checked={currency === c}
                    onChange={() => setCurrency(c)}
                  />
                  {c === "euro" ? "Euro" : c === "sterling" ? "Sterling" : "Both"}
                </label>
              ))}
            </div>
          </div>
        </div>
      </div>
    </Modal>
  );
}
