import { useState } from "react";
import Modal from "../../components/Modal";
import {
  BlockedPeriod,
  Holiday,
  LeaveCategory,
  createBlockedPeriod,
  createHoliday,
  updateBlockedPeriod,
  updateHoliday,
} from "../../api/annualLeave";

// The Company Holidays tab's two dialogs: a company day, and a leave-discouraged period.

export function HolidayModal({
  holiday,
  year,
  categories,
  onClose,
}: {
  holiday: Holiday | null;
  year: number;
  categories: LeaveCategory[];
  onClose: (saved: boolean) => void;
}) {
  const [date, setDate] = useState(holiday?.date ?? "");
  const [name, setName] = useState(holiday?.name ?? "");
  // A new day is for everyone until a category is unticked.
  const [ids, setIds] = useState<number[]>(holiday?.categoryIds ?? categories.map((c) => c.id));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const valid = date !== "" && name.trim() !== "";

  async function save() {
    if (!valid || busy) return;
    setBusy(true);
    setError(null);
    try {
      const input = { date, name: name.trim(), categoryIds: ids };
      if (holiday) await updateHoliday(holiday.id, input);
      else await createHoliday(input);
      onClose(true);
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={holiday ? "Edit Company Day" : "Add Company Day"}
      width={460}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)} disabled={busy}>
            Cancel
          </button>
          <button className="btn-blue" onClick={save} disabled={!valid || busy}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div className="form-grid">
        <div className="form-field">
          <label className="form-label">Date</label>
          <input
            className="form-input"
            type="date"
            value={date}
            min={`${year}-01-01`}
            max={`${year}-12-31`}
            onChange={(e) => setDate(e.target.value)}
            autoFocus
          />
        </div>
        <div className="form-field">
          <label className="form-label">Calendar event</label>
          <input className="form-input" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Christmas" />
        </div>
        <div className="form-field form-field-wide">
          <label className="form-label">Day off for</label>
          <div className="al-cat-ticks">
            {categories.map((c) => (
              <label key={c.id} className="al-check-label">
                <input
                  type="checkbox"
                  checked={ids.includes(c.id)}
                  onChange={(e) => setIds((cur) => (e.target.checked ? [...cur, c.id] : cur.filter((x) => x !== c.id)))}
                />
                {c.name}
              </label>
            ))}
          </div>
        </div>
      </div>
      {error && <p className="al-error">{error}</p>}
    </Modal>
  );
}

export function BlockedModal({
  period,
  year,
  categories,
  onClose,
}: {
  period: BlockedPeriod | null;
  year: number;
  categories: LeaveCategory[];
  onClose: (saved: boolean) => void;
}) {
  const [name, setName] = useState(period?.name ?? "");
  const [startDate, setStartDate] = useState(period?.startDate ?? "");
  const [endDate, setEndDate] = useState(period?.endDate ?? "");
  const [categoryId, setCategoryId] = useState<number | "">(period?.categoryId ?? "");
  const [anyLeave, setAnyLeave] = useState(period ? period.maxDays == null : true);
  const [maxDays, setMaxDays] = useState(period?.maxDays != null ? String(period.maxDays) : "5");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const valid = name.trim() !== "" && startDate !== "" && endDate !== "" && endDate >= startDate && (anyLeave || Number(maxDays) >= 0);

  async function save() {
    if (!valid || busy) return;
    setBusy(true);
    setError(null);
    try {
      const input = {
        name: name.trim(),
        startDate,
        endDate,
        categoryId: categoryId === "" ? null : categoryId,
        maxDays: anyLeave ? null : Number(maxDays),
      };
      if (period) await updateBlockedPeriod(period.id, input);
      else await createBlockedPeriod(input);
      onClose(true);
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={period ? "Edit Period" : "Add Period"}
      width={480}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)} disabled={busy}>
            Cancel
          </button>
          <button className="btn-blue" onClick={save} disabled={!valid || busy}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div className="form-grid">
        <div className="form-field form-field-wide">
          <label className="form-label">Name</label>
          <input className="form-input" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Glee (NEC)" autoFocus />
        </div>
        <div className="form-field">
          <label className="form-label">From</label>
          <input className="form-input" type="date" value={startDate} min={`${year}-01-01`} onChange={(e) => setStartDate(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label">To</label>
          <input className="form-input" type="date" value={endDate} min={startDate || undefined} onChange={(e) => setEndDate(e.target.value)} />
        </div>
        <div className="form-field form-field-wide">
          <label className="form-label">Applies to</label>
          <select className="form-input" value={categoryId} onChange={(e) => setCategoryId(e.target.value ? Number(e.target.value) : "")}>
            <option value="">Everyone</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>
        <div className="form-field form-field-wide">
          <label className="form-label">Flag a request when</label>
          <div className="al-cat-ticks">
            <label className="al-check-label">
              <input type="radio" checked={anyLeave} onChange={() => setAnyLeave(true)} /> Any leave falls in it
            </label>
            <label className="al-check-label">
              <input type="radio" checked={!anyLeave} onChange={() => setAnyLeave(false)} /> It's more than
              <input
                className="form-input"
                style={{ width: 60 }}
                type="number"
                step="0.5"
                min="0"
                value={maxDays}
                disabled={anyLeave}
                onChange={(e) => setMaxDays(e.target.value)}
              />
              days
            </label>
          </div>
        </div>
      </div>
      {error && <p className="al-error">{error}</p>}
    </Modal>
  );
}
