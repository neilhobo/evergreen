import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import {
  getCostingZones,
  setCustomerCostingZone,
  CustomerSupplier,
  CostingZoneRef,
} from "../../api/customersSuppliers";

interface Props {
  customer: CustomerSupplier;
  onClose: (saved: boolean) => void;
}

// Specific to the dispatch "customer has no costing zone" prompt: assign a costing
// zone to the customer without touching any other field. The customer name is shown
// read-only; only the zone can be changed.
export default function SetCostingZoneModal({ customer, onClose }: Props) {
  const [zones, setZones] = useState<CostingZoneRef[]>([]);
  const [costingZoneId, setCostingZoneId] = useState(customer.costingZoneId || 0);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void (async () => {
      try {
        const z = await getCostingZones();
        setZones(z);
      } catch (err) {
        void messageDialog("Failed to load costing zones.\n" + (err as Error).message);
      }
    })();
  }, []);

  async function onSave() {
    if (costingZoneId <= 0) {
      void messageDialog("Select a costing zone.");
      return;
    }
    setBusy(true);
    try {
      await setCustomerCostingZone(customer.id, costingZoneId);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title="Edit Costing Zone"
      width={460}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || costingZoneId <= 0} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div className="form-col">
        <div className="form-field form-field-inline">
          <label className="form-label">Customer</label>
          <span className="form-static">{customer.name}</span>
        </div>
        <div className="form-field form-field-inline">
          <label className="form-label">Costing Zone</label>
          <select
            className="form-input"
            value={costingZoneId}
            autoFocus
            onChange={(e) => setCostingZoneId(Number(e.target.value))}
          >
            <option value={0}>— Select —</option>
            {zones.map((z) => (
              <option key={z.id} value={z.id}>
                {z.description}
              </option>
            ))}
          </select>
        </div>
      </div>
    </Modal>
  );
}
