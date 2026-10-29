import { useEffect, useMemo, useRef, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  getCustomersSuppliers,
  getAddresses,
  getLabelConfig,
  deleteCustomerSupplier,
  CustomerSupplier,
  CustomerAddress,
  CustomerLabelConfig,
  SalesRepRef,
  getSalesReps,
  setCustomerSalesRep,
} from "../../api/customersSuppliers";
import CustomerSupplierEditModal from "./CustomerSupplierEditModal";
import LabelConfigModal from "./LabelConfigModal";
import { EMAIL_COLUMNS, toEmailRows } from "../../components/EmailList";
import { AdminUserDto } from "../../types";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import "./CustomersSuppliers.css";

// The Customers / Suppliers toggle — the left-hand "Type" list, same shape as the
// Admin and Super Admin pages (keys are the typeId).
const TYPE_ITEMS: { key: number; label: string }[] = [
  { key: 1, label: "Customers" },
  { key: 2, label: "Suppliers" },
];

// Customers/Suppliers master list — mirrors the WPF CustomerSupplierListView.
// The list on the left switches between Customers (typeId 1) and Suppliers (typeId 2);
// selecting a row shows its addresses, their delivery note info and its email addresses
// in the side panel.
export default function CustomersSuppliersPage(_props: { user: AdminUserDto }) {
  const [typeId, setTypeId] = useState(1);
  const [rows, setRows] = useState<CustomerSupplier[]>([]);
  const [selected, setSelected] = useState<CustomerSupplier | null>(null);
  const [addresses, setAddresses] = useState<CustomerAddress[]>([]);
  // The address highlighted in the side panel — its Delivery Note Info shows underneath.
  const [selectedAddr, setSelectedAddr] = useState<CustomerAddress | null>(null);
  // Swap in a customer's addresses, keeping the highlighted one if it is still there,
  // else starting on the first so a single-address customer's note shows at once.
  function showAddresses(list: CustomerAddress[]) {
    setAddresses(list);
    setSelectedAddr((cur) => (cur && list.find((a) => a.id === cur.id)) || list[0] || null);
  }
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(false);
  const [editing, setEditing] = useState<CustomerSupplier | null | "new">(null);
  const [labelCfg, setLabelCfg] = useState<CustomerLabelConfig | null>(null);
  const [configuringLabel, setConfiguringLabel] = useState(false);

  // Per-customer label overrides for the preview. Failure (e.g. config table not
  // deployed yet) just means the default logo — don't nag on every row click.
  async function loadLabelCfg(id: number) {
    try {
      setLabelCfg(await getLabelConfig(id));
    } catch {
      setLabelCfg(null);
    }
  }

  async function refresh(id = typeId, keepSelection = false) {
    // Each load is numbered: when Customers and Suppliers are switched quickly, only the latest
    // request's answer is shown, never a slower earlier one landing on top of it.
    const seq = ++loadSeq.current;
    setLoading(true);
    try {
      const data = await getCustomersSuppliers(id);
      if (seq !== loadSeq.current) return;
      setRows(data);
      if (keepSelection && selected) {
        const next = data.find((r) => r.id === selected.id) ?? null;
        setSelected(next);
        if (next && id === 1) {
          showAddresses(await getAddresses(next.id));
          void loadLabelCfg(next.id);
        } else {
          showAddresses([]);
          setLabelCfg(null);
        }
      } else {
        setSelected(null);
        showAddresses([]);
        setLabelCfg(null);
      }
    } catch (err) {
      if (seq !== loadSeq.current) return;
      void messageDialog("Failed to load customers/suppliers.\n" + (err as Error).message);
    } finally {
      if (seq === loadSeq.current) setLoading(false);
    }
  }
  const loadSeq = useRef(0);

  useEffect(() => {
    void refresh(1);
    getSalesReps()
      .then(setSalesReps)
      .catch(() => setSalesReps([]));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Sales Rep column: the users with the Sales role, and saving a pick as soon as it is made.
  const [salesReps, setSalesReps] = useState<SalesRepRef[]>([]);
  async function changeSalesRep(row: CustomerSupplier, salesRepId: number | null) {
    const was = row.salesRepId;
    const put = (v: number | null) =>
      setRows((cur) => cur.map((r) => (r.id === row.id ? { ...r, salesRepId: v } : r)));
    put(salesRepId); // shown at once; put back if the save fails
    try {
      await setCustomerSalesRep(row.id, salesRepId);
    } catch (err) {
      put(was);
      void messageDialog("Failed to save the sales rep.\n" + (err as Error).message);
    }
  }

  function selectType(id: number) {
    if (id === typeId) return;
    setTypeId(id);
    // Clear the other list at once (rows, selection and the side panel) so it never sits under
    // the new heading while the new list loads; the grid shows the spinner meanwhile.
    setRows([]);
    setSelected(null);
    showAddresses([]);
    setLabelCfg(null);
    void refresh(id);
  }

  async function selectRow(row: CustomerSupplier) {
    setSelected(row);
    // Addresses/label panel is customer-only.
    if (typeId !== 1) return;
    setLabelCfg(null);
    void loadLabelCfg(row.id);
    try {
      showAddresses(await getAddresses(row.id));
    } catch (err) {
      showAddresses([]);
      void messageDialog("Failed to load addresses.\n" + (err as Error).message);
    }
  }

  async function onDelete() {
    if (!selected) return;
    if (!(await confirmDialog(`Delete "${selected.name}"?`))) return;
    try {
      await deleteCustomerSupplier(selected.id);
      await refresh();
    } catch (err) {
      void messageDialog("Failed to delete.\n" + (err as Error).message);
    }
  }

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return rows;
    // Suppliers only show/search by name (code is a customer-only field); both search
    // their email addresses.
    return rows.filter((r) =>
      (typeId === 1 ? [r.code, r.name, ...r.emails] : [r.name, ...r.emails]).some((v) =>
        v?.toLowerCase().includes(term)
      )
    );
  }, [rows, term, typeId]);

  // How many email addresses the record has — the addresses themselves are in the panel
  // beside the grid, so this is for spotting the records that have none.
  const emailCountColumn: Column<CustomerSupplier> = {
    key: "emails",
    header: "Emails",
    fit: true, // best-fit: as wide as its header
    align: "right",
    render: (r) => r.emails.length || "",
    sortValue: (r) => r.emails.length,
  };

  // Code and costing zones only apply to customers — suppliers show name only.
  const columns: Column<CustomerSupplier>[] =
    typeId === 1
      ? [
          { key: "code", header: "Code", width: 100, render: (r) => <Highlight text={r.code} term={term} /> },
          { key: "name", header: "Name", render: (r) => <Highlight text={r.name} term={term} /> },
          { key: "costingZone", header: "Costing Zone", width: 160, render: (r) => r.costingZone },
          {
            key: "salesRepId",
            header: "Sales Rep",
            width: 170,
            // A dropdown in the cell, saved on change. Its clicks stay in the cell, so picking a
            // rep does not also select (or, double-clicked, open) the row.
            render: (r) => (
              <select
                className="cs-rep-select"
                value={r.salesRepId ?? ""}
                onClick={(e) => e.stopPropagation()}
                onDoubleClick={(e) => e.stopPropagation()}
                onChange={(e) => void changeSalesRep(r, e.target.value === "" ? null : Number(e.target.value))}
              >
                <option value="">—</option>
                {salesReps
                  .filter((s) => !s.disabled || s.id === r.salesRepId)
                  .map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                      {s.disabled ? " (disabled)" : ""}
                    </option>
                  ))}
              </select>
            ),
            sortValue: (r) => salesReps.find((s) => s.id === r.salesRepId)?.name ?? "",
          },
          { key: "addressCount", header: "Address Count", fit: true, align: "right", render: (r) => r.addressCount },
          emailCountColumn,
        ]
      : [{ key: "name", header: "Name", render: (r) => <Highlight text={r.name} term={term} /> }, emailCountColumn];

  const addressColumns: Column<CustomerAddress>[] = [
    {
      key: "address1",
      header: "Address",
      render: (r) =>
        [r.address1, r.address2, r.address3, r.address4, r.address5]
          .map((v) => v?.trim())
          .filter(Boolean)
          .join(", "),
    },
  ];

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader
          title="Customers/Suppliers"
          meta={loading && rows.length === 0 ? "Loading…" : `${filtered.length} ${typeId === 1 ? "customers" : "suppliers"}`}
        />
        <div className={cc.body}>
          <aside className={cc.listCol} style={{ flexBasis: 120 }}>
            <div className={cc.colHeader}>
              <h2 className={cc.colTitle}>Type</h2>
            </div>
            <div className={cc.navList}>
              {TYPE_ITEMS.map((item) => (
                <div
                  key={item.key}
                  className={item.key === typeId ? `${cc.navItem} ${cc.navItemActive}` : cc.navItem}
                  onClick={() => selectType(item.key)}
                  role="button"
                  tabIndex={0}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") selectType(item.key);
                  }}
                >
                  {item.label}
                </div>
              ))}
            </div>
          </aside>

      <div className="po-open-layout">
        <div className="po-main">
          <div className="toolbar">
            <input
              className="form-input toolbar-search"
              placeholder="Search…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            {loading && <span className="toolbar-note">Loading…</span>}
            <div className="toolbar-right">
              <button className="btn-primary" onClick={() => setEditing("new")}>
                Add
              </button>
              <button className="toolbar-btn" disabled={!selected} onClick={() => setEditing(selected)}>
                Edit
              </button>
              <button className="toolbar-btn" disabled={!selected} onClick={onDelete}>
                Delete
              </button>
              {/* The customer's label logo override — the only label setting there is. */}
              {typeId === 1 && (
                <button className="toolbar-btn" disabled={!selected} onClick={() => setConfiguringLabel(true)}>
                  Configure Label…
                </button>
              )}
              <button className="toolbar-btn" onClick={() => void refresh()} disabled={loading}>
                Refresh
              </button>
            </div>
          </div>

          {/* Nothing to show yet (first load, or just switched type): the spinner, not an empty
              grid. A Refresh keeps the current list on screen while it reloads. */}
          {loading && rows.length === 0 ? (
            <div className={cc.loadingWrap}>
              <div className={cc.spinner} />
            </div>
          ) : (
          <DataTable
            columns={columns}
            rows={filtered}
            sortable
            rowKey={(r) => r.id}
            selectedKey={selected?.id ?? null}
            onSelect={(r) => void selectRow(r)}
            onDoubleClick={(r) => setEditing(r)}
            emptyText={loading ? "Loading…" : "No records"}
          />
          )}
        </div>

        {/* Read-only detail on the selected record: Edit (or a double-click on the row) is
            where any of it changes. Suppliers have no delivery addresses, so they get the
            Emails section on its own. */}
        <div className="po-side" style={{ width: typeId === 1 ? 540 : 340 }}>
          {selected ? (
            <>
              {typeId === 1 && (
                <>
                  {/* Addresses / Delivery Note Info / Emails stack from the top, each as tall as what it
                      holds (cs-side-sec); they shrink and scroll only if the panel runs out. */}
                  <div className="cs-side-sec">
                    <h3 className="po-side-title">Addresses</h3>
                    <DataTable
                      columns={addressColumns}
                      rows={addresses}
                      rowKey={(r) => r.id}
                      selectedKey={selectedAddr?.id ?? null}
                      onSelect={setSelectedAddr}
                      emptyText="No addresses"
                    />
                  </div>
                  {/* The highlighted address's reusable note (Edit Customer > Delivery Note Info). */}
                  <div className="cs-side-sec">
                    <h3 className="po-side-title">Delivery Note Info</h3>
                    <div className="cs-note-view">
                      {!selectedAddr ? (
                        <span className="cs-note-empty">Select an address to see its delivery note info</span>
                      ) : selectedAddr.deliveryNote?.trim() ? (
                        selectedAddr.deliveryNote
                      ) : (
                        <span className="cs-note-empty">No delivery note info for this address</span>
                      )}
                    </div>
                  </div>
                </>
              )}
              {/* The record's own email addresses (Edit > Emails tab), not the contact
                  address of any one delivery address. */}
              <div className="cs-side-sec">
                <h3 className="po-side-title">Emails</h3>
                <DataTable
                  columns={EMAIL_COLUMNS}
                  rows={toEmailRows(selected.emails ?? [])}
                  rowKey={(r) => r.email}
                  emptyText="No email addresses"
                />
              </div>
            </>
          ) : (
            <div
              style={{
                flex: 1,
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                color: "#64748b",
                fontSize: 13,
              }}
            >
              Select {typeId === 1 ? "customer" : "supplier"} to view data
            </div>
          )}
        </div>
      </div>
        </div>
      </div>

      {configuringLabel && selected && (
        <LabelConfigModal
          customer={selected}
          current={labelCfg}
          onClose={(saved) => {
            setConfiguringLabel(false);
            if (saved && selected) void loadLabelCfg(selected.id);
          }}
        />
      )}

      {editing !== null && (
        <CustomerSupplierEditModal
          existing={editing === "new" ? null : editing}
          typeId={typeId}
          onClose={(saved) => {
            setEditing(null);
            if (saved) void refresh(typeId, true);
          }}
        />
      )}
    </div>
  );
}
