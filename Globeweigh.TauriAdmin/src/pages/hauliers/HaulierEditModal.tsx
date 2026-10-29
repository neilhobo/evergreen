import { useLayoutEffect, useRef, useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import EmailListEditor, { withPendingEmail } from "../../components/EmailList";
import { createHaulier, updateHaulier, HaulierInput, HaulierRow } from "../../api/hauliers";
import "./Hauliers.css";

interface Props {
  existing: HaulierRow | null; // null = new
  onClose: (saved: boolean) => void;
}

// Mirrors AddEditHaulierView — add/edit a haulier. Two tabs: General (the fields) and
// Emails (the haulier's addresses, any number). The emails are held here and saved with
// the record, so a new haulier can have its addresses before it exists, and Cancel drops
// every change.
export default function HaulierEditModal({ existing, onClose }: Props) {
  const isNew = existing === null;
  const [tab, setTab] = useState<"general" | "emails">("general");
  const [form, setForm] = useState<HaulierInput>({
    code: existing?.code ?? "",
    name: existing?.name ?? "",
    contactName: existing?.contactName ?? "",
    tel: existing?.tel ?? "",
    emails: existing?.emails ?? [],
    addressLine1: existing?.addressLine1 ?? "",
    addressLine2: existing?.addressLine2 ?? "",
    addressLine3: existing?.addressLine3 ?? "",
    addressLine4: existing?.addressLine4 ?? "",
    addressLine5: existing?.addressLine5 ?? "",
    vatNumber: existing?.vatNumber ?? "",
    nominalCode: existing?.nominalCode ?? "",
    vatCode: existing?.vatCode ?? "",
    intakeHaulier: existing?.intakeHaulier ?? false,
  });
  const [busy, setBusy] = useState(false);
  // A new haulier answers its type first, on a step of its own. The Intake flag is what
  // puts a haulier on the Touch Weigh In list, and on the plain form it was being saved
  // unticked and then reported as "the haulier is missing" from the weighbridge.
  const [step, setStep] = useState<"type" | "form">(isNew ? "type" : "form");
  // Emails tab: the address being typed (held here so Save picks it up).
  const [newEmail, setNewEmail] = useState("");

  // The dialog keeps one height whatever tab is showing: the tab area is measured once
  // while the General tab is up (the initial tab) and locked to that, so the Emails tab
  // fills and scrolls inside it instead of resizing the dialog. A new haulier starts on
  // the type step, where there is no form yet, so the measure waits for it.
  const panelRef = useRef<HTMLDivElement>(null);
  const [panelHeight, setPanelHeight] = useState<number>();
  useLayoutEffect(() => {
    // +50: the General tab alone is a little cramped for the email list.
    if (panelHeight === undefined && panelRef.current) setPanelHeight(panelRef.current.offsetHeight + 50);
  }, [step, panelHeight]);

  function chooseType(intake: boolean) {
    // Not final — the Intake Haulier checkbox on the form still shows and edits it.
    setForm((f) => ({ ...f, intakeHaulier: intake }));
    setStep("form");
  }

  function set<K extends keyof HaulierInput>(key: K, value: HaulierInput[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function onSave() {
    if (!form.name?.trim()) {
      void messageDialog("Name is required.");
      return;
    }
    // An address typed on the Emails tab but not yet added still counts. If it isn't an
    // address, stay on that tab — withPendingEmail has said so.
    const emails = withPendingEmail(form.emails, newEmail);
    if (emails === null) {
      setTab("emails");
      return;
    }
    setBusy(true);
    try {
      const input = { ...form, emails };
      if (isNew) await createHaulier(input);
      else await updateHaulier(existing!.id, input);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  if (step === "type") {
    return (
      <Modal
        title="Add New Haulier"
        width={620}
        onClose={() => onClose(false)}
        footer={
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
        }
      >
        <div className="wizard-step">
          <h3 className="wizard-question">What type is this Haulier?</h3>
          <div className="wizard-options">
            <button className="wizard-option" onClick={() => chooseType(true)}>
              <span className="wizard-option-title">Intake (Goods in)</span>
              <span className="wizard-option-sub">
                Brings raw materials in. Appears on the Touch Weigh In screen for deliveries.
              </span>
            </button>
            <button className="wizard-option" onClick={() => chooseType(false)}>
              <span className="wizard-option-title">Customer Orders</span>
              <span className="wizard-option-sub">
                Takes finished orders out. Used on dispatch orders and loads, not on Weigh In.
              </span>
            </button>
          </div>
        </div>
      </Modal>
    );
  }

  return (
    <Modal
      title={`${isNew ? "Add New" : "Edit"} Haulier`}
      width={620}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || !form.name?.trim()} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div className="page-tabs">
        <button className={"page-tab" + (tab === "general" ? " active" : "")} onClick={() => setTab("general")}>
          General
        </button>
        <button className={"page-tab" + (tab === "emails" ? " active" : "")} onClick={() => setTab("emails")}>
          Emails ({form.emails.length})
        </button>
      </div>

      <div ref={panelRef} className="hl-tab-panel" style={{ height: panelHeight }}>
        {tab === "general" && (
          <div className="form-inline">
            <div className="form-cols">
              <div className="form-col">
                <div className="form-field form-field-inline">
                  <label className="form-label">Code</label>
                  <input className="form-input" value={form.code ?? ""} onChange={(e) => set("code", e.target.value)} />
                </div>
                <div className="form-field form-field-inline">
                  <label className="form-label">Name *</label>
                  <input
                    className="form-input"
                    value={form.name ?? ""}
                    autoFocus
                    onChange={(e) => set("name", e.target.value)}
                  />
                </div>
                <div className="form-field form-field-inline">
                  <label className="form-label">Contact</label>
                  <input
                    className="form-input"
                    value={form.contactName ?? ""}
                    onChange={(e) => set("contactName", e.target.value)}
                  />
                </div>
                <div className="form-field form-field-inline">
                  <label className="form-label">Telephone</label>
                  <input className="form-input" value={form.tel ?? ""} onChange={(e) => set("tel", e.target.value)} />
                </div>
                <div className="form-field form-field-inline form-field-check">
                  <label className="form-label">Intake Haulier</label>
                  <div className="form-check">
                    <input
                      type="checkbox"
                      checked={form.intakeHaulier}
                      onChange={(e) => set("intakeHaulier", e.target.checked)}
                    />
                    <span className="form-hint">Required to appear on Touch Weigh In.</span>
                  </div>
                </div>
              </div>

              <div className="form-col">
                <div className="form-field form-field-inline">
                  <label className="form-label">Address 1</label>
                  <input
                    className="form-input"
                    value={form.addressLine1 ?? ""}
                    onChange={(e) => set("addressLine1", e.target.value)}
                  />
                </div>
                <div className="form-field form-field-inline">
                  <label className="form-label">Address 2</label>
                  <input
                    className="form-input"
                    value={form.addressLine2 ?? ""}
                    onChange={(e) => set("addressLine2", e.target.value)}
                  />
                </div>
                <div className="form-field form-field-inline">
                  <label className="form-label">Address 3</label>
                  <input
                    className="form-input"
                    value={form.addressLine3 ?? ""}
                    onChange={(e) => set("addressLine3", e.target.value)}
                  />
                </div>
                <div className="form-field form-field-inline">
                  <label className="form-label">VAT Number</label>
                  <input
                    className="form-input"
                    value={form.vatNumber ?? ""}
                    onChange={(e) => set("vatNumber", e.target.value)}
                  />
                </div>
                <div className="form-field form-field-inline">
                  <label className="form-label">Nominal Code</label>
                  <input
                    className="form-input"
                    value={form.nominalCode ?? ""}
                    onChange={(e) => set("nominalCode", e.target.value)}
                  />
                </div>
                <div className="form-field form-field-inline">
                  <label className="form-label">VAT Code</label>
                  <input
                    className="form-input"
                    value={form.vatCode ?? ""}
                    onChange={(e) => set("vatCode", e.target.value)}
                  />
                </div>
              </div>
            </div>
          </div>
        )}

        {tab === "emails" && (
          <div className="hl-tab-fill">
            <EmailListEditor
              emails={form.emails}
              onChange={(emails) => set("emails", emails)}
              typed={newEmail}
              onTypedChange={setNewEmail}
              autoFocus
            />
          </div>
        )}
      </div>
    </Modal>
  );
}
