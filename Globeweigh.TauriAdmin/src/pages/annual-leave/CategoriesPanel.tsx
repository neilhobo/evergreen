import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  LeaveCategory,
  createLeaveCategory,
  deleteLeaveCategory,
  fmtDays,
  getLeaveCategories,
  updateLeaveCategory,
} from "../../api/annualLeave";
import type { PanelProps } from "./AnnualLeavePage";

// Categories: the staff groups (Office, Factory / HGV, UK Sales) with their days a year, and —
// read off the year's company days — how that splits into set and floating days. Beside it, the
// rules every balance follows, in the client's own words, so the office can see why a number is
// what it is.
export default function CategoriesPanel({ year, version, onChanged, onMeta }: PanelProps) {
  const [rows, setRows] = useState<LeaveCategory[] | null>(null);
  const [editing, setEditing] = useState<LeaveCategory | "new" | null>(null);

  async function load() {
    try {
      setRows(await getLeaveCategories(year));
    } catch (err) {
      setRows((cur) => cur ?? []);
      void messageDialog("Failed to load the categories.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [year, version]);

  useEffect(() => {
    onMeta(rows ? `${rows.length} categories` : "");
  }, [rows, onMeta]);

  async function remove(c: LeaveCategory) {
    if (!(await confirmDialog(`Remove the ${c.name} category?`))) return;
    try {
      await deleteLeaveCategory(c.id);
      onChanged();
      void load();
    } catch (err) {
      void messageDialog((err as Error).message);
    }
  }

  return (
    <div className="al-panel">
      <div className="al-two-col">
        <div className="al-card" style={{ flex: 3, minWidth: 0 }}>
          <div className="al-card-head">
            <div>
              <div className="al-card-title">Staff categories</div>
              <div className="al-card-sub">Set days are the company days ticked for the category on Company Holidays ({year}).</div>
            </div>
            <button className="btn-blue" style={{ marginLeft: "auto" }} onClick={() => setEditing("new")}>
              + Add category
            </button>
          </div>
          <div className="al-card-body">
            <table className="al-grid">
              <thead>
                <tr>
                  <th>Category</th>
                  <th className="r">Days a year</th>
                  <th className="r">Set days {year}</th>
                  <th className="r">Floating days {year}</th>
                  <th className="r">Staff</th>
                  <th style={{ width: 110 }} />
                </tr>
              </thead>
              <tbody>
                {rows?.map((c) => (
                  <tr key={c.id}>
                    <td>{c.name}</td>
                    <td className="r">{fmtDays(c.annualDays)}</td>
                    <td className="r">{c.setDays}</td>
                    <td className="r">
                      <b>{fmtDays(c.annualDays - c.setDays)}</b>
                    </td>
                    <td className="r">{c.employees}</td>
                    <td className="r">
                      <button className="al-link" onClick={() => setEditing(c)}>
                        Edit
                      </button>
                      <button className="al-link danger" onClick={() => void remove(c)} disabled={c.employees > 0} title={c.employees > 0 ? "Move its staff to another category first" : undefined}>
                        Remove
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        <div className="al-card" style={{ flex: 2, minWidth: 0 }}>
          <div className="al-card-head">
            <div className="al-card-title">How a balance is worked out</div>
          </div>
          <div className="al-card-body al-rules">
            <h3>Total</h3>
            <p>
              The category's days a year (28) for anyone employed all year. A starter or leaver gets that share of the 28 for the part of
              the year they're employed, rounded up to the next half day.
            </p>
            <h3>Set days</h3>
            <p>The category's company days that fall on a working day while they're employed. A starter only counts the ones on or after their start date.</p>
            <h3>Floating days</h3>
            <p>
              Total − set days, plus any manual adjustment on their Staff record. A late starter can end up below zero: the Christmas
              shutdown alone can be more than they've earned.
            </p>
            <h3>Remaining</h3>
            <p>Floating days − approved leave. Waiting requests are shown, but only come off once approved.</p>
            <h3>Leave year</h3>
            <p>1 January – 31 December. Nothing carries forward: unused days are lost at the end of the year.</p>
          </div>
        </div>
      </div>

      {editing && (
        <CategoryModal
          category={editing === "new" ? null : editing}
          nextOrder={(rows?.length ?? 0) + 1}
          onClose={(saved) => {
            setEditing(null);
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

function CategoryModal({ category, nextOrder, onClose }: { category: LeaveCategory | null; nextOrder: number; onClose: (saved: boolean) => void }) {
  const [name, setName] = useState(category?.name ?? "");
  const [days, setDays] = useState(String(category?.annualDays ?? 28));
  const [order, setOrder] = useState(String(category?.sortOrder ?? nextOrder));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const valid = name.trim() !== "" && Number(days) > 0;

  async function save() {
    if (!valid || busy) return;
    setBusy(true);
    setError(null);
    try {
      const input = { name: name.trim(), annualDays: Number(days), sortOrder: Number(order) || 0 };
      if (category) await updateLeaveCategory(category.id, input);
      else await createLeaveCategory(input);
      onClose(true);
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={category ? "Edit Category" : "Add Category"}
      width={420}
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
          <input className="form-input" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        </div>
        <div className="form-field">
          <label className="form-label">Days a year (full year)</label>
          <input className="form-input" type="number" step="0.5" min="0" value={days} onChange={(e) => setDays(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label">Order in lists</label>
          <input className="form-input" type="number" value={order} onChange={(e) => setOrder(e.target.value)} />
        </div>
      </div>
      {!category && <p className="al-muted" style={{ fontSize: 12, margin: 0 }}>Then tick its company days on Company Holidays.</p>}
      {error && <p className="al-error">{error}</p>}
    </Modal>
  );
}
