import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import {
  Adjustment,
  LeaveCategory,
  LeaveEmployee,
  addAdjustment,
  deleteAdjustment,
  fmtDays,
  getAdjustments,
  getLeaveApprovers,
  getLeaveCategories,
  saveLeaveProfile,
} from "../../api/annualLeave";

interface Props {
  employee: LeaveEmployee;
  year: number;
  onClose: (changed: boolean) => void;
}

// One person's leave: their category (which decides their set days), start and leaving dates
// (which pro-rate their 28), contact details for the approval emails, who approves their leave,
// and the year's manual adjustments (+/- floating days with a reason) for anything the rules
// don't cover. Who they are is Admin > Staff's: people are added, renamed and taken out of
// Annual Leave there, not here.
export default function EmployeeModal({ employee, year, onClose }: Props) {
  const [categoryId, setCategoryId] = useState<number | "">(employee?.categoryId ?? "");
  const [startDate, setStartDate] = useState(employee?.startDate ?? "");
  const [endDate, setEndDate] = useState(employee?.endDate ?? "");
  const [email, setEmail] = useState(employee?.email ?? "");
  const [mobile, setMobile] = useState(employee?.mobile ?? "");
  const [approverUserId, setApproverUserId] = useState<number | "">(employee?.approverUserId ?? "");
  const [notes, setNotes] = useState(employee?.notes ?? "");
  const [categories, setCategories] = useState<LeaveCategory[]>([]);
  const [approvers, setApprovers] = useState<{ id: number; name: string }[]>([]);
  const [adjustments, setAdjustments] = useState<Adjustment[]>([]);
  const [adjDays, setAdjDays] = useState("");
  const [adjReason, setAdjReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  // An adjustment added or removed counts as a change even if the dialog is then cancelled.
  const [adjChanged, setAdjChanged] = useState(false);

  useEffect(() => {
    getLeaveCategories(year).then(setCategories).catch(() => {});
    getLeaveApprovers().then(setApprovers).catch(() => {});
    getAdjustments(employee.id, year).then(setAdjustments).catch(() => {});
  }, [employee, year]);

  const valid = categoryId !== "";
  // Their login, when it is an email address: where approval emails go if Email is left blank.
  const loginEmail = !employee.email && employee.emailTo ? employee.emailTo : null;

  async function save() {
    if (!valid || busy) return;
    setBusy(true);
    setError(null);
    const input = {
      enrolled: true,
      categoryId: Number(categoryId),
      startDate: startDate || null,
      endDate: endDate || null,
      email: email || null,
      mobile: mobile || null,
      approverUserId: approverUserId === "" ? null : approverUserId,
      notes: notes || null,
    };
    try {
      await saveLeaveProfile(employee.userId, input);
      onClose(true);
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }

  async function addAdj() {
    const days = Number(adjDays);
    if (!days || !adjReason.trim()) return;
    try {
      await addAdjustment(employee.id, year, days, adjReason.trim());
      setAdjustments(await getAdjustments(employee.id, year));
      setAdjDays("");
      setAdjReason("");
      setAdjChanged(true);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function removeAdj(a: Adjustment) {
    try {
      await deleteAdjustment(a.id);
      setAdjustments(await getAdjustments(employee.id, year));
      setAdjChanged(true);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <Modal
      title={`${employee.forename} ${employee.surname}`}
      width={620}
      onClose={() => onClose(adjChanged)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(adjChanged)} disabled={busy}>
            Cancel
          </button>
          <button className="btn-blue" onClick={save} disabled={!valid || busy}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div className="form-grid">
        <p className="al-muted form-field-wide" style={{ margin: "0 0 4px" }}>
          Name, login and taking someone out of Annual Leave are in Admin › Staff.
        </p>
        <div className="form-field">
          <label className="form-label">Category</label>
          <select className="form-input" value={categoryId} onChange={(e) => setCategoryId(e.target.value ? Number(e.target.value) : "")} autoFocus>
            <option value="">Choose…</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name} ({fmtDays(c.setDays)} set + {fmtDays(c.annualDays - c.setDays)} floating)
              </option>
            ))}
          </select>
        </div>
        <div className="form-field">
          <label className="form-label">Approver</label>
          <select className="form-input" value={approverUserId} onChange={(e) => setApproverUserId(e.target.value ? Number(e.target.value) : "")}>
            <option value="">—</option>
            {approvers.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
        </div>
        <div className="form-field">
          <label className="form-label">Start date (blank = before this year)</label>
          <input className="form-input" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label">Leaving date</label>
          <input className="form-input" type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label">Email (approval emails go here)</label>
          <input
            className="form-input"
            type="email"
            value={email}
            placeholder={loginEmail ? `Their login: ${loginEmail}` : undefined}
            onChange={(e) => setEmail(e.target.value)}
          />
        </div>
        <div className="form-field">
          <label className="form-label">Mobile (for texts, later)</label>
          <input className="form-input" value={mobile} onChange={(e) => setMobile(e.target.value)} />
        </div>
        <div className="form-field form-field-wide">
          <label className="form-label">Notes</label>
          <input className="form-input" value={notes} onChange={(e) => setNotes(e.target.value)} />
        </div>
      </div>

      <div className="al-adj">
        <div className="form-label">
          {year}: {fmtDays(employee.total)} total − {fmtDays(employee.set)} set = {fmtDays(employee.total - employee.set)} floating
          {employee.adjustments ? `, ${employee.adjustments > 0 ? "+" : ""}${fmtDays(employee.adjustments)} adjusted` : ""}
          {" "}· {fmtDays(employee.used)} used · <b className={employee.remaining < 0 ? "al-neg" : undefined}>{fmtDays(employee.remaining)} left</b>
        </div>
        <table className="al-grid" style={{ marginTop: 6 }}>
          <thead>
            <tr>
              <th className="r" style={{ width: 70 }}>Days</th>
              <th>Adjustment reason</th>
              <th style={{ width: 150 }}>By</th>
              <th style={{ width: 50 }} />
            </tr>
          </thead>
          <tbody>
            {adjustments.length === 0 && (
              <tr>
                <td colSpan={4} className="al-muted">
                  No adjustments for {year}.
                </td>
              </tr>
            )}
            {adjustments.map((a) => (
              <tr key={a.id}>
                <td className="r">
                  {a.days > 0 ? "+" : ""}
                  {fmtDays(a.days)}
                </td>
                <td>{a.reason}</td>
                <td>{a.createdByName ?? ""}</td>
                <td>
                  <button className="al-link danger" onClick={() => void removeAdj(a)}>
                    Remove
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className="al-adj-add">
          <input
            className="form-input"
            style={{ width: 80 }}
            type="number"
            step="0.25"
            placeholder="± days"
            value={adjDays}
            onChange={(e) => setAdjDays(e.target.value)}
          />
          <input className="form-input" placeholder="Reason, e.g. extra day agreed by Justina" value={adjReason} onChange={(e) => setAdjReason(e.target.value)} />
          <button className="toolbar-btn" onClick={() => void addAdj()} disabled={!Number(adjDays) || !adjReason.trim()}>
            Add
          </button>
        </div>
      </div>
      {error && <p className="al-error">{error}</p>}
    </Modal>
  );
}
