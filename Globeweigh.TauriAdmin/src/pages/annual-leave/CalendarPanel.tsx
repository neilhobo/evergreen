import { Fragment, useEffect, useMemo, useState } from "react";
import { messageDialog } from "../../components/confirmDialog";
import {
  CalendarLeave,
  Holiday,
  LeaveEmployee,
  LeaveStatus,
  getLeaveCalendar,
  getLeaveEmployees,
  parseIso,
  toIso,
} from "../../api/annualLeave";
import type { PanelProps } from "./AnnualLeavePage";
import AddLeaveModal from "./AddLeaveModal";

const MONTHS = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
const DOW1 = ["S", "M", "T", "W", "T", "F", "S"];

interface Day {
  iso: string;
  date: Date;
  weekend: boolean;
}

interface Booked {
  status: number;
  dayLength: number;
}

// Calendar: the wall planner. A row per employee, a column per day — company days in green,
// approved leave in blue, waiting leave hatched, weekends grey — and a count along the bottom of
// how many are off each day, so a clash shows before it is approved. Month view to work in; Whole
// year to see the shape of it. Click an empty day to book leave for that person on that day.
export default function CalendarPanel({ year, version, onChanged, onMeta }: PanelProps) {
  const now = new Date();
  const [month, setMonth] = useState(year === now.getFullYear() ? now.getMonth() : 0);
  const [mode, setMode] = useState<"month" | "year">("month");
  const [categoryId, setCategoryId] = useState<number | "">("");
  const [employees, setEmployees] = useState<LeaveEmployee[]>([]);
  const [holidays, setHolidays] = useState<Holiday[]>([]);
  const [leave, setLeave] = useState<CalendarLeave[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [adding, setAdding] = useState<{ employeeId: number; date: string } | null>(null);

  async function load() {
    try {
      const [e, c] = await Promise.all([getLeaveEmployees(year), getLeaveCalendar(year)]);
      setEmployees(e);
      setHolidays(c.holidays);
      setLeave(c.leave);
    } catch (err) {
      void messageDialog("Failed to load the calendar.\n" + (err as Error).message);
    } finally {
      setLoaded(true);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [year, version]);

  const categories = useMemo(() => {
    const m = new Map<number, string>();
    employees.forEach((e) => m.set(e.categoryId, e.categoryName));
    return [...m.entries()].sort((a, b) => a[1].localeCompare(b[1]));
  }, [employees]);

  const rows = categoryId === "" ? employees : employees.filter((e) => e.categoryId === categoryId);

  useEffect(() => {
    onMeta(loaded ? `${rows.length} staff` : "");
  }, [loaded, rows.length, onMeta]);

  // Company days: per category, and their names for the tooltip.
  const { companyByCat, holidayName } = useMemo(() => {
    const byCat = new Map<number, Set<string>>();
    const names = new Map<string, string>();
    for (const h of holidays) {
      names.set(h.date, h.name);
      for (const c of h.categoryIds) {
        if (!byCat.has(c)) byCat.set(c, new Set());
        byCat.get(c)!.add(h.date);
      }
    }
    return { companyByCat: byCat, holidayName: names };
  }, [holidays]);

  // Every day each employee has leave on (ranges expanded).
  const booked = useMemo(() => {
    const m = new Map<number, Map<string, Booked>>();
    for (const l of leave) {
      if (!m.has(l.employeeId)) m.set(l.employeeId, new Map());
      const days = m.get(l.employeeId)!;
      for (let d = parseIso(l.startDate); toIso(d) <= l.endDate; d.setDate(d.getDate() + 1))
        days.set(toIso(d), { status: l.status, dayLength: l.dayLength });
    }
    return m;
  }, [leave]);

  const days: Day[] = useMemo(() => {
    const list: Day[] = [];
    const from = mode === "month" ? new Date(year, month, 1) : new Date(year, 0, 1);
    const to = mode === "month" ? new Date(year, month + 1, 0) : new Date(year, 11, 31);
    for (let d = new Date(from); d <= to; d.setDate(d.getDate() + 1)) {
      const date = new Date(d);
      list.push({ iso: toIso(date), date, weekend: date.getDay() === 0 || date.getDay() === 6 });
    }
    return list;
  }, [mode, year, month]);

  const todayIso = toIso(now);

  type Kind = "weekend" | "company" | "leave" | "waiting" | "none";
  function classify(e: LeaveEmployee, day: Day): { kind: Kind; part: number; title: string } {
    if (day.weekend) return { kind: "weekend", part: 1, title: "" };
    if (companyByCat.get(e.categoryId)?.has(day.iso)) return { kind: "company", part: 1, title: holidayName.get(day.iso) ?? "Company day" };
    const b = booked.get(e.id)?.get(day.iso);
    if (b) {
      const waiting = b.status === LeaveStatus.Waiting;
      const part = b.dayLength;
      return {
        kind: waiting ? "waiting" : "leave",
        part,
        title: `${e.forename} ${e.surname}: ${waiting ? "waiting" : "approved"}${part < 1 ? (part === 0.5 ? " (half day)" : " (quarter day)") : ""}`,
      };
    }
    return { kind: "none", part: 1, title: `Book leave for ${e.forename} ${e.surname} on ${day.date.toDateString()}` };
  }

  // How many of the rows on show are off each day (approved or waiting).
  const counts = days.map((day) =>
    day.weekend ? 0 : rows.reduce((n, e) => (booked.get(e.id)?.has(day.iso) && !companyByCat.get(e.categoryId)?.has(day.iso) ? n + 1 : n), 0),
  );

  function cellProps(e: LeaveEmployee, day: Day, monthStart: boolean) {
    const c = classify(e, day);
    const sep = monthStart ? " al-cal-month-sep" : "";
    if ((c.kind === "leave" || c.kind === "waiting") && c.part < 1)
      return {
        className: `al-cell al-cell-part${sep}`,
        style: {
          ["--al-part" as string]: `${c.part * 100}%`,
          ["--al-part-colour" as string]: c.kind === "leave" ? "#2563eb" : "#93c5fd",
        },
        title: c.title,
        kind: c.kind,
      };
    return { className: `al-cell al-cell-${c.kind}${sep}`, style: undefined, title: c.title, kind: c.kind };
  }

  const isYear = mode === "year";
  // Whole year: month names across the top instead of every day.
  const monthSpans = isYear
    ? MONTHS.map((m, i) => ({ label: m.slice(0, 3), span: new Date(year, i + 1, 0).getDate() }))
    : [];

  return (
    <div className="al-panel po-main">
      <div className="toolbar">
        {!isYear && (
          <>
            <button className="toolbar-btn toolbar-btn-icon" aria-label="Previous month" onClick={() => setMonth((m) => Math.max(0, m - 1))} disabled={month === 0}>
              ‹
            </button>
            <span className="al-month-title">
              {MONTHS[month]} {year}
            </span>
            <button className="toolbar-btn toolbar-btn-icon" aria-label="Next month" onClick={() => setMonth((m) => Math.min(11, m + 1))} disabled={month === 11}>
              ›
            </button>
          </>
        )}
        <button className={`al-seg${!isYear ? " on" : ""}`} onClick={() => setMode("month")}>
          Month
        </button>
        <button className={`al-seg${isYear ? " on" : ""}`} onClick={() => setMode("year")}>
          Whole year
        </button>
        <select className="form-input" style={{ width: 170 }} value={categoryId} onChange={(e) => setCategoryId(e.target.value ? Number(e.target.value) : "")}>
          <option value="">All categories</option>
          {categories.map(([id, name]) => (
            <option key={id} value={id}>
              {name}
            </option>
          ))}
        </select>
        <div className="toolbar-right al-legend">
          <span>
            <i className="al-sw al-sw-company" />
            Company day
          </span>
          <span>
            <i className="al-sw al-sw-leave" />
            Approved
          </span>
          <span>
            <i className="al-sw al-sw-waiting" />
            Waiting
          </span>
          <span>
            <i className="al-sw al-sw-weekend" />
            Weekend
          </span>
        </div>
      </div>

      <div className="al-cal-wrap">
        <table className={`al-cal${isYear ? " al-cal-year" : ""}`}>
          <colgroup>
            <col style={{ width: 190 }} />
            {days.map((d) => (
              <col key={d.iso} />
            ))}
          </colgroup>
          <thead>
            <tr>
              <th className="al-cal-name">Staff</th>
              {isYear
                ? monthSpans.map((m, i) => (
                    <th key={m.label} colSpan={m.span} className={i > 0 ? "al-cal-month-sep" : undefined}>
                      {m.label}
                    </th>
                  ))
                : days.map((d) => (
                    <th key={d.iso} className={d.iso === todayIso ? "al-cal-today" : undefined} title={holidayName.get(d.iso)}>
                      {DOW1[d.date.getDay()]}
                      <br />
                      {d.date.getDate()}
                    </th>
                  ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((e) => (
              <tr key={e.id}>
                <td className="al-cal-name" title={`${e.forename} ${e.surname} — ${e.categoryName}`}>
                  {e.surname}, {e.forename}
                  <small>{e.categoryName}</small>
                </td>
                {days.map((d) => {
                  const p = cellProps(e, d, isYear && d.date.getDate() === 1 && d.date.getMonth() > 0);
                  return (
                    <td
                      key={d.iso}
                      className={p.className}
                      style={p.style}
                      title={p.title}
                      onClick={() => p.kind === "none" && setAdding({ employeeId: e.id, date: d.iso })}
                    />
                  );
                })}
              </tr>
            ))}
            {rows.length === 0 && loaded && (
              <tr>
                <td className="al-cal-name al-muted" colSpan={days.length + 1}>
                  No staff in this category.
                </td>
              </tr>
            )}
            <tr className="al-cal-count">
              <td className="al-cal-name">Off</td>
              {days.map((d, i) => (
                <Fragment key={d.iso}>
                  <td
                    className={[counts[i] >= 2 ? "busy" : "", isYear && d.date.getDate() === 1 && d.date.getMonth() > 0 ? "al-cal-month-sep" : ""].filter(Boolean).join(" ") || undefined}
                    title={counts[i] ? `${counts[i]} off on ${d.date.toDateString()}` : undefined}
                  >
                    {!isYear && counts[i] ? counts[i] : ""}
                  </td>
                </Fragment>
              ))}
            </tr>
          </tbody>
        </table>
      </div>

      {adding && (
        <AddLeaveModal
          employees={employees}
          employeeId={adding.employeeId}
          date={adding.date}
          onClose={(saved) => {
            setAdding(null);
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
