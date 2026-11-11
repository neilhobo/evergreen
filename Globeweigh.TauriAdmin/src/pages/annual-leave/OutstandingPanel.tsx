import { useEffect, useMemo, useState } from "react";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import { OutstandingRow, fmtDate, fmtDays, getOutstanding, sendLeaveReminders } from "../../api/annualLeave";
import type { PanelProps } from "./AnnualLeavePage";

// Outstanding: the 2027 policy's "Where leave remains unallocated on 31 August, employees must
// confirm by 15 September when they intend to take it. Otherwise the company may allocate
// outstanding leave with one month's notice." Everyone with floating days neither approved nor
// asked for, most first, and an email to each chosen one with their days and the deadline. Each
// email sent is logged (LeaveReminder), so the list shows who has already been asked.
export default function OutstandingPanel({ year, version, onMeta }: PanelProps) {
  const [rows, setRows] = useState<OutstandingRow[] | null>(null);
  const [picked, setPicked] = useState<Set<number>>(new Set());
  const [busy, setBusy] = useState(false);

  async function load() {
    try {
      const data = await getOutstanding(year);
      setRows(data);
      setPicked((cur) => new Set([...cur].filter((id) => data.some((r) => r.employeeId === id))));
    } catch (err) {
      setRows((cur) => cur ?? []);
      void messageDialog("Failed to load outstanding leave.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [year, version]);

  const list = rows ?? [];
  const total = list.reduce((n, r) => n + r.unbooked, 0);
  const emailable = list.filter((r) => r.emailTo);
  useEffect(() => {
    onMeta(rows ? `${list.length} staff · ${fmtDays(total)} days unbooked` : "");
  }, [rows, list.length, total, onMeta]);

  const allPicked = emailable.length > 0 && emailable.every((r) => picked.has(r.employeeId));
  const pickedRows = useMemo(() => list.filter((r) => picked.has(r.employeeId)), [list, picked]);

  function toggle(id: number) {
    setPicked((cur) => {
      const next = new Set(cur);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  async function send() {
    if (pickedRows.length === 0 || busy) return;
    const n = pickedRows.length;
    if (
      !(await confirmDialog(
        `Email ${n} ${n === 1 ? "person" : "people"} their unbooked ${year} leave, asking them to confirm by 15 September ${year} when they will take it?`,
      ))
    )
      return;
    setBusy(true);
    try {
      const r = await sendLeaveReminders(year, pickedRows.map((x) => x.employeeId));
      const lines = [`${r.sent} reminder${r.sent === 1 ? "" : "s"} sent.`];
      if (r.skipped.length) lines.push(`No email address: ${r.skipped.join(", ")}.`);
      if (r.failed.length) lines.push(`Could not be sent (check Super Admin > Email Setup): ${r.failed.join(", ")}.`);
      void messageDialog(lines.join("\n"));
      setPicked(new Set());
      await load();
    } catch (err) {
      void messageDialog("Failed to send.\n" + (err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const today = new Date();
  const checkDay = new Date(year, 7, 31);
  const beforeCheck = today < checkDay;

  return (
    <div className="al-panel po-main">
      <div className="toolbar">
        <span className="toolbar-note">
          {beforeCheck
            ? `From 31 Aug ${year}, staff with unbooked days must confirm by 15 Sep when they will take them, or the company may allocate them with a month's notice.`
            : `Unbooked on 31 Aug ${year}: staff confirm by 15 Sep when they will take it, or the company may allocate it with a month's notice.`}
        </span>
        <div className="toolbar-right">
          <button className="btn-blue" onClick={() => void send()} disabled={pickedRows.length === 0 || busy}>
            {busy ? "Sending…" : `Email reminder${pickedRows.length ? ` (${pickedRows.length})` : ""}`}
          </button>
        </div>
      </div>
      <div className="al-out-wrap">
        <table className="al-grid">
          <thead>
            <tr>
              <th style={{ width: 34 }}>
                <input
                  type="checkbox"
                  aria-label="Choose everyone with an email address"
                  checked={allPicked}
                  disabled={emailable.length === 0}
                  onChange={() => setPicked(allPicked ? new Set() : new Set(emailable.map((r) => r.employeeId)))}
                />
              </th>
              <th>Name</th>
              <th style={{ width: 130 }}>Category</th>
              <th className="r" style={{ width: 75 }}>Floating</th>
              <th className="r" style={{ width: 75 }}>Approved</th>
              <th className="r" style={{ width: 75 }}>Waiting</th>
              <th className="r" style={{ width: 85 }}>Unbooked</th>
              <th style={{ width: 230 }}>Email</th>
              <th style={{ width: 150 }}>Last reminded</th>
            </tr>
          </thead>
          <tbody>
            {rows === null && (
              <tr>
                <td colSpan={9} className="al-muted">
                  Loading…
                </td>
              </tr>
            )}
            {rows !== null && list.length === 0 && (
              <tr>
                <td colSpan={9} className="al-muted">
                  Everyone has booked all their {year} floating days.
                </td>
              </tr>
            )}
            {list.map((r) => (
              <tr key={r.employeeId} onClick={() => r.emailTo && toggle(r.employeeId)} style={{ cursor: r.emailTo ? "pointer" : undefined }}>
                <td>
                  <input
                    type="checkbox"
                    aria-label={`Choose ${r.name}`}
                    checked={picked.has(r.employeeId)}
                    disabled={!r.emailTo}
                    onClick={(e) => e.stopPropagation()}
                    onChange={() => toggle(r.employeeId)}
                  />
                </td>
                <td>{r.name}</td>
                <td>{r.categoryName}</td>
                <td className="r">{fmtDays(r.floating)}</td>
                <td className="r">{fmtDays(r.used)}</td>
                <td className="r">{r.waiting ? fmtDays(r.waiting) : ""}</td>
                <td className="r">
                  <b>{fmtDays(r.unbooked)}</b>
                </td>
                <td className={r.emailTo ? undefined : "al-muted"}>{r.emailTo ?? "No email: set one in Admin › Staff"}</td>
                <td>
                  {r.lastReminded
                    ? `${fmtDate(r.lastReminded)} (${fmtDays(r.lastRemindedDays ?? 0)} day${r.lastRemindedDays === 1 ? "" : "s"})`
                    : ""}
                </td>
              </tr>
            ))}
          </tbody>
          {list.length > 0 && (
            <tfoot>
              <tr>
                <td />
                <td>{list.length} staff</td>
                <td colSpan={4} />
                <td className="r">
                  <b>{fmtDays(total)}</b>
                </td>
                <td colSpan={2} />
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    </div>
  );
}
