import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import Modal from "../../components/Modal";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  LeaveCheck,
  LeaveEmployee,
  LeaveRequestRow,
  LeaveStatus,
  decideLeaveRequest,
  fmtDate,
  fmtDays,
  fmtRange,
  getLeaveEmployees,
  getLeaveRequests,
} from "../../api/annualLeave";
import type { PanelProps } from "./AnnualLeavePage";
import AddLeaveModal from "./AddLeaveModal";
import LeaveRequestModal from "./LeaveRequestModal";
import { StatusChip } from "./parts";

type History = "all" | "approved" | "declined" | "cancelled";

// Requests (design B, https://claude.ai/artifact/Ck5iCcMTNcvEzmWDqdJXmL): an approval queue and a
// history, each a full-width grid with one request to a line.
//  - Waiting: every policy check in its own column (Notice, Allowance, Peak, Clash, and Other
//    for anything that stops it), so one look down a column shows every short-notice request or
//    clash. Approve and Decline are at the end of the line; ⋯ or a double-click opens the request
//    (details, edit, cancel, delete).
//  - Decided: approved, declined and cancelled leave, latest first.
// The checks are the API's own (AnnualLeaveController.AssessAsync); each carries a Key naming its
// column and a Short text for the cell.
export default function RequestsPanel({ year, version, onChanged, onMeta }: PanelProps) {
  const [requests, setRequests] = useState<LeaveRequestRow[] | null>(null);
  const [employees, setEmployees] = useState<LeaveEmployee[]>([]);
  const [history, setHistory] = useState<History>("all");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<LeaveRequestRow | null>(null);
  const [opened, setOpened] = useState<LeaveRequestRow | null>(null);
  const [declining, setDeclining] = useState<LeaveRequestRow | null>(null);
  const [adding, setAdding] = useState(false);
  const [busy, setBusy] = useState(false);

  async function load() {
    try {
      const [r, e] = await Promise.all([getLeaveRequests(year), getLeaveEmployees(year)]);
      setRequests(r);
      setEmployees(e);
    } catch (err) {
      setRequests((cur) => cur ?? []);
      void messageDialog("Failed to load leave requests.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [year, version]);

  const term = search.trim().toLowerCase();
  const match = (r: LeaveRequestRow) => !term || r.employeeName.toLowerCase().includes(term);

  // Waiting: soonest leave first, the order it needs deciding in.
  const waiting = useMemo(
    () => (requests ?? []).filter((r) => r.status === LeaveStatus.Waiting && match(r)).sort((a, b) => a.startDate.localeCompare(b.startDate)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [requests, term],
  );
  const decided = useMemo(
    () =>
      (requests ?? [])
        .filter(
          (r) =>
            r.status !== LeaveStatus.Waiting &&
            match(r) &&
            (history === "all" ||
              (history === "approved" && r.status === LeaveStatus.Approved) ||
              (history === "declined" && r.status === LeaveStatus.Declined) ||
              (history === "cancelled" && r.status === LeaveStatus.Cancelled)),
        )
        .sort((a, b) => b.startDate.localeCompare(a.startDate)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [requests, term, history],
  );

  const warned = waiting.filter((r) => r.checks.some((c) => c.level === "warn" || c.level === "error")).length;
  useEffect(() => {
    onMeta(requests ? `${waiting.length} waiting${warned ? ` · ${warned} with warnings` : ""}` : "");
  }, [requests, waiting.length, warned, onMeta]);

  const emailOf = (r: LeaveRequestRow) => employees.find((e) => e.id === r.employeeId)?.emailTo ?? null;
  const counts = useMemo(() => {
    const all = requests ?? [];
    return {
      approved: all.filter((r) => r.status === LeaveStatus.Approved).length,
      declined: all.filter((r) => r.status === LeaveStatus.Declined).length,
      cancelled: all.filter((r) => r.status === LeaveStatus.Cancelled).length,
    };
  }, [requests]);

  function refresh() {
    onChanged();
    void load();
  }

  async function approve(r: LeaveRequestRow) {
    if (busy) return;
    const to = emailOf(r);
    const warns = r.checks.filter((c) => c.level === "warn").map((c) => `• ${c.text}`);
    const text =
      `Approve ${r.employeeName}'s leave on ${fmtRange(r.startDate, r.endDate, r.dayLength)} (${fmtDays(r.days)} day${r.days === 1 ? "" : "s"})?` +
      (warns.length ? `\n\nThink twice:\n${warns.join("\n")}` : "") +
      (to ? `\n\n${to} will be emailed.` : "\n\nThey have no email address, so they won't be told by email.");
    if (!(await confirmDialog(text))) return;
    setBusy(true);
    try {
      const res = await decideLeaveRequest(r.id, true, null, !!to);
      if (to && !res.emailed) void messageDialog(`Approved, but the email to ${to} didn't go. Check Super Admin > Email Setup.`);
      refresh();
    } catch (err) {
      void messageDialog((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  // The worst check of one kind, for its column: an error over a warning over a pass.
  const RANK: Record<string, number> = { error: 0, warn: 1, info: 2, ok: 3 };
  function worst(r: LeaveRequestRow, keys: string[]): LeaveCheck | null {
    return r.checks.filter((c) => c.key && keys.includes(c.key)).sort((a, b) => RANK[a.level] - RANK[b.level])[0] ?? null;
  }
  const cell = (c: LeaveCheck | null, empty = "—") =>
    c ? (
      <span className={`al-pc al-pc-${c.level}`} title={c.text}>
        {c.short ?? c.text}
      </span>
    ) : (
      <span className="al-muted">{empty}</span>
    );

  const name = (r: LeaveRequestRow) => (
    <b>
      <Highlight text={r.employeeName} term={term} />
    </b>
  );
  const entered = (r: LeaveRequestRow) => `${r.requestedByName ? r.requestedByName.split(" ")[0] + " · " : ""}${fmtDate(r.dateCreated)}`;
  const more = (r: LeaveRequestRow) => (
    <button className="al-ib" title="Open: details, edit, cancel, delete" onClick={() => setOpened(r)}>
      ⋯
    </button>
  );

  const waitingCols: Column<LeaveRequestRow>[] = [
    { key: "employeeName", header: "Employee", width: 150, render: name, footer: (l) => `${l.length} waiting` },
    { key: "categoryName", header: "Category", width: 105 },
    {
      key: "startDate",
      header: "Dates",
      width: 175,
      render: (r) => fmtRange(r.startDate, r.endDate, r.dayLength),
      sortValue: (r) => r.startDate,
    },
    {
      key: "days",
      header: "Days",
      width: 50,
      align: "right",
      render: (r) => fmtDays(r.days),
      footer: (l) => fmtDays(l.reduce((n, r) => n + r.days, 0)),
    },
    { key: "notice", header: "Notice (14d)", width: 90, render: (r) => cell(worst(r, ["notice"])) },
    { key: "allowance", header: "Allowance", width: 100, render: (r) => cell(worst(r, ["allowance"])) },
    { key: "peak", header: "Peak (5d max)", width: 100, render: (r) => cell(worst(r, ["peak"])) },
    { key: "clash", header: "Clash", width: 120, render: (r) => cell(worst(r, ["clash"])) },
    { key: "other", header: "Other", width: 110, render: (r) => cell(worst(r, ["dates", "overlap", "employment"]), "") },
    { key: "note", header: "Note", render: (r) => <span className="al-muted">{r.note ?? ""}</span> },
    { key: "dateCreated", header: "Entered", width: 120, render: entered, sortValue: (r) => r.dateCreated },
    {
      key: "actions",
      header: "",
      width: 172,
      render: (r) => {
        const blocked = r.checks.some((c) => c.level === "error");
        return (
          <span className="al-row-acts">
            <button className="al-btn-sm al-btn-sm-red" onClick={() => setDeclining(r)} disabled={busy}>
              Decline
            </button>
            <button
              className="al-btn-sm al-btn-sm-green"
              onClick={() => void approve(r)}
              disabled={busy || blocked}
              title={blocked ? "Something stops this being approved — see Other" : undefined}
            >
              Approve
            </button>
            {more(r)}
          </span>
        );
      },
    },
  ];

  const decidedCols: Column<LeaveRequestRow>[] = [
    { key: "status", header: "Status", width: 90, render: (r) => <StatusChip status={r.status} />, sortValue: (r) => r.status },
    { key: "employeeName", header: "Employee", width: 150, render: name, footer: (l) => `${l.length} request${l.length === 1 ? "" : "s"}` },
    { key: "categoryName", header: "Category", width: 105 },
    {
      key: "startDate",
      header: "Dates",
      width: 175,
      render: (r) => fmtRange(r.startDate, r.endDate, r.dayLength),
      sortValue: (r) => r.startDate,
    },
    {
      key: "days",
      header: "Days",
      width: 50,
      align: "right",
      render: (r) => fmtDays(r.days),
      footer: (l) => fmtDays(l.reduce((n, r) => n + r.days, 0)),
    },
    { key: "dateCreated", header: "Entered", width: 120, render: entered, sortValue: (r) => r.dateCreated },
    {
      key: "dateDecided",
      header: "Decided",
      width: 140,
      render: (r) => `${r.decidedByName ? r.decidedByName + " · " : ""}${r.dateDecided ? fmtDate(r.dateDecided) : ""}`,
      sortValue: (r) => r.dateDecided ?? "",
    },
    { key: "decisionNote", header: "Decision note", render: (r) => <span className="al-muted">{r.decisionNote ?? ""}</span> },
    { key: "note", header: "Note", width: 180, render: (r) => <span className="al-muted">{r.note ?? ""}</span> },
    { key: "actions", header: "", width: 44, render: more },
  ];

  const HISTORY: [History, string, number | null][] = [
    ["all", "All", null],
    ["approved", "Approved", counts.approved],
    ["declined", "Declined", counts.declined],
    ["cancelled", "Cancelled", counts.cancelled],
  ];

  return (
    <div className="al-panel po-main">
      <div className="toolbar">
        <input className="form-input toolbar-search" placeholder="Search staff…" value={search} onChange={(e) => setSearch(e.target.value)} />
        <span className="toolbar-note">
          <span className="al-pc al-pc-ok">passes</span> <span className="al-pc al-pc-warn">think twice</span>{" "}
          <span className="al-pc al-pc-error">cannot be approved</span> · hover a cell for the full check
        </span>
        <div className="toolbar-right">
          <button className="btn-blue" onClick={() => setAdding(true)} disabled={!requests}>
            + Add leave
          </button>
        </div>
      </div>

      <div className="al-rq-sec al-rq-queue">
        <div className="al-rq-title">
          Waiting for approval <span className="al-chip al-chip-0">{waiting.length}</span>
        </div>
        <DataTable
          columns={waitingCols}
          rows={waiting}
          rowKey={(r) => r.id}
          selectedKey={selected?.id ?? null}
          onSelect={setSelected}
          onDoubleClick={(r) => setOpened(r)}
          rowClass={(r) => (r.checks.some((c) => c.level === "error") ? "al-row-blocked" : undefined)}
          emptyText={requests === null ? "Loading…" : "Nothing waiting for approval."}
          sortable
        />
      </div>

      <div className="al-rq-sec al-rq-history">
        <div className="al-rq-title">
          Decided
          {HISTORY.map(([k, label, n]) => (
            <button key={k} type="button" className={`al-seg${history === k ? " on" : ""}`} onClick={() => setHistory(k)}>
              {label}
              {n != null ? ` ${n}` : ""}
            </button>
          ))}
        </div>
        <DataTable
          columns={decidedCols}
          rows={decided}
          rowKey={(r) => r.id}
          selectedKey={selected?.id ?? null}
          onSelect={setSelected}
          onDoubleClick={(r) => setOpened(r)}
          emptyText={requests === null ? "Loading…" : "No decided leave."}
          sortable
        />
      </div>

      {opened && (
        <LeaveRequestModal
          requestId={opened.id}
          employee={employees.find((e) => e.id === opened.employeeId)}
          onClose={(changed) => {
            setOpened(null);
            if (changed) refresh();
          }}
        />
      )}
      {declining && (
        <DeclineModal
          request={declining}
          emailTo={emailOf(declining)}
          onClose={(done) => {
            setDeclining(null);
            if (done) refresh();
          }}
        />
      )}
      {adding && (
        <AddLeaveModal
          employees={employees}
          onClose={(saved) => {
            setAdding(false);
            if (saved) refresh();
          }}
        />
      )}
    </div>
  );
}

// Decline from the queue: an optional note for the employee, and whether to email them.
function DeclineModal({
  request,
  emailTo,
  onClose,
}: {
  request: LeaveRequestRow;
  emailTo: string | null;
  onClose: (done: boolean) => void;
}) {
  const [note, setNote] = useState("");
  const [email, setEmail] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const first = request.employeeName.split(" ")[0];

  async function go() {
    setBusy(true);
    setError(null);
    try {
      const res = await decideLeaveRequest(request.id, false, note.trim() || null, email && !!emailTo);
      if (email && emailTo && !res.emailed) void messageDialog(`Declined, but the email to ${emailTo} didn't go. Check Super Admin > Email Setup.`);
      onClose(true);
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={`Decline ${request.employeeName}'s Leave`}
      width={480}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)} disabled={busy}>
            Back
          </button>
          <button className="al-btn-danger" onClick={() => void go()} disabled={busy}>
            {busy ? "Declining…" : "Decline"}
          </button>
        </>
      }
    >
      <p style={{ margin: "0 0 10px" }}>
        {fmtRange(request.startDate, request.endDate, request.dayLength)} · {fmtDays(request.days)} day{request.days === 1 ? "" : "s"}
      </p>
      <div className="form-grid">
        <div className="form-field form-field-wide">
          <label className="form-label">Note to {first} (optional)</label>
          <input className="form-input" value={note} autoFocus onChange={(e) => setNote(e.target.value)} placeholder="e.g. Needed for peak cover" />
        </div>
        <div className="form-field form-field-wide">
          <label className="al-check-label" title={emailTo ?? "No email address on their Staff record"}>
            <input type="checkbox" checked={email && !!emailTo} disabled={!emailTo} onChange={(e) => setEmail(e.target.checked)} />
            {emailTo ? `Email ${emailTo}` : "No email address on file"}
          </label>
        </div>
      </div>
      {error && <p className="al-error">{error}</p>}
    </Modal>
  );
}
