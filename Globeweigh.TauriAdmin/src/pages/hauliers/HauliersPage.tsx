import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import { deleteHaulier, getHauliers, HaulierRow } from "../../api/hauliers";
import HaulierEditModal from "./HaulierEditModal";
import { EMAIL_COLUMNS, toEmailRows } from "../../components/EmailList";
import { AdminUserDto, isSuperAdmin } from "../../types";
import { fmtDateTime } from "../../format";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import "./Hauliers.css";

// Hauliers master list — mirrors the WPF HaulierListView (Add/Edit/Delete), with the
// selected haulier's email addresses in a panel beside the grid, so they can be read
// without opening Edit.
export default function HauliersPage({ user }: { user: AdminUserDto }) {
  const [rows, setRows] = useState<HaulierRow[]>([]);
  const [selected, setSelected] = useState<HaulierRow | null>(null);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(false);
  const [editing, setEditing] = useState<HaulierRow | null | "new">(null);

  async function refresh() {
    setLoading(true);
    try {
      const data = await getHauliers();
      setRows(data);
      setSelected((cur) => (cur ? data.find((r) => r.id === cur.id) ?? null : null));
    } catch (err) {
      void messageDialog("Failed to load hauliers.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  async function onDelete() {
    if (!selected) return;
    if (!(await confirmDialog(`Delete haulier "${selected.name}"?`))) return;
    try {
      await deleteHaulier(selected.id);
      await refresh();
    } catch (err) {
      void messageDialog("Failed to delete.\n" + (err as Error).message);
    }
  }

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return rows;
    return rows.filter((r) =>
      [r.name, r.code, r.contactName, r.tel, ...r.emails].some((v) => v?.toLowerCase().includes(term)),
    );
  }, [rows, term]);

  // Audit columns are Globeweigh-internal: built only for the super admin, and shaded
  // lavender by DataTable (Column.superAdmin) so it's clear the client never sees them.
  const superAdmin = isSuperAdmin(user);

  const columns: Column<HaulierRow>[] = [
    { key: "code", header: "Code", width: 90, render: (r) => <Highlight text={r.code} term={term} /> },
    { key: "name", header: "Name", render: (r) => <Highlight text={r.name} term={term} /> },
    {
      key: "intakeHaulier",
      header: "Intake",
      width: 84,
      align: "center",
      // A tag rather than a tick: this flag is what puts a haulier on the Touch Weigh In
      // list, so it needs to read at a glance down the grid.
      render: (r) => (r.intakeHaulier ? <span className="row-badge row-badge-intake">Intake</span> : ""),
      // Sorting on the rendered cell would put the blanks last in both directions
      // (DataTable sinks blanks); sort the flag instead, tagged rows first ascending.
      sortValue: (r) => (r.intakeHaulier ? 0 : 1),
    },
    { key: "contactName", header: "Contact", render: (r) => <Highlight text={r.contactName} term={term} /> },
    { key: "tel", header: "Telephone", width: 130 },
    // How many addresses the haulier has — the addresses themselves are in the panel
    // beside the grid, so this is for spotting the hauliers that have none.
    {
      key: "emails",
      header: "Emails",
      width: 70,
      align: "center",
      render: (r) => r.emails.length || "",
      sortValue: (r) => r.emails.length,
    },
    ...(superAdmin
      ? ([
          {
            key: "dateCreated",
            header: "Created",
            width: 115,
            superAdmin: true,
            render: (r) => fmtDateTime(r.dateCreated),
          },
          {
            key: "createdBy",
            header: "Created By",
            width: 140,
            superAdmin: true,
          },
        ] as Column<HaulierRow>[])
      : []),
  ];

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title="Hauliers" meta={`${filtered.length} hauliers`} />
        <div className={cc.body}>
          <div className="po-open-layout">
            <div className="po-main">
              <div className="toolbar">
                <input
                  className="form-input toolbar-search"
                  placeholder="Search hauliers…"
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
                  <button className="toolbar-btn" onClick={() => void refresh()} disabled={loading}>
                    Refresh
                  </button>
                </div>
              </div>

              <DataTable
                sortable
                columns={columns}
                rows={filtered}
                rowKey={(r) => r.id}
                selectedKey={selected?.id ?? null}
                onSelect={setSelected}
                onDoubleClick={(r) => setEditing(r)}
                emptyText={loading ? "Loading…" : "No hauliers"}
              />
            </div>

            {/* The selected haulier's email addresses, read-only: Edit (or a double-click
                on the row) is where they change. */}
            <div className="po-side" style={{ width: 320 }}>
              <h3 className="po-side-title">Emails</h3>
              {selected ? (
                <DataTable
                  columns={EMAIL_COLUMNS}
                  rows={toEmailRows(selected.emails)}
                  rowKey={(r) => r.email}
                  emptyText="No email addresses"
                />
              ) : (
                <div className="hl-emails-empty">Select a haulier to see its email addresses</div>
              )}
            </div>
          </div>
        </div>
      </div>

      {editing !== null && (
        <HaulierEditModal
          existing={editing === "new" ? null : editing}
          onClose={(saved) => {
            setEditing(null);
            if (saved) void refresh();
          }}
        />
      )}
    </div>
  );
}
