import { useEffect, useMemo, useState } from "react";
import Modal from "../../components/Modal";
import { ORDER_DIALOG_WIDTH, ORDER_DIALOG_HEIGHT } from "./orderDialogSize";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import {
  CustomerAddress,
  CustomerSupplier,
  getAddresses,
  getCustomersSuppliers,
  setAddressDeliveryNote,
} from "../../api/customersSuppliers";
// cs-note-view / cs-note-empty: the same read-only note block the Customers page uses.
import "../customers-suppliers/CustomersSuppliers.css";
import "./Dispatch.css";

interface Props {
  initialCustomerId: number;
  initialAddressId: number;
  // deliveryNote: the address's Delivery Note Info (saved, or typed in the dialog), for the order.
  onSelect: (customer: CustomerSupplier, address: CustomerAddress | null, deliveryNote: string | null) => void;
  onClose: () => void;
  // My Orders, a Sales user: only the customers assigned to this rep (Customers grid, Sales Rep).
  salesRepId?: number;
}

// Customer picker for dispatch orders: searchable customer list (left) with the
// selected customer's delivery addresses (right). An address must be chosen
// unless the customer has none.
export default function SelectCustomerAddressModal({
  initialCustomerId,
  initialAddressId,
  onSelect,
  onClose,
  salesRepId,
}: Props) {
  const [customers, setCustomers] = useState<CustomerSupplier[]>([]);
  const [search, setSearch] = useState("");
  const [customer, setCustomer] = useState<CustomerSupplier | null>(null);
  const [addresses, setAddresses] = useState<CustomerAddress[]>([]);
  const [addressId, setAddressId] = useState(0);
  const [loading, setLoading] = useState(true);
  const [addrLoading, setAddrLoading] = useState(false);
  // Delivery Note Info for the highlighted address. When the address has none saved it
  // can be typed here for this order; saveNote = also keep it on the address for next time.
  const [noteText, setNoteText] = useState("");
  const [saveNote, setSaveNote] = useState(false);
  const [saving, setSaving] = useState(false);
  useEffect(() => {
    setNoteText("");
    setSaveNote(false);
  }, [addressId]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const all = await getCustomersSuppliers(1);
        if (cancelled) return;
        const cs = salesRepId != null ? all.filter((c) => c.salesRepId === salesRepId) : all;
        setCustomers(cs);
        const initial = cs.find((c) => c.id === initialCustomerId) ?? null;
        if (initial) setCustomer(initial);
      } catch (err) {
        if (!cancelled) void messageDialog("Failed to load customers.\n" + (err as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [initialCustomerId, salesRepId]);

  // Load the highlighted customer's addresses; preselect the order's current
  // address (first open) or the only address when there is exactly one.
  useEffect(() => {
    if (!customer) {
      setAddresses([]);
      setAddressId(0);
      return;
    }
    let cancelled = false;
    setAddrLoading(true);
    (async () => {
      try {
        const rows = await getAddresses(customer.id);
        if (cancelled) return;
        setAddresses(rows);
        if (customer.id === initialCustomerId && rows.some((a) => a.id === initialAddressId)) {
          setAddressId(initialAddressId);
        } else if (rows.length === 1) {
          setAddressId(rows[0].id);
        } else {
          setAddressId(0);
        }
      } catch (err) {
        if (!cancelled) void messageDialog("Failed to load addresses.\n" + (err as Error).message);
      } finally {
        if (!cancelled) setAddrLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [customer, initialCustomerId, initialAddressId]);

  const term = search.trim().toLowerCase();
  const filtered = useMemo(
    () =>
      customers.filter(
        (c) => !term || [c.name, c.code].some((v) => v?.toLowerCase().includes(term)),
      ),
    [customers, term],
  );

  const selectedAddress = addresses.find((a) => a.id === addressId) ?? null;
  const canOk = customer != null && !addrLoading && (addresses.length === 0 || selectedAddress != null);

  const savedNote = selectedAddress?.deliveryNote?.trim() ?? "";
  const effectiveNote = savedNote || noteText.trim();

  async function confirm(address: CustomerAddress | null) {
    if (!customer) return;
    // The typed note belongs to the highlighted address only — a double-click on another
    // row takes that row's saved note instead and never stores the typed text.
    const isHighlighted = address != null && address.id === selectedAddress?.id;
    const note = isHighlighted ? effectiveNote : (address?.deliveryNote?.trim() ?? "");
    if (address && isHighlighted && !savedNote && saveNote && noteText.trim()) {
      setSaving(true);
      try {
        await setAddressDeliveryNote(address.id, noteText);
      } catch (err) {
        setSaving(false);
        void messageDialog("Failed to save the delivery note info with the address.\n" + (err as Error).message);
        return;
      }
    }
    onSelect(customer, address, note || null);
    onClose();
  }

  const customerColumns: Column<CustomerSupplier>[] = [
    { key: "code", header: "Code", width: 80, render: (c) => <Highlight text={c.code} term={term} /> },
    { key: "name", header: "Customer", render: (c) => <Highlight text={c.name} term={term} /> },
  ];

  function addressLines(a: CustomerAddress) {
    return [a.address1, a.address2, a.address3, a.address4, a.address5].filter(Boolean).join(", ");
  }

  return (
    <Modal
      title="Select Customer & Address"
      // Same size as the Add/Edit Order dialog that opens it.
      width={ORDER_DIALOG_WIDTH}
      height={ORDER_DIALOG_HEIGHT}
      onClose={onClose}
      footer={
        <>
          <button className="toolbar-btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-blue" disabled={!canOk || saving} onClick={() => void confirm(selectedAddress)}>
            {saving ? "Saving…" : "OK"}
          </button>
        </>
      }
    >
      {loading ? (
        <div className="data-table-empty">Loading…</div>
      ) : (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "minmax(0, 1fr) minmax(0, 1fr)",
            gap: 12,
            height: "100%",
            minHeight: 0,
          }}
        >
          {/* Left: searchable customers */}
          <div style={{ display: "flex", flexDirection: "column", minWidth: 0, minHeight: 0 }}>
            <div className="toolbar">
              <input
                className="form-input toolbar-search"
                placeholder="Search customers…"
                value={search}
                autoFocus
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
            <DataTable
              columns={customerColumns}
              rows={filtered}
              rowKey={(c) => c.id}
              selectedKey={customer?.id ?? null}
              onSelect={(c) => setCustomer(c)}
              emptyText={loading ? "Loading…" : salesRepId != null ? "No customers are assigned to you" : "No customers"}
            />
          </div>

          {/* Right: the selected customer's addresses */}
          <div style={{ display: "flex", flexDirection: "column", minWidth: 0, minHeight: 0, gap: 6 }}>
            <h3 className="dsp-panel-title" style={{ margin: 0 }}>
              Addresses
            </h3>
            {!customer ? (
              <div className="data-table-empty">Select a customer.</div>
            ) : addrLoading ? (
              <div className="data-table-empty">Loading…</div>
            ) : addresses.length === 0 ? (
              <div className="data-table-empty">
                No addresses for this customer — none required.
              </div>
            ) : (
              <div style={{ overflowY: "auto", minHeight: 0, display: "flex", flexDirection: "column", gap: 6 }}>
                {addresses.map((a) => (
                  <div
                    key={a.id}
                    className={`stock-type-item${addressId === a.id ? " active" : ""}`}
                    style={{ flex: "none" }}
                    onClick={() => setAddressId(a.id)}
                    onDoubleClick={() => void confirm(a)}
                  >
                    {a.addressName && <div style={{ fontWeight: 600 }}>{a.addressName}</div>}
                    {addressLines(a) && (
                      <div style={{ fontSize: 11, opacity: 0.85 }}>{addressLines(a)}</div>
                    )}
                  </div>
                ))}
              </div>
            )}
            {/* The highlighted address's reusable note (Customers > Edit Customer > Delivery Note
                Info). With none saved it can be typed here for this order — and kept on the
                address for next time when the box is ticked. */}
            {customer && !addrLoading && selectedAddress && (
              <div className="dsp-note-box">
                <h3 className="dsp-panel-title" style={{ margin: 0 }}>
                  Delivery Note Info
                </h3>
                {savedNote ? (
                  <div className="cs-note-view">{selectedAddress.deliveryNote}</div>
                ) : (
                  <>
                    <textarea
                      className="form-input form-textarea form-textarea-fill"
                      value={noteText}
                      spellCheck={false}
                      onChange={(e) => setNoteText(e.target.value)}
                    />
                    <label className="stock-radio">
                      <input type="checkbox" checked={saveNote} onChange={(e) => setSaveNote(e.target.checked)} />
                      Save this delivery note info with the address
                    </label>
                  </>
                )}
              </div>
            )}
          </div>
        </div>
      )}
    </Modal>
  );
}
