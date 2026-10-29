import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  deleteReferenceData,
  EntityType,
  getRefDataTypes,
  getReferenceData,
  ReferenceDataItem,
} from "../../api/referenceData";
import RefDataEditModal from "./RefDataEditModal";
import CostingZonesPanel from "./CostingZonesPanel";
import StaffPanel from "./StaffPanel";
import BulkDensityPanel from "./BulkDensityPanel";
import CustomerLabelPanel from "./CustomerLabelPanel";
import SettingsPanel from "./SettingsPanel";
import AutomaticEmailsPanel from "./AutomaticEmailsPanel";
import DeliveryNotePanel from "./DeliveryNotePanel";
import { AdminUserDto } from "../../types";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";

// Extra "Data Setup" items that aren't generic reference-data types — each has its
// own dedicated editor (mirrors the WPF ReferenceDataMasterView special cases).
// They sit in the SAME list as the reference-data types (e.g. Finished Product Category).
type SpecialView =
  | "costing-zones"
  | "staff"
  | "bulk-density"
  | "customer-label"
  | "settings"
  | "automatic-emails"
  | "delivery-note";
const SPECIAL_ITEMS: { key: SpecialView; label: string }[] = [
  { key: "costing-zones", label: "Costing Zones" },
  { key: "staff", label: "Staff (Login)" },
  { key: "bulk-density", label: "Bulk Density" },
  { key: "customer-label", label: "Customer Label" },
  { key: "settings", label: "Settings" },
  { key: "automatic-emails", label: "Automatic Emails" },
  { key: "delivery-note", label: "Delivery Note" },
];

// A single nav row in the "Data Setup" list — either a reference-data type or one
// of the special editors above.
type NavRow =
  | { key: string; label: string; kind: "refdata"; type: EntityType }
  | { key: string; label: string; kind: "special"; special: SpecialView };

