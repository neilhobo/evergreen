import { useEffect, useState } from "react";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  BlockedPeriod,
  Holiday,
  LeaveCategory,
  copyHolidays,
  deleteBlockedPeriod,
  deleteHoliday,
  fmtDate,
  fmtDays,
  getBlockedPeriods,
  getHolidays,
  getLeaveCategories,
  parseIso,
  toIso,
  updateHoliday,
} from "../../api/annualLeave";
import type { PanelProps } from "./AnnualLeavePage";
import { BlockedModal, HolidayModal } from "./HolidayModals";

const DAY = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

// Company Holidays: the year's company days laid out like the client's entitlement sheet — a row
// per day, a tick per category — so setting up a year is ticking the same grid they already
// print. Each category's set days are counted along the bottom. Beside it, the periods where
// leave is discouraged (peak season, the Sales trade shows), which the request checks flag.
export default function HolidaysPanel({ year, version, onChanged, onMeta }: PanelProps) {
  const [holidays, setHolidays] = useState<Holiday[] | null>(null);
  const [categories, setCategories] = useState<LeaveCategory[]>([]);
  const [blocked, setBlocked] = useState<BlockedPeriod[]>([]);
  const [editing, setEditing] = useState<Holiday | "new" | null>(null);
  const [editingBlocked, setEditingBlocked] = useState<BlockedPeriod | "new" | null>(null);
  const [busy, setBusy] = useState(false);

  async function load() {
    try {
      const [h, c, b] = await Promise.all([getHolidays(year), getLeaveCategories(year), getBlockedPeriods(year)]);
      setHolidays(h);
      setCategories(c);
      setBlocked(b);
    } catch (err) {
      setHolidays((cur) => cur ?? []);
      void messageDialog("Failed to load the company holidays.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [year, version]);

  useEffect(() => {
    onMeta(holidays ? `${holidays.length} company days` : "");
  }, [holidays, onMeta]);

  const reloadAll = () => {
    onChanged();
    void load();
  };

  async function toggle(h: Holiday, categoryId: number) {
    const ids = h.categoryIds.includes(categoryId) ? h.categoryIds.filter((c) => c !== categoryId) : [...h.categoryIds, categoryId];
    // Show it straight away; put it back if the save fails.
    setHolidays((list) => list?.map((x) => (x.id === h.id ? { ...x, categoryIds: ids } : x)) ?? list);
    try {
      await updateHoliday(h.id, { date: h.date, name: h.name, categoryIds: ids });
      onChanged();
    } catch (err) {
      void messageDialog((err as Error).message);
      void load();
    }
  }

  async function remove(h: Holiday) {
    if (!(await confirmDialog(`Remove ${h.name} (${fmtDate(h.date)}) as a company day? Everyone's set days and balances change with it.`))) return;
    try {
      await deleteHoliday(h.id);
      reloadAll();
    } catch (err) {
      void messageDialog((err as Error).message);
    }
  }

  async function copyLastYear() {
    if (busy) return;
    if (!(await confirmDialog(`Copy ${year - 1}'s company days into ${year}? Each one is carried to the same date — then check the ones that move every year (Easter, bank-holiday Mondays, the Christmas shutdown).`)))
      return;
    setBusy(true);
    try {
      await copyHolidays(year - 1, year);
      reloadAll();
    } catch (err) {
      void messageDialog((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function removeBlocked(p: BlockedPeriod) {
    if (!(await confirmDialog(`Remove "${p.name}"?`))) return;
    try {
      await deleteBlockedPeriod(p.id);
      reloadAll();
    } catch (err) {
      void messageDialog((err as Error).message);
    }
  }

  const today = toIso(new Date());
  const setCount = (catId: number) =>
    (holidays ?? []).filter((h) => h.categoryIds.includes(catId) && ![0, 6].includes(parseIso(h.date).getDay())).length;
  const catName = (id: number | null) => (id == null ? "Everyone" : (categories.find((c) => c.id === id)?.name ?? "?"));

  return (
    <div className="al-panel">
      <div className="al-two-col">
        <div className="al-card" style={{ flex: 3, minWidth: 0 }}>
          <div className="al-card-head">
            <div>
              <div className="al-card-title">Company days {year}</div>
              <div className="al-card-sub">Tick the categories that get each day off. These count as the "set" part of everyone's 28.</div>
            </div>
            <div style={{ marginLeft: "auto", display: "flex", gap: 8 }}>
              {holidays && holidays.length === 0 && (
                <button className="toolbar-btn" onClick={() => void copyLastYear()} disabled={busy}>
                  Copy from {year - 1}
                </button>
              )}
              <button className="btn-blue" onClick={() => setEditing("new")}>
                + Add day
              </button>
            </div>
          </div>
          <div className="al-card-body">
            <table className="al-grid">
              <thead>
                <tr>
                  <th style={{ width: 100 }}>Date</th>
                  <th style={{ width: 100 }}>Day</th>
                  <th>Calendar event</th>
                  {categories.map((c) => (
                    <th key={c.id} className="c" style={{ width: 110 }}>
                      {c.name}
                    </th>
                  ))}
                  <th style={{ width: 110 }} />
                </tr>
              </thead>
              <tbody>
                {holidays?.length === 0 && (
                  <tr>
                    <td colSpan={4 + categories.length} className="al-muted" style={{ padding: 16 }}>
                      No company days for {year} yet — add them, or copy last year's and adjust.
                    </td>
                  </tr>
                )}
                {holidays?.map((h) => {
                  const dow = parseIso(h.date).getDay();
                  const weekend = dow === 0 || dow === 6;
                  return (
                    <tr key={h.id} className={h.date < today ? "al-past" : undefined}>
                      <td>{fmtDate(h.date)}</td>
                      <td>
                        {DAY[dow]}
                        {weekend && <span className="al-flag" title="A weekend day doesn't count as a set day — move it to the working day off">weekend</span>}
                      </td>
                      <td>{h.name}</td>
                      {categories.map((c) => (
                        <td key={c.id} className="c">
                          <input
                            type="checkbox"
                            className="al-tick"
                            checked={h.categoryIds.includes(c.id)}
                            onChange={() => void toggle(h, c.id)}
                            aria-label={`${c.name} gets ${h.name} ${fmtDate(h.date)}`}
                          />
                        </td>
                      ))}
                      <td className="r">
                        <button className="al-link" onClick={() => setEditing(h)}>
                          Edit
                        </button>
                        <button className="al-link danger" onClick={() => void remove(h)}>
                          Remove
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
              {holidays && holidays.length > 0 && (
                <tfoot>
                  <tr>
                    <td colSpan={3}>Set days (working days ticked) → floating days</td>
                    {categories.map((c) => (
                      <td key={c.id} className="c">
                        {setCount(c.id)} → {fmtDays(c.annualDays - setCount(c.id))}
                      </td>
                    ))}
                    <td />
                  </tr>
                </tfoot>
              )}
            </table>
          </div>
        </div>

        <div className="al-card" style={{ flex: 2, minWidth: 0 }}>
          <div className="al-card-head">
            <div>
              <div className="al-card-title">Leave discouraged</div>
              <div className="al-card-sub">Requests in these dates are flagged to the approver. Nothing is blocked outright.</div>
            </div>
            <button className="btn-blue" style={{ marginLeft: "auto" }} onClick={() => setEditingBlocked("new")}>
              + Add period
            </button>
          </div>
          <div className="al-card-body">
            <table className="al-grid">
              <thead>
                <tr>
                  <th>Period</th>
                  <th>Dates</th>
                  <th>Applies to</th>
                  <th>Flag when</th>
                  <th style={{ width: 100 }} />
                </tr>
              </thead>
              <tbody>
                {blocked.length === 0 && (
                  <tr>
                    <td colSpan={5} className="al-muted" style={{ padding: 16 }}>
                      None for {year}. Add peak season, and Four Oaks / Glee for UK Sales.
                    </td>
                  </tr>
                )}
                {blocked.map((p) => (
                  <tr key={p.id}>
                    <td>{p.name}</td>
                    <td>
                      {fmtDate(p.startDate)} – {fmtDate(p.endDate)}
                    </td>
                    <td>{catName(p.categoryId)}</td>
                    <td>{p.maxDays == null ? "Any leave" : `More than ${fmtDays(p.maxDays)} days`}</td>
                    <td className="r">
                      <button className="al-link" onClick={() => setEditingBlocked(p)}>
                        Edit
                      </button>
                      <button className="al-link danger" onClick={() => void removeBlocked(p)}>
                        Remove
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="al-card-foot">
            From the policy: no extended leave March–June; Sales and stand staff available for Four Oaks and Glee in September.
          </div>
        </div>
      </div>

      {editing && (
        <HolidayModal
          holiday={editing === "new" ? null : editing}
          year={year}
          categories={categories}
          onClose={(saved) => {
            setEditing(null);
            if (saved) reloadAll();
          }}
        />
      )}
      {editingBlocked && (
        <BlockedModal
          period={editingBlocked === "new" ? null : editingBlocked}
          year={year}
          categories={categories}
          onClose={(saved) => {
            setEditingBlocked(null);
            if (saved) reloadAll();
          }}
        />
      )}
    </div>
  );
}
