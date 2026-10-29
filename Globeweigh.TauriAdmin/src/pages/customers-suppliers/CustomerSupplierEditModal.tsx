import { useEffect, useLayoutEffect, useRef, useState } from "react";
import Modal from "../../components/Modal";
import DataTable, { Column } from "../../components/DataTable";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  createCustomerSupplier,
  updateCustomerSupplier,
  getCostingZones,
  getAddresses,
  deleteAddress,
  setAddressDeliveryNote,
  CustomerSupplier,
  CustomerSupplierInput,
  CustomerAddress,
  CostingZoneRef,
} from "../../api/customersSuppliers";
import AddressEditModal from "./AddressEditModal";
import EmailListEditor, { withPendingEmail } from "../../components/EmailList";
import "./CustomersSuppliers.css";

interface Props {
  existing: CustomerSupplier | null; // null = new
  typeId: number;
  onClose: (saved: boolean) => void;
}

// Add/edit a customer/supplier (General tab), its email addresses (Emails tab), and its
// delivery addresses (Addresses / Delivery Note Info tabs — customers only).
export default function CustomerSupplierEditModal({ existing, typeId, onClose }: Props) {
  const isNew = existing === null;
  const [tab, setTab] = useState<"general" | "emails" | "addresses" | "deliveryNotes">("general");
  const [zones, setZones] = useState<CostingZoneRef[]>([]);
  const [form, setForm] = useState<CustomerSupplierInput>({
    typeId: existing?.typeId ?? typeId,
    code: existing?.code ?? "",
    name: existing?.name ?? "",
    costingZoneId: existing?.costingZoneId ?? 0,
    isBoth: false,
    inactive: false,
    emails: existing?.emails ?? [],
  });
  const [busy, setBusy] = useState(false);
  // Emails tab: the address being typed (held here so Save picks it up). The list itself
  // lives on the form and is saved with the record, so a new record can have addresses
  // before it exists and Cancel drops every change.
  const [newEmail, setNewEmail] = useState("");

  // Addresses tab state.
  const [addresses, setAddresses] = useState<CustomerAddress[]>([]);
  const [selectedAddr, setSelectedAddr] = useState<CustomerAddress | null>(null);
  const [editingAddr, setEditingAddr] = useState<CustomerAddress | null | "new">(null);
  // Set once an address is added, edited or deleted, so even Cancel makes the list reload
  // (its "Address Count" column).
  const [addrTouched, setAddrTouched] = useState(false);
  // Delivery Note Info tab: one reusable block of text per address. Edits are held
  // here keyed by address id until Save, so switching address or tab loses nothing.
  const [noteAddr, setNoteAddr] = useState<CustomerAddress | null>(null);
  const [noteEdits, setNoteEdits] = useState<Record<number, string>>({});

  // The dialog keeps one height whatever tab is showing: the tab area is measured once
  // while the General tab is up (the initial tab) and locked to that, so the Addresses
  // and Delivery Note Info tabs fill and scroll inside it instead of resizing the dialog.
  const panelRef = useRef<HTMLDivElement>(null);
  const [panelHeight, setPanelHeight] = useState<number>();
  useLayoutEffect(() => {
    // +50: the General tab alone is a little cramped for the address, email and note
    // lists. A supplier's General tab is shorter still (name only), so there's a floor.
    if (panelRef.current) setPanelHeight(Math.max(panelRef.current.offsetHeight + 50, 300));
  }, []);

  // Costing zones only apply to customers — suppliers have no link to costing.
  const isSupplier = typeId === 2;

  useEffect(() => {
    if (isSupplier) return;
    void (async () => {
      try {
        const z = await getCostingZones();
        setZones(z);
        // Default to existing zone, or the first zone for a new record.
        setForm((f) => ({
          ...f,
          costingZoneId: f.costingZoneId || (z.length > 0 ? z[0].id : 0),
        }));
      } catch (err) {
        void messageDialog("Failed to load costing zones.\n" + (err as Error).message);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function reloadAddresses() {
    if (isNew) return;
    try {
      const data = await getAddresses(existing!.id);
      setAddresses(data);
      setSelectedAddr((cur) => (cur ? data.find((a) => a.id === cur.id) ?? null : null));
      // Keep the note editor on the same address, else start on the first one.
      setNoteAddr((cur) => (cur ? data.find((a) => a.id === cur.id) ?? null : null) ?? data[0] ?? null);
    } catch (err) {
      void messageDialog("Failed to load addresses.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    if (!isNew && !isSupplier) void reloadAddresses();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function set<K extends keyof CustomerSupplierInput>(key: K, value: CustomerSupplierInput[K]) {
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
      if (isNew) await createCustomerSupplier(input);
      else {
        await updateCustomerSupplier(existing!.id, input);
        await saveDeliveryNotes();
      }
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  // Only the notes actually changed are written, each to its own address.
  async function saveDeliveryNotes() {
    for (const addr of addresses) {
      const edited = noteEdits[addr.id];
      if (edited === undefined || edited.trim() === (addr.deliveryNote ?? "").trim()) continue;
      await setAddressDeliveryNote(addr.id, edited);
    }
  }

  async function onDeleteAddress() {
    if (!selectedAddr) return;
    if (!(await confirmDialog(`Delete address "${selectedAddr.addressName}"?`))) return;
    try {
      await deleteAddress(selectedAddr.id);
      setAddrTouched(true);
      await reloadAddresses();
    } catch (err) {
      void messageDialog("Failed to delete.\n" + (err as Error).message);
    }
  }

  // The whole address on one line, as the Customers page shows it.
  const addressParts = (r: CustomerAddress) =>
    [r.address1, r.address2, r.address3, r.address4, r.address5]
      .map((v) => v?.trim())
      .filter((v): v is string => !!v);
  const fullAddress = (r: CustomerAddress) => addressParts(r).join(", ");
  const addressColumn: Column<CustomerAddress> = { key: "address", header: "Address", render: (r) => fullAddress(r) };

  // Addresses tab: the Name column only earns its space when at least one address is
  // actually named; the address column takes the rest and truncates with an ellipsis.
  const anyNamed = addresses.some((a) => a.addressName?.trim());
  const addressColumns: Column<CustomerAddress>[] = [
    ...(anyNamed
      ? [{ key: "addressName", header: "Name", fit: true, render: (r) => r.addressName } as Column<CustomerAddress>]
      : []),
    addressColumn,
  ];

  // How many addresses have a note (saved, or edited and not yet saved) — shown on the tab.
  const hasNote = (a: CustomerAddress) => !!(noteEdits[a.id] ?? a.deliveryNote ?? "").trim();
  const noteCount = addresses.filter(hasNote).length;

  // Delivery Note Info tab list: address only, plus ✎ on an address whose note is filled in.
  const noteAddressColumns: Column<CustomerAddress>[] = [
    addressColumn,
    {
      key: "hasNote",
      header: "",
      fit: true,
      align: "center",
      render: (r) => (hasNote(r) ? "✎" : ""),
    },
  ];

  return (
    <Modal
      title={`${isNew ? "Add New" : "Edit"} ${typeId === 2 ? "Supplier" : "Customer"}`}
      width={750}
      onClose={() => onClose(addrTouched)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(addrTouched)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || !form.name?.trim()} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      {/* Suppliers get General and Emails — delivery addresses (and their notes) are a
          customer-only thing. */}
      <div className="page-tabs">
        <button
          className={"page-tab" + (tab === "general" ? " active" : "")}
          onClick={() => setTab("general")}
        >
          General
        </button>
        <button
          className={"page-tab" + (tab === "emails" ? " active" : "")}
          onClick={() => setTab("emails")}
        >
          Emails ({form.emails.length})
        </button>
        {!isSupplier && (
          <>
            <button
              className={"page-tab" + (tab === "addresses" ? " active" : "")}
              onClick={() => setTab("addresses")}
            >
              Addresses
            </button>
            <button
              className={"page-tab" + (tab === "deliveryNotes" ? " active" : "")}
              onClick={() => setTab("deliveryNotes")}
            >
              Delivery Note Info ({noteCount})
            </button>
          </>
        )}
      </div>

      <div ref={panelRef} className="cs-tab-panel" style={{ height: panelHeight }}>
        {tab === "general" && (
          <div className="form-cols">
            <div className="form-col">
              {!isSupplier && (
                <div className="form-field">
                  <label className="form-label">Code</label>
                  <input
                    className="form-input"
                    value={form.code ?? ""}
                    onChange={(e) => set("code", e.target.value)}
                  />
                </div>
              )}
              <div className="form-field">
                <label className="form-label">Name *</label>
                <input
                  className="form-input"
                  value={form.name ?? ""}
                  autoFocus
                  onChange={(e) => set("name", e.target.value)}
                />
              </div>
              {!isSupplier && (
                <div className="form-field">
                  <label className="form-label">Costing Zone</label>
                  <select
                    className="form-input"
                    value={form.costingZoneId}
                    onChange={(e) => set("costingZoneId", Number(e.target.value))}
                  >
                    {zones.map((z) => (
                      <option key={z.id} value={z.id}>
                        {z.description}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              <label className="stock-radio" style={{ marginTop: 6 }}>
                <input
                  type="checkbox"
                  checked={form.isBoth}
                  onChange={(e) => set("isBoth", e.target.checked)}
                />
                Is Both
              </label>
              <label className="stock-radio" style={{ marginTop: 6 }}>
                <input
                  type="checkbox"
                  checked={form.inactive}
                  onChange={(e) => set("inactive", e.target.checked)}
                />
                Inactive
              </label>
            </div>
          </div>
        )}

        {tab === "emails" && (
          <div className="cs-tab-fill">
            <EmailListEditor
              emails={form.emails}
              onChange={(emails) => set("emails", emails)}
              typed={newEmail}
              onTypedChange={setNewEmail}
              autoFocus
            />
          </div>
        )}

        {tab === "addresses" && (
          <div className="cs-tab-fill">
            {isNew ? (
              <p className="toolbar-note">Save the record first to add addresses.</p>
            ) : (
              <>
                <div className="toolbar">
                  <div className="toolbar-right">
                    <button className="btn-primary" onClick={() => setEditingAddr("new")}>
                      Add
                    </button>
                    <button
                      className="toolbar-btn"
                      disabled={!selectedAddr}
                      onClick={() => setEditingAddr(selectedAddr)}
                    >
                      Edit
                    </button>
                    <button
                      className="toolbar-btn"
                      disabled={!selectedAddr}
                      onClick={onDeleteAddress}
                    >
                      Delete
                    </button>
                  </div>
                </div>
                <DataTable
                  columns={addressColumns}
                  rows={addresses}
                  rowKey={(r) => r.id}
                  selectedKey={selectedAddr?.id ?? null}
                  onSelect={setSelectedAddr}
                  onDoubleClick={(r) => setEditingAddr(r)}
                  emptyText="No addresses"
                />
              </>
            )}
          </div>
        )}

        {tab === "deliveryNotes" && (
          <div className="cs-tab-fill">
            {isNew ? (
              <p className="toolbar-note">Save the record first to add delivery note info.</p>
            ) : (
              <div className="cs-note-tab">
                <div className="cs-note-tab-list">
                  <DataTable
                    columns={noteAddressColumns}
                    rows={addresses}
                    rowKey={(r) => r.id}
                    selectedKey={noteAddr?.id ?? null}
                    onSelect={setNoteAddr}
                    emptyText="No addresses"
                  />
                </div>
                <div className="cs-note-tab-editor">
                  {noteAddr ? (
                    <>
                      <label className="form-label">Address</label>
                      <div className="cs-note-addr">
                        {addressParts(noteAddr).join("\n") || noteAddr.addressName || "Selected address"}
                      </div>
                      <label className="form-label">Delivery note Info</label>
                      <textarea
                        className="form-input form-textarea form-textarea-fill"
                        value={noteEdits[noteAddr.id] ?? noteAddr.deliveryNote ?? ""}
                        spellCheck={false}
                        onChange={(e) => {
                          const id = noteAddr.id;
                          const text = e.target.value;
                          setNoteEdits((m) => ({ ...m, [id]: text }));
                        }}
                      />
                    </>
                  ) : (
                    <p className="toolbar-note">Select an address to enter its delivery note info.</p>
                  )}
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      {editingAddr !== null && !isNew && (
        <AddressEditModal
          csId={existing!.id}
          existing={editingAddr === "new" ? null : editingAddr}
          onClose={(saved) => {
            setEditingAddr(null);
            if (saved) {
              setAddrTouched(true);
              void reloadAddresses();
            }
          }}
        />
      )}
    </Modal>
  );
}
