import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import {
  LeaveEmployee,
  RequestPreview,
  createLeaveRequest,
  fmtDays,
  previewLeaveRequest,
} from "../../api/annualLeave";
import { CheckList } from "./parts";

interface Props {
  employees: LeaveEmployee[];
  // Opened from the calendar: that person and day.
  employeeId?: number;
  date?: string;
  onClose: (saved: boolean) => void;
}

// Add leave: how the office enters a holiday request — today from the paper Leave Request Form,
// later the employees' own requests arrive the same way. Shows what it would cost and the policy
// checks as the dates are picked; can be entered already approved (a signed form) or left
// waiting for the approver.
export default function AddLeaveModal({ employees, employeeId, date, onClose }: Props) {
  const [empId, setEmpId] = useState<number | "">(employeeId ?? "");
  const [start, setStart] = useState(date ?? "");
  const [end, setEnd] = useState(date ?? "");
  const [length, setLength] = useState(1);
  const [note, setNote] = useState("");
  const [approve, setApprove] = useState(true);
  const [preview, setPreview] = useState<RequestPreview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const single = start !== "" && start === end;
  const ready = empId !== "" && start !== "" && end !== "";

  // The preview follows every change, a moment after typing stops.
  useEffect(() => {
    setPreview(null);
    if (!ready) return;
    const t = window.setTimeout(() => {
      previewLeaveRequest({ employeeId: Number(empId), startDate: start, endDate: end, dayLength: single ? length : 1 })
        .then(setPreview)
        .catch((e) => setError((e as Error).message));
    }, 250);
    return () => window.clearTimeout(t);
  }, [empId, start, end, length, single, ready]);

  const blocked = preview?.checks.some((c) => c.level === "error") ?? true;

  async function save() {
    if (!ready || blocked || busy) return;
    setBusy(true);
    setError(null);
    try {
      await createLeaveRequest({
        employeeId: Number(empId),
        startDate: start,
        endDate: end,
        dayLength: single ? length : 1,
        note: note.trim() || null,
        approve,
      });
      onClose(true);
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title="Add Leave"
      width={560}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)} disabled={busy}>
            Cancel
          </button>
          <button className="btn-blue" onClick={save} disabled={!ready || blocked || busy}>
            {busy ? "Saving…" : approve ? "Save as approved" : "Save as waiting"}
          </button>
        </>
      }
    >
      <div className="form-grid">
        <div className="form-field form-field-wide">
          <label className="form-label">Employee</label>
          <select className="form-input" value={empId} onChange={(e) => setEmpId(e.target.value ? Number(e.target.value) : "")} autoFocus={!employeeId}>
            <option value="">Choose…</option>
            {employees.map((e) => (
              <option key={e.id} value={e.id}>
                {e.surname}, {e.forename} — {e.categoryName} ({fmtDays(e.remaining)} left)
              </option>
            ))}
          </select>
        </div>
        <div className="form-field">
          <label className="form-label">First day</label>
          <input
            className="form-input"
            type="date"
            value={start}
            onChange={(e) => {
              setStart(e.target.value);
              if (!end || e.target.value > end) setEnd(e.target.value);
            }}
          />
        </div>
        <div className="form-field">
          <label className="form-label">Last day</label>
          <input className="form-input" type="date" value={end} min={start || undefined} onChange={(e) => setEnd(e.target.value)} />
        </div>
        {single && (
          <div className="form-field form-field-wide">
            <label className="form-label">How long</label>
            <div className="al-lengths">
              {[
                [1, "Full day"],
                [0.5, "Half day"],
                [0.25, "Quarter day"],
              ].map(([v, label]) => (
                <button key={v} type="button" className={`al-seg${length === v ? " on" : ""}`} onClick={() => setLength(v as number)}>
                  {label}
                </button>
              ))}
            </div>
          </div>
        )}
        <div className="form-field form-field-wide">
          <label className="form-label">Note (optional)</label>
          <input className="form-input" value={note} onChange={(e) => setNote(e.target.value)} placeholder="e.g. Leave Request Form handed in 3 Nov" />
        </div>
        <div className="form-field form-field-wide">
          <label className="al-check-label">
            <input type="checkbox" checked={approve} onChange={(e) => setApprove(e.target.checked)} />
            Already approved (a signed form) — untick to leave it waiting for the approver
          </label>
        </div>
      </div>

      {preview && (
        <div className="al-preview">
          <div className="al-preview-sum">
            <span>
              Uses {fmtDays(preview.days)} day{preview.days === 1 ? "" : "s"}
            </span>
            <span className={preview.remainingAfter < 0 ? "al-neg" : undefined}>
              {fmtDays(preview.remainingBefore)} → {fmtDays(preview.remainingAfter)} floating left
            </span>
          </div>
          <CheckList checks={preview.checks} />
        </div>
      )}
      {error && <p className="al-error">{error}</p>}
    </Modal>
  );
}
