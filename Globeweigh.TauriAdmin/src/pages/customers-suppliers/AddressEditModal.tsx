import { useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import {
  createAddress,
  updateAddress,
  AddressInput,
  CustomerAddress,
} from "../../api/customersSuppliers";

interface Props {
  csId: number;
  existing: CustomerAddress | null; // null = new
  onClose: (saved: boolean) => void;
}

// Add/edit an address for a customer/supplier.
export default function AddressEditModal({ csId, existing, onClose }: Props) {
  const isNew = existing === null;
  const [form, setForm] = useState<AddressInput>({
    addressName: existing?.addressName ?? "",
    address1: existing?.address1 ?? "",
    address2: existing?.address2 ?? "",
    address3: existing?.address3 ?? "",
    address4: existing?.address4 ?? "",
    address5: existing?.address5 ?? "",
    telephone: existing?.telephone ?? "",
    telephone2: existing?.telephone2 ?? "",
    email: existing?.email ?? "",
    contactName: existing?.contactName ?? "",
  });
  const [busy, setBusy] = useState(false);

  function set<K extends keyof AddressInput>(key: K, value: AddressInput[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function onSave() {
    setBusy(true);
    try {
      if (isNew) await createAddress(csId, form);
      else await updateAddress(existing!.id, form);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={`${isNew ? "Add New" : "Edit"} Address`}
      width={560}
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
      <div className="form-cols">
        <div className="form-col">
          <div className="form-field">
            <label className="form-label">Address Name</label>
            <input
              className="form-input"
              value={form.addressName ?? ""}
              autoFocus
              onChange={(e) => set("addressName", e.target.value)}
            />
          </div>
          <div className="form-field">
            <label className="form-label">Address 1</label>
            <input
              className="form-input"
              value={form.address1 ?? ""}
              onChange={(e) => set("address1", e.target.value)}
            />
          </div>
          <div className="form-field">
            <label className="form-label">Address 2</label>
            <input
              className="form-input"
              value={form.address2 ?? ""}
              onChange={(e) => set("address2", e.target.value)}
            />
          </div>
          <div className="form-field">
            <label className="form-label">Address 3</label>
            <input
              className="form-input"
              value={form.address3 ?? ""}
              onChange={(e) => set("address3", e.target.value)}
            />
          </div>
          <div className="form-field">
            <label className="form-label">Address 4</label>
            <input
              className="form-input"
              value={form.address4 ?? ""}
              onChange={(e) => set("address4", e.target.value)}
            />
          </div>
          <div className="form-field">
            <label className="form-label">Address 5</label>
            <input
              className="form-input"
              value={form.address5 ?? ""}
              onChange={(e) => set("address5", e.target.value)}
            />
          </div>
        </div>

        <div className="form-col">
          <div className="form-field">
            <label className="form-label">Telephone</label>
            <input
              className="form-input"
              value={form.telephone ?? ""}
              onChange={(e) => set("telephone", e.target.value)}
            />
          </div>
          <div className="form-field">
            <label className="form-label">Telephone 2</label>
            <input
              className="form-input"
              value={form.telephone2 ?? ""}
              onChange={(e) => set("telephone2", e.target.value)}
            />
          </div>
          <div className="form-field">
            <label className="form-label">Email</label>
            <input
              className="form-input"
              value={form.email ?? ""}
              onChange={(e) => set("email", e.target.value)}
            />
          </div>
          <div className="form-field">
            <label className="form-label">Contact</label>
            <input
              className="form-input"
              value={form.contactName ?? ""}
              onChange={(e) => set("contactName", e.target.value)}
            />
          </div>
        </div>
      </div>
    </Modal>
  );
}
