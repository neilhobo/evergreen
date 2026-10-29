import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  StaffRow,
  deleteStaff,
  getStaff,
  roleLabel,
  userTypeLabel,
} from "../../api/staff";
import StaffEditModal from "./StaffEditModal";

// Admin "Staff (Login)" — mirrors the WPF GlobeweighUserListView (add/edit/delete).
export default function StaffPanel() {
  const [staff, setStaff] = useState<StaffRow[]>([]);
  const [selected, setSelected] = useState<StaffRow | null>(null);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(false);
  const [editing, setEditing] = useState<StaffRow | null | "new">(null);

  async function load() {
    setLoading(true);
    try {
      const data = await getStaff();
      setStaff(data);
      setSelected((cur) => (cur ? data.find((r) => r.id === cur.id) ?? null : null));
    } catch (err) {
      setStaff([]);
      void messageDialog("Failed to load staff.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function onDelete() {
    if (!selected) return;
    const name = `${selected.firstName ?? ""} ${selected.surname ?? ""}`.trim();
    if (!(await confirmDialog(`Delete user "${name}"?`))) return;
    try {
      await deleteStaff(selected.id);
      await load();
    } catch (err) {
      void messageDialog("Failed to delete.\n" + (err as Error).message);
    }
  }

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return staff;
    return staff.filter((s) =>
      [s.firstName, s.surname, s.username].some((v) => v?.toLowerCase().includes(term)),
    );
  }, [staff, term]);

  const columns: Column<StaffRow>[] = [
    {
      key: "firstName",
      header: "First Name",
      render: (r) => <Highlight text={r.firstName} term={term} />,
    },
    {
      key: "surname",
      header: "Surname",
      render: (r) => <Highlight text={r.surname} term={term} />,
    },
    {
      key: "username",
      header: "Username",
      render: (r) => <Highlight text={r.username} term={term} />,
    },
    { key: "userTypeId", header: "Type", width: 90, render: (r) => userTypeLabel(r.userTypeId) },
    {
      key: "roleIds",
      header: "Roles",
      render: (r) => r.roleIds.map(roleLabel).join(", "),
    },
    {
      key: "disabled",
      header: "Status",
      width: 90,
      render: (r) => (r.disabled ? "Disabled" : "Active"),
    },
  ];

  return (
    <div className="po-main">
      <div className="toolbar">
        <input
          className="form-input toolbar-search"
          placeholder="Search staff…"
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
        emptyText={loading ? "Loading…" : "No staff"}
      />

      {editing !== null && (
        <StaffEditModal
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
