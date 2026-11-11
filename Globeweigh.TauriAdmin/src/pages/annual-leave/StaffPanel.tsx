import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import { LeaveEmployee, fmtDate, fmtDays, getLeaveEmployees } from "../../api/annualLeave";
import type { PanelProps } from "./AnnualLeavePage";
import EmployeeModal from "./EmployeeModal";

// Staff: the spreadsheet, live — everyone with their category and the year's balance in the
// spreadsheet's own columns (Total / Set / Float / Used / Remaining), overdrawn balances in red.
// Double-click (or Edit) for their details and any manual adjustment. The people are Admin >
// Staff's: someone is added to (or taken out of) Annual Leave on their Staff record, not here.
export default function StaffPanel({ year, version, onChanged, onMeta }: PanelProps) {
  const [rows, setRows] = useState<LeaveEmployee[] | null>(null);
  const [selected, setSelected] = useState<LeaveEmployee | null>(null);
  const [categoryId, setCategoryId] = useState<number | "">("");
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<LeaveEmployee | null>(null);

  async function load() {
    try {
      setRows(await getLeaveEmployees(year));
    } catch (err) {
      setRows((cur) => cur ?? []);
      void messageDialog("Failed to load staff.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [year, version]);

  const categories = useMemo(() => {
    const m = new Map<number, string>();
    (rows ?? []).forEach((e) => m.set(e.categoryId, e.categoryName));
    return [...m.entries()].sort((a, b) => a[1].localeCompare(b[1]));
  }, [rows]);

  const term = search.trim().toLowerCase();
  const shown = useMemo(
    () =>
      (rows ?? []).filter(
        (e) =>
          (categoryId === "" || e.categoryId === categoryId) &&
          (!term || `${e.forename} ${e.surname}`.toLowerCase().includes(term)),
      ),
    [rows, categoryId, term],
  );

  const overdrawn = shown.filter((e) => e.remaining < 0).length;
  useEffect(() => {
    onMeta(rows ? `${shown.length} staff${overdrawn ? ` · ${overdrawn} with unpaid days` : ""}` : "");
  }, [rows, shown.length, overdrawn, onMeta]);

  const num = (key: keyof LeaveEmployee, header: string, width = 70): Column<LeaveEmployee> => ({
    key,
    header,
    width,
    align: "right",
    render: (e) => fmtDays(e[key] as number),
    footer: (list) => fmtDays(list.reduce((n, e) => n + (e[key] as number), 0)),
  });

  const columns: Column<LeaveEmployee>[] = [
    { key: "surname", header: "Surname", width: 130, render: (e) => <Highlight text={e.surname} term={term} />, footer: (l) => `${l.length} staff` },
    { key: "forename", header: "Forename", width: 120, render: (e) => <Highlight text={e.forename} term={term} /> },
    { key: "categoryName", header: "Category", width: 120 },
    {
      key: "startDate",
      header: "Started",
      width: 90,
      render: (e) => fmtDate(e.startDate),
      sortValue: (e) => e.startDate ?? "",
    },
    { key: "endDate", header: "Leaving", width: 90, render: (e) => fmtDate(e.endDate), sortValue: (e) => e.endDate ?? "" },
    num("total", "Total"),
    { ...num("set", "Set", 55), footer: undefined },
    {
      ...num("floating", "Float"),
      render: (e) => (
        <span
          title={
            [
              e.adjustments ? `Includes ${fmtDays(e.adjustments)} adjustment` : "",
              e.worked ? `Includes ${e.worked} company day${e.worked === 1 ? "" : "s"} worked` : "",
            ]
              .filter(Boolean)
              .join("; ") || undefined
          }
        >
          {fmtDays(e.floating)}
          {e.adjustments || e.worked ? "*" : ""}
        </span>
      ),
    },
    num("used", "Used"),
    num("waiting", "Waiting"),
    {
      key: "remaining",
      header: "Remaining",
      width: 85,
      align: "right",
      className: "al-remaining",
      render: (e) => <span className={e.remaining < 0 ? "al-neg" : undefined}>{fmtDays(Math.max(0, e.remaining))}</span>,
      sortValue: (e) => e.remaining,
      footer: (list) => fmtDays(list.reduce((n, e) => n + Math.max(0, e.remaining), 0)),
    },
    {
      key: "unpaid",
      header: "Unpaid",
      width: 70,
      align: "right",
      render: (e) => (e.unpaid ? <span className="al-neg" title="Past their paid allowance — unpaid (leave policy)">{fmtDays(e.unpaid)}</span> : ""),
      footer: (list) => {
        const n = list.reduce((t, e) => t + e.unpaid, 0);
        return n ? fmtDays(n) : "";
      },
    },
    { key: "approverName", header: "Approver", width: 120 },
    {
      key: "emailTo",
      header: "Email",
      render: (e) => <span className={e.email ? undefined : "al-muted"} title={e.email ? undefined : "Their login"}>{e.emailTo ?? ""}</span>,
    },
  ];

  return (
    <div className="al-panel po-main">
      <div className="toolbar">
        <select className="form-input" style={{ width: 170 }} value={categoryId} onChange={(e) => setCategoryId(e.target.value ? Number(e.target.value) : "")}>
          <option value="">All categories</option>
          {categories.map(([id, name]) => (
            <option key={id} value={id}>
              {name}
            </option>
          ))}
        </select>
        <input className="form-input toolbar-search" placeholder="Search staff…" value={search} onChange={(e) => setSearch(e.target.value)} />
        <span className="toolbar-note">Float * = includes a manual adjustment or a company day worked · Unpaid = days past the paid allowance</span>
        <div className="toolbar-right">
          <button className="toolbar-btn" disabled={!selected} onClick={() => selected && setEditing(selected)}>
            Edit
          </button>
        </div>
      </div>
      <DataTable
        columns={columns}
        rows={shown}
        rowKey={(e) => e.id}
        selectedKey={selected?.id ?? null}
        onSelect={setSelected}
        onDoubleClick={(e) => setEditing(e)}
        rowClass={(e) => (e.remaining < 0 ? "al-row-neg" : undefined)}
        emptyText={rows === null ? "Loading…" : "Nobody yet — tick Annual Leave on someone in Admin › Staff"}
        sortable
      />

      {editing && (
        <EmployeeModal
          employee={editing}
          year={year}
          onClose={(changed) => {
            setEditing(null);
            if (changed) {
              onChanged();
              void load();
            }
          }}
        />
      )}
    </div>
  );
}
