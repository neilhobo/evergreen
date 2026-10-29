import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  CostingZoneRow,
  currencyLabel,
  deleteCostingZone,
  getCostingZones,
} from "../../api/costingZones";
import CostingZoneEditModal from "./CostingZoneEditModal";

// Admin "Costing Zones" — mirrors the WPF CostingZoneListView (add/edit/delete).
export default function CostingZonesPanel() {
  const [zones, setZones] = useState<CostingZoneRow[]>([]);
  const [selected, setSelected] = useState<CostingZoneRow | null>(null);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(false);
  const [editing, setEditing] = useState<CostingZoneRow | null | "new">(null);

  async function load() {
    setLoading(true);
    try {
      const data = await getCostingZones();
      setZones(data);
      setSelected((cur) => (cur ? data.find((r) => r.id === cur.id) ?? null : null));
    } catch (err) {
      setZones([]);
      void messageDialog("Failed to load costing zones.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function onDelete() {
    if (!selected) return;
    if (!(await confirmDialog(`Delete zone "${selected.description}"?`))) return;
    try {
      await deleteCostingZone(selected.id);
      await load();
    } catch (err) {
      void messageDialog("Failed to delete.\n" + (err as Error).message);
    }
  }

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return zones;
    return zones.filter((z) =>
      [z.description, z.secondaryDescription].some((v) => v?.toLowerCase().includes(term)),
    );
  }, [zones, term]);

  const columns: Column<CostingZoneRow>[] = [
    {
      key: "description",
      header: "Description",
      render: (r) => <Highlight text={r.description} term={term} />,
    },
    {
      key: "secondaryDescription",
      header: "Extra Info",
      render: (r) => <Highlight text={r.secondaryDescription} term={term} />,
    },
    { key: "isEuro", header: "Currency", width: 120, render: (r) => currencyLabel(r.isEuro) },
  ];

  return (
    <div className="po-main">
      <div className="toolbar">
        <input
          className="form-input toolbar-search"
          placeholder="Search costing zones…"
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
          <button className="toolbar-btn" onClick={() => void load()} disabled={loading}>
            Refresh
          </button>
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={filtered}
        rowKey={(r) => r.id}
        selectedKey={selected?.id ?? null}
        onSelect={setSelected}
        onDoubleClick={(r) => setEditing(r)}
        emptyText={loading ? "Loading…" : "No costing zones"}
      />

      {editing !== null && (
        <CostingZoneEditModal
          existing={editing === "new" ? null : editing}
          onClose={(saved) => {
            setEditing(null);
            if (saved) void load();
          }}
        />
      )}
    </div>
  );
}
