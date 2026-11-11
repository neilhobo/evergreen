import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import {
  Adjustment,
  Holiday,
  LeaveCategory,
  LeaveEmployee,
  WorkedDay,
  addAdjustment,
  addWorkedDay,
  deleteAdjustment,
  deleteWorkedDay,
  fmtDay,
  fmtDays,
  fmtLeft,
  getAdjustments,
  getHolidays,
  getWorkedDays,
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
  // Company days they work instead, and the year's company days to choose from.
  const [worked, setWorked] = useState<WorkedDay[]>([]);
  const [holidays, setHolidays] = useState<Holiday[]>([]);
  const [workHolidayId, setWorkHolidayId] = useState<number | "">("");
  const [workReason, setWorkReason] = useState("");
  // An adjustment or worked day added or removed counts as a change even if the dialog is then cancelled.
  const [adjChanged, setAdjChanged] = useState(false);

  useEffect(() => {
    getLeaveCategories(year).then(setCategories).catch(() => {});
    getLeaveApprovers().then(setApprovers).catch(() => {});
    getAdjustments(employee.id, year).then(setAdjustments).catch(() => {});
    getWorkedDays(employee.id, year).then(setWorked).catch(() => {});
    getHolidays(year).then(setHolidays).catch(() => {});
  }, [employee, year]);

  // Their company days (by the saved category) not already being worked, inside their employment.
  const workable = holidays.filter(
    (h) =>
      h.categoryIds.includes(employee.categoryId) &&
      !worked.some((w) => w.holidayId === h.id) &&
      (!employee.startDate || h.date >= employee.startDate) &&
      (!employee.endDate || h.date <= employee.endDate),
  );

  async function addWork() {
    if (workHolidayId === "" || !workReason.trim()) return;
    try {
      await addWorkedDay(employee.id, workHolidayId, workReason.trim());
      setWorked(await getWorkedDays(employee.id, year));
      setWorkHolidayId("");
      setWorkReason("");
      setAdjChanged(true);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function removeWork(w: WorkedDay) {
    try {
      await deleteWorkedDay(w.id);
      setWorked(await getWorkedDays(employee.id, year));
      setAdjChanged(true);
    } catch (e) {
      setError((e as Error).message);
    }
  }

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
          {" "}· {fmtDays(employee.used)} used · <b className={employee.remaining < 0 ? "al-neg" : undefined}>
            {employee.remaining < 0 ? fmtLeft(employee.remaining) : `${fmtDays(employee.remaining)} left`}
          </b>
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
      <div className="al-adj">
        <div className="form-label">
          Company days worked in {year}: each one becomes a floating day instead, so their total stays the same
        </div>
        <table className="al-grid" style={{ marginTop: 6 }}>
          <thead>
            <tr>
              <th style={{ width: 100 }}>Date</th>
              <th style={{ width: 140 }}>Company day</th>
              <th>Reason</th>
              <th style={{ width: 130 }}>By</th>
              <th style={{ width: 50 }} />
            </tr>
          </thead>
          <tbody>
            {worked.length === 0 && (
              <tr>
                <td colSpan={5} className="al-muted">
                  None: they have all their company days off.
                </td>
              </tr>
            )}
            {worked.map((w) => (
              <tr key={w.id}>
                <td>{fmtDay(w.date)}</td>
                <td>{w.holidayName}</td>
                <td>{w.reason}</td>
                <td>{w.createdByName ?? ""}</td>
                <td>
                  <button className="al-link danger" onClick={() => void removeWork(w)}>
                    Remove
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className="al-adj-add">
          <select
            className="form-input"
            style={{ width: 210 }}
            value={workHolidayId}
            onChange={(e) => setWorkHolidayId(e.target.value ? Number(e.target.value) : "")}
          >
            <option value="">{workable.length ? "Company day…" : "No company days left to work"}</option>
            {workable.map((h) => (
              <option key={h.id} value={h.id}>
                {fmtDay(h.date)} — {h.name}
              </option>
            ))}
          </select>
          <input
            className="form-input"
            placeholder="Reason, e.g. covering the weighbridge, agreed by Justina"
            value={workReason}
            onChange={(e) => setWorkReason(e.target.value)}
          />
          <button className="toolbar-btn" onClick={() => void addWork()} disabled={workHolidayId === "" || !workReason.trim()}>
            Add
          </button>
        </div>
      </div>
      {error && <p className="al-error">{error}</p>}
    </Modal>
  );
}
