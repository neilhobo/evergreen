import { useEffect, useMemo, useState } from "react";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  LeaveEmployee,
  LeaveRequestRow,
  LeaveStatus,
  cancelLeaveRequest,
  decideLeaveRequest,
  fmtDays,
  fmtRange,
  getLeaveEmployees,
  getLeaveRequests,
} from "../../api/annualLeave";
import { fmtDateTime } from "../../format";
import type { PanelProps } from "./AnnualLeavePage";
import AddLeaveModal from "./AddLeaveModal";
import { CheckList, StatusChip, flagLine } from "./parts";

type Filter = "waiting" | "approved" | "declined" | "all";

// Requests: the list down the left (waiting first), the chosen one on the right with what it
// costs, the policy checks, a note for the employee and Approve / Decline. Approved leave can be
// cancelled from here too.
export default function RequestsPanel({ year, version, onChanged, onMeta }: PanelProps) {
  const [requests, setRequests] = useState<LeaveRequestRow[] | null>(null);
  const [employees, setEmployees] = useState<LeaveEmployee[]>([]);
  const [filter, setFilter] = useState<Filter>("waiting");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);
  const [note, setNote] = useState("");
  const [sendEmail, setSendEmail] = useState(true);
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

  const waitingCount = requests?.filter((r) => r.status === LeaveStatus.Waiting).length ?? 0;
  useEffect(() => {
    onMeta(requests ? `${waitingCount} waiting` : "");
  }, [requests, waitingCount, onMeta]);

  const term = search.trim().toLowerCase();
  const shown = useMemo(() => {
    const list = (requests ?? []).filter(
      (r) =>
        (filter === "all" ||
          (filter === "waiting" && r.status === LeaveStatus.Waiting) ||
          (filter === "approved" && r.status === LeaveStatus.Approved) ||
          (filter === "declined" && (r.status === LeaveStatus.Declined || r.status === LeaveStatus.Cancelled))) &&
        (!term || r.employeeName.toLowerCase().includes(term)),
    );
    // Waiting: soonest leave first (what needs deciding first). Otherwise latest leave first.
    return filter === "waiting"
      ? list.sort((a, b) => a.startDate.localeCompare(b.startDate))
      : list.sort((a, b) => b.startDate.localeCompare(a.startDate));
  }, [requests, filter, term]);

  // Keep a selection that is still on show, else the first one.
  const selected = shown.find((r) => r.id === selectedId) ?? shown[0] ?? null;
  const emp = selected ? employees.find((e) => e.id === selected.employeeId) : undefined;

  useEffect(() => {
    setNote("");
    setSendEmail(true);
  }, [selected?.id]);

  async function decide(approve: boolean) {
    if (!selected || busy) return;
    setBusy(true);
    try {
      const res = await decideLeaveRequest(selected.id, approve, note.trim() || null, sendEmail && !!emp?.email);
      if (sendEmail && emp?.email && !res.emailed)
        void messageDialog(
          `The request was ${approve ? "approved" : "declined"}, but the email to ${emp.email} didn't go. ` +
            "Check Super Admin > Email Setup.",
        );
      onChanged();
      await load();
    } catch (err) {
      void messageDialog((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function cancel() {
    if (!selected || busy) return;
    if (!(await confirmDialog(`Cancel ${selected.employeeName}'s leave on ${fmtRange(selected.startDate, selected.endDate, selected.dayLength)}? The days go back on their balance.`)))
      return;
    setBusy(true);
    try {
      await cancelLeaveRequest(selected.id);
      onChanged();
      await load();
    } catch (err) {
      void messageDialog((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const FILTERS: [Filter, string][] = [
    ["waiting", `Waiting ${waitingCount}`],
    ["approved", "Approved"],
    ["declined", "Declined"],
    ["all", "All"],
  ];

  return (
    <div className="al-panel">
      <div className="al-split">
        <aside className="al-req-list">
          <div className="al-req-filters">
            {FILTERS.map(([k, label]) => (
              <button key={k} type="button" className={`al-seg${filter === k ? " on" : ""}`} onClick={() => setFilter(k)}>
                {label}
              </button>
            ))}
          </div>
          <div className="al-req-filters">
            <input
              className="form-input"
              style={{ flex: 1 }}
              placeholder="Search name…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <button className="btn-blue" onClick={() => setAdding(true)} disabled={!requests}>
              + Add leave
            </button>
          </div>
          <div className="al-req-scroll">
            {requests === null && <div className="al-empty" style={{ padding: 20 }}>Loading…</div>}
            {requests && shown.length === 0 && (
              <div className="al-empty" style={{ padding: 20 }}>
                {filter === "waiting" ? "Nothing waiting for approval." : "No requests."}
              </div>
            )}
            {shown.map((r) => {
              const flag = r.status === LeaveStatus.Waiting ? flagLine(r.checks) : null;
              return (
                <button
                  key={r.id}
                  type="button"
                  className={`al-req-item${selected?.id === r.id ? " on" : ""}`}
                  onClick={() => setSelectedId(r.id)}
                >
                  <div className="al-req-item-top">
                    <span className="al-req-item-name">{r.employeeName}</span>
                    {filter === "all" || filter === "declined" ? <StatusChip status={r.status} /> : <span className="al-muted">{r.categoryName}</span>}
                  </div>
                  <div>
                    {fmtRange(r.startDate, r.endDate, r.dayLength)} · {fmtDays(r.days)} day{r.days === 1 ? "" : "s"}
                  </div>
                  {flag && <div className={`al-req-item-flag ${flag.warn ? "al-flag-warn" : "al-flag-ok"}`}>{flag.text}</div>}
                </button>
              );
            })}
          </div>
        </aside>

        <section className="al-detail">
          {!selected ? (
            requests && (
              <div className="al-empty">
                <strong>No request chosen</strong>
                Pick one on the left, or press <b>+ Add leave</b> to enter a Leave Request Form.
              </div>
            )
          ) : (
            <>
              <div className="al-detail-head">
                <div>
                  <div className="al-detail-name">{selected.employeeName}</div>
                  <div className="al-detail-sub">
                    {selected.categoryName}
                    {emp?.approverName ? ` · approver ${emp.approverName}` : ""}
                    {` · entered ${fmtDateTime(selected.dateCreated)}`}
                    {selected.requestedByName ? ` by ${selected.requestedByName}` : ""}
                  </div>
                </div>
                <div className="al-detail-when">
                  {fmtRange(selected.startDate, selected.endDate, selected.dayLength)}
                  <div className="al-detail-sub">
                    {fmtDays(selected.days)} day{selected.days === 1 ? "" : "s"} · <StatusChip status={selected.status} />
                  </div>
                </div>
              </div>

              {emp && (
                <div className="al-tiles">
                  <div className="al-tile">
                    <div className="al-tile-label">Floating days</div>
                    <div className="al-tile-value">{fmtDays(emp.floating)}</div>
                  </div>
                  <div className="al-tile">
                    <div className="al-tile-label">Used</div>
                    <div className="al-tile-value">{fmtDays(emp.used)}</div>
                  </div>
                  <div className={`al-tile${emp.remaining < 0 ? " bad" : ""}`}>
                    <div className="al-tile-label">Left now</div>
                    <div className="al-tile-value">{fmtDays(emp.remaining)}</div>
                  </div>
                  {selected.status === LeaveStatus.Waiting ? (
                    <div className={`al-tile${selected.remainingAfter < 0 ? " bad" : ""}`}>
                      <div className="al-tile-label">Left if approved</div>
                      <div className="al-tile-value">{fmtDays(selected.remainingAfter)}</div>
                    </div>
                  ) : (
                    <div className="al-tile">
                      <div className="al-tile-label">Waiting</div>
                      <div className="al-tile-value">{fmtDays(emp.waiting)}</div>
                    </div>
                  )}
                </div>
              )}

              {selected.note && (
                <dl className="al-facts">
                  <dt>Note</dt>
                  <dd>{selected.note}</dd>
                </dl>
              )}

              {selected.status === LeaveStatus.Waiting ? (
                <>
                  <div className="al-checks">
                    <div className="al-checks-title">Policy checks</div>
                    <CheckList checks={selected.checks} />
                  </div>
                  <div className="form-field">
                    <label className="form-label" htmlFor="al-note">
                      Note to {selected.employeeName.split(" ")[0]} (optional)
                    </label>
                    <textarea
                      id="al-note"
                      className="form-input form-textarea"
                      rows={3}
                      value={note}
                      onChange={(e) => setNote(e.target.value)}
                    />
                  </div>
                  <div className="al-actions">
                    <label className="al-check-label" title={emp?.email ? emp.email : "No email address on their Staff record"}>
                      <input
                        type="checkbox"
                        checked={sendEmail && !!emp?.email}
                        disabled={!emp?.email}
                        onChange={(e) => setSendEmail(e.target.checked)}
                      />
                      {emp?.email ? `Email ${emp.email}` : "No email address on file"}
                    </label>
                    <span className="spacer" />
                    <button className="al-btn-danger" onClick={() => void decide(false)} disabled={busy}>
                      Decline
                    </button>
                    <button
                      className="btn-blue"
                      onClick={() => void decide(true)}
                      disabled={busy || selected.checks.some((c) => c.level === "error")}
                    >
                      Approve
                    </button>
                  </div>
                </>
              ) : (
                <>
                  <dl className="al-facts">
                    <dt>{selected.status === LeaveStatus.Approved ? "Approved by" : selected.status === LeaveStatus.Declined ? "Declined by" : "Cancelled by"}</dt>
                    <dd>
                      {selected.decidedByName || "—"}
                      {selected.dateDecided ? `, ${fmtDateTime(selected.dateDecided)}` : ""}
                    </dd>
                    {selected.decisionNote && (
                      <>
                        <dt>Note</dt>
                        <dd>{selected.decisionNote}</dd>
                      </>
                    )}
                  </dl>
                  {selected.status === LeaveStatus.Approved && (
                    <div className="al-actions">
                      <span className="spacer" />
                      <button className="al-btn-danger" onClick={() => void cancel()} disabled={busy}>
                        Cancel this leave
                      </button>
                    </div>
                  )}
                </>
              )}
            </>
          )}
        </section>
      </div>

      {adding && (
        <AddLeaveModal
          employees={employees}
          onClose={(saved) => {
            setAdding(false);
            if (saved) {
              onChanged();
              void load();
            }
          }}
        />
      )}
    </div>
  );
}