// Reference Data master/detail — mirrors the WPF ReferenceDataMasterView ("Admin" menu).
export default function ReferenceDataPage(_props: { user: AdminUserDto }) {
  const [types, setTypes] = useState<EntityType[]>([]);
  const [selectedType, setSelectedType] = useState<EntityType | null>(null);
  const [special, setSpecial] = useState<SpecialView | null>(null);
  const [items, setItems] = useState<ReferenceDataItem[]>([]);
  const [selectedItem, setSelectedItem] = useState<ReferenceDataItem | null>(null);
  const [search, setSearch] = useState("");
  const [loadingTypes, setLoadingTypes] = useState(false);
  const [loadingItems, setLoadingItems] = useState(false);
  const [editing, setEditing] = useState<ReferenceDataItem | null | "new">(null);

  async function refreshTypes() {
    setLoadingTypes(true);
    try {
      setTypes(await getRefDataTypes());
    } catch (err) {
      void messageDialog("Failed to load reference data types.\n" + (err as Error).message);
    } finally {
      setLoadingTypes(false);
    }
  }

  async function loadItems(type: EntityType) {
    setSpecial(null);
    setSelectedType(type);
    setLoadingItems(true);
    try {
      const data = await getReferenceData(type.entityTypeId);
      setItems(data);
      setSelectedItem((cur) => (cur ? data.find((r) => r.id === cur.id) ?? null : null));
    } catch (err) {
      setItems([]);
      void messageDialog("Failed to load reference data.\n" + (err as Error).message);
    } finally {
      setLoadingItems(false);
    }
  }

  function selectSpecial(view: SpecialView) {
    setSpecial(view);
    setSelectedType(null);
    setSelectedItem(null);
  }

  useEffect(() => {
    void refreshTypes();
  }, []);

  async function onDelete() {
    if (!selectedItem) return;
    if (!(await confirmDialog(`Delete "${selectedItem.displayName}"?`))) return;
    try {
      await deleteReferenceData(selectedItem.id);
      if (selectedType) await loadItems(selectedType);
    } catch (err) {
      void messageDialog("Failed to delete.\n" + (err as Error).message);
    }
  }

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return items;
    return items.filter((r) =>
      [r.code, r.displayName].some((v) => v?.toLowerCase().includes(term)),
    );
  }, [items, term]);

  // Reference-data types followed by the special editors — one combined list.
  const navRows: NavRow[] = [
    ...types.map<NavRow>((t) => ({
      key: `t${t.id}`,
      label: t.displayName ?? "",
      kind: "refdata",
      type: t,
    })),
    ...SPECIAL_ITEMS.map<NavRow>((s) => ({
      key: s.key,
      label: s.label,
      kind: "special",
      special: s.key,
    })),
  ];

  const selectedNavKey = special ?? (selectedType ? `t${selectedType.id}` : null);

  function onSelectNav(row: NavRow) {
    if (row.kind === "special") selectSpecial(row.special);
    else void loadItems(row.type);
  }

  const itemColumns: Column<ReferenceDataItem>[] = [
    { key: "displayName", header: "Name", render: (r) => <Highlight text={r.displayName} term={term} /> },
    { key: "alternativeDisplayName", header: "Alt Name", render: (r) => r.alternativeDisplayName },
  ];

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader
          title="Admin"
          meta={
            special
              ? SPECIAL_ITEMS.find((s) => s.key === special)?.label
              : (selectedType?.displayName ?? undefined)
          }
        />
        <div className={cc.body}>
        <aside className={`${cc.listCol} ${cc.listColTinted}`} style={{ flexBasis: 200 }}>
          <div className="side-list-title rail">Data Setup</div>
          <ul className="side-list">
            {loadingTypes && navRows.length === 0 && <li>Loading…</li>}
            {navRows.map((r) => (
              <li
                key={r.key}
                className={r.key === selectedNavKey ? "active" : undefined}
                onClick={() => onSelectNav(r)}
                tabIndex={0}
                onKeyDown={(e) => {
                  if (e.key === "Enter" || e.key === " ") onSelectNav(r);
                }}
              >
                {r.label}
              </li>
            ))}
          </ul>
        </aside>

        {special === "costing-zones" ? (
          <CostingZonesPanel />
        ) : special === "staff" ? (
          <StaffPanel />
        ) : special === "bulk-density" ? (
          <BulkDensityPanel />
        ) : special === "customer-label" ? (
          <CustomerLabelPanel />
        ) : special === "settings" ? (
          <SettingsPanel />
        ) : special === "automatic-emails" ? (
          <AutomaticEmailsPanel />
        ) : special === "delivery-note" ? (
          <DeliveryNotePanel />
        ) : !selectedType ? (
          <div className="po-main" />
        ) : (
          <div className="po-main">
            <div className="toolbar">
              <input
                className="form-input toolbar-search"
                placeholder="Search reference data…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
              {loadingItems && <span className="toolbar-note">Loading…</span>}
              <div className="toolbar-right">
                <button className="btn-primary" disabled={!selectedType} onClick={() => setEditing("new")}>
                  Add
                </button>
                <button className="toolbar-btn" disabled={!selectedItem} onClick={() => setEditing(selectedItem)}>
                  Edit
                </button>
                <button className="toolbar-btn" disabled={!selectedItem} onClick={onDelete}>
                  Delete
                </button>
                <button
                  className="toolbar-btn"
                  onClick={() => selectedType && void loadItems(selectedType)}
                  disabled={loadingItems || !selectedType}
                >
                  Refresh
                </button>
              </div>
            </div>

            <DataTable
              columns={itemColumns}
              rows={filtered}
              rowKey={(r) => r.id}
              selectedKey={selectedItem?.id ?? null}
              onSelect={setSelectedItem}
              onDoubleClick={(r) => setEditing(r)}
              emptyText={
                !selectedType ? "Select a type" : loadingItems ? "Loading…" : "No reference data"
              }
            />
          </div>
        )}
        </div>
      </div>

      {editing !== null && selectedType && (
        <RefDataEditModal
          existing={editing === "new" ? null : editing}
          refTypeId={selectedType.entityTypeId}
          onClose={(saved) => {
            setEditing(null);
            if (saved && selectedType) void loadItems(selectedType);
          }}
        />
      )}
    </div>
  );
}
