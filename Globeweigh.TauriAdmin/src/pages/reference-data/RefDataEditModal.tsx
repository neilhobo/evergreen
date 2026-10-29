import { useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import {
  createReferenceData,
  updateReferenceData,
  RefDataInput,
  ReferenceDataItem,
} from "../../api/referenceData";

interface Props {
  existing: ReferenceDataItem | null; // null = new
  refTypeId: number;
  onClose: (saved: boolean) => void;
}

// Add/edit a reference-data item.
export default function RefDataEditModal({ existing, refTypeId, onClose }: Props) {
  const isNew = existing === null;
  const [form, setForm] = useState<RefDataInput>({
    refTypeId: existing?.refTypeId ?? refTypeId,
    code: existing?.code ?? "",
    displayName: existing?.displayName ?? "",
    alternativeDisplayName: existing?.alternativeDisplayName ?? "",
  });
  const [busy, setBusy] = useState(false);

  function set<K extends keyof RefDataInput>(key: K, value: RefDataInput[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function onSave() {
    if (!form.displayName?.trim()) {
      void messageDialog("Display Name is required.");
      return;
    }
    setBusy(true);
    try {
      if (isNew) await createReferenceData(form);
      else await updateReferenceData(existing!.id, form);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={`${isNew ? "Add New" : "Edit"} Reference Data`}
      width={520}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || !form.displayName?.trim()} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div className="form-cols">
        <div className="form-col">
          <div className="form-field">
            <label className="form-label">Code</label>
            <input className="form-input" value={form.code ?? ""} onChange={(e) => set("code", e.target.value)} />
          </div>
          <div className="form-field">
            <label className="form-label">Display Name *</label>
            <input
              className="form-input"
              value={form.displayName ?? ""}
              autoFocus
              onChange={(e) => set("displayName", e.target.value)}
            />
          </div>
          <div className="form-field">
            <label className="form-label">Alt Name</label>
            <input
              className="form-input"
              value={form.alternativeDisplayName ?? ""}
              onChange={(e) => set("alternativeDisplayName", e.target.value)}
            />
          </div>
        </div>
      </div>
    </Modal>
  );
}
