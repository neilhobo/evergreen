import { apiGet, apiPost, apiPut, request } from "./client";

// Annual Leave (pages/annual-leave): the office side of the client's staff holiday entitlement.
// Mirrors Globeweigh.Api AnnualLeaveController; the sums behind every balance are
// Services/LeaveCalculator.cs. Dates are ISO "yyyy-MM-dd" strings throughout (DateOnly).

export const LeaveStatus = { Waiting: 0, Approved: 1, Declined: 2, Cancelled: 3 } as const;
export const STATUS_LABEL: Record<number, string> = { 0: "Waiting", 1: "Approved", 2: "Declined", 3: "Cancelled" };

export interface LeaveEmployee {
  id: number;
  /** The Admin > Staff user they are (GlobeweighUser id). */
  userId: number;
  forename: string;
  surname: string;
  categoryId: number;
  categoryName: string;
  startDate: string | null;
  endDate: string | null;
  email: string | null;
  /** Where approval emails go: email, else their login when that is an address. */
  emailTo: string | null;
  mobile: string | null;
  approverUserId: number | null;
  approverName: string | null;
  notes: string | null;
  // The year's balance (LeaveCalculator.BalanceFor).
  total: number;
  set: number;
  adjustments: number;
  floating: number;
  used: number;
  waiting: number;
  remaining: number;
  /** Past the paid allowance — unpaid (2027 policy). 0 when Remaining is not below 0. */
  unpaid: number;
  /** Floating days neither approved nor asked for (the 31 August check). */
  unbooked: number;
  /** Company days this year they are working instead (each one a floating day back). */
  worked: number;
}

/** One Admin > Staff user's leave profile. People are added and named in Admin > Staff;
 *  this is only what Annual Leave keeps on top. enrolled = false: not in Annual Leave. */
export interface LeaveProfile {
  enrolled: boolean;
  employeeId?: number | null;
  categoryId: number | null;
  startDate: string | null;
  endDate: string | null;
  email: string | null;
  mobile: string | null;
  approverUserId: number | null;
  notes: string | null;
  /** Read only: their login, when it is an email address. */
  loginEmail?: string | null;
}

export interface LeaveCheck {
  level: "ok" | "info" | "warn" | "error";
  text: string;
  /** Which policy: dates, overlap, employment, notice, allowance, peak, clash (the Requests columns). */
  key?: string | null;
  /** The cell text for that column, e.g. "9d", "6 left", "2 unpaid", "G. McCann". */
  short?: string | null;
}

export interface LeaveRequestRow {
  id: number;
  employeeId: number;
  employeeName: string;
  categoryName: string;
  startDate: string;
  endDate: string;
  dayLength: number;
  days: number;
  status: number;
  note: string | null;
  decisionNote: string | null;
  decidedByName: string | null;
  dateDecided: string | null;
  dateCreated: string;
  requestedByName: string | null;
  // Worked out for waiting requests only.
  remainingBefore: number;
  remainingAfter: number;
  checks: LeaveCheck[];
}

export interface RequestInput {
  /** Preview only: the request being edited, left out of its own checks. */
  requestId?: number;
  employeeId: number;
  startDate: string;
  endDate: string;
  dayLength: number;
  note?: string | null;
  approve?: boolean;
}

export interface RequestPreview {
  days: number;
  remainingBefore: number;
  remainingAfter: number;
  checks: LeaveCheck[];
}

export interface Holiday {
  id: number;
  date: string;
  name: string;
  categoryIds: number[];
}

export interface HolidayInput {
  date: string;
  name: string;
  categoryIds: number[];
}

export interface BlockedPeriod {
  id: number;
  name: string;
  startDate: string;
  endDate: string;
  categoryId: number | null;
  maxDays: number | null;
}

export type BlockedInput = Omit<BlockedPeriod, "id">;

export interface LeaveCategory {
  id: number;
  name: string;
  annualDays: number;
  sortOrder: number;
  employees: number;
  setDays: number;
}

export interface CategoryInput {
  name: string;
  annualDays: number;
  sortOrder: number;
}

export interface CalendarLeave {
  id: number;
  employeeId: number;
  startDate: string;
  endDate: string;
  dayLength: number;
  days: number;
  status: number;
}

export interface Adjustment {
  id: number;
  days: number;
  reason: string;
  createdByName: string | null;
  dateCreated: string;
}

const base = "/annual-leave";

export const getAnnualLeaveAccessMe = () => apiGet<{ canView: boolean }>(`${base}/access/me`);

export const getLeaveEmployees = (year: number) => apiGet<LeaveEmployee[]>(`${base}/employees?year=${year}`);
/** Who is in Annual Leave, by Staff user — Admin > Staff's column. */
export const getStaffLeave = () =>
  apiGet<{ userId: number; employeeId: number; categoryName: string }[]>(`${base}/staff`);
export const getLeaveProfile = (userId: number) => apiGet<LeaveProfile>(`${base}/staff/${userId}`);
export const saveLeaveProfile = (userId: number, profile: LeaveProfile) =>
  apiPut<unknown>(`${base}/staff/${userId}`, profile);
export const getLeaveApprovers = () => apiGet<{ id: number; name: string }[]>(`${base}/approvers`);

export const getAdjustments = (employeeId: number, year: number) =>
  apiGet<Adjustment[]>(`${base}/employees/${employeeId}/adjustments?year=${year}`);
export const addAdjustment = (employeeId: number, year: number, days: number, reason: string) =>
  apiPost<void>(`${base}/employees/${employeeId}/adjustments`, { year, days, reason });
export const deleteAdjustment = (id: number) => request<void>("DELETE", `${base}/adjustments/${id}`);

/** A company day they work instead, getting a floating day for it (2027 policy). */
export interface WorkedDay {
  id: number;
  holidayId: number;
  date: string;
  holidayName: string;
  reason: string;
  createdByName: string | null;
  dateCreated: string;
}
export const getWorkedDays = (employeeId: number, year: number) =>
  apiGet<WorkedDay[]>(`${base}/employees/${employeeId}/worked?year=${year}`);
export const addWorkedDay = (employeeId: number, holidayId: number, reason: string) =>
  apiPost<void>(`${base}/employees/${employeeId}/worked`, { holidayId, reason });
export const deleteWorkedDay = (id: number) => request<void>("DELETE", `${base}/worked/${id}`);

/** Someone with floating days still to book (Outstanding tab). */
export interface OutstandingRow {
  employeeId: number;
  name: string;
  categoryName: string;
  floating: number;
  used: number;
  waiting: number;
  unbooked: number;
  emailTo: string | null;
  lastReminded: string | null;
  lastRemindedDays: number | null;
}
export const getOutstanding = (year: number) => apiGet<OutstandingRow[]>(`${base}/outstanding?year=${year}`);
export const sendLeaveReminders = (year: number, employeeIds: number[]) =>
  apiPost<{ sent: number; skipped: string[]; failed: string[] }>(`${base}/reminders`, { year, employeeIds });

export const getLeaveRequests = (year: number) => apiGet<LeaveRequestRow[]>(`${base}/requests?year=${year}`);
export const previewLeaveRequest = (input: RequestInput) => apiPost<RequestPreview>(`${base}/requests/preview`, input);
export const createLeaveRequest = (input: RequestInput) => apiPost<{ id: number; emailed: boolean }>(`${base}/requests`, input);
export const decideLeaveRequest = (id: number, approve: boolean, note: string | null, email: boolean) =>
  apiPost<{ emailed: boolean }>(`${base}/requests/${id}/decide`, { approve, note, email });
export const cancelLeaveRequest = (id: number) => apiPost<void>(`${base}/requests/${id}/cancel`, {});
/** One request with its balance effect and checks (the calendar popup). */
export const getLeaveRequest = (id: number) => apiGet<LeaveRequestRow>(`${base}/requests/${id}`);
/** Change a waiting / approved request's dates, length or note; it keeps its status. */
export const updateLeaveRequest = (id: number, input: RequestInput) => apiPut<void>(`${base}/requests/${id}`, input);
/** Remove a request entered by mistake (real leave no longer taken should be cancelled). */
export const deleteLeaveRequest = (id: number) => request<void>("DELETE", `${base}/requests/${id}`);

export const getLeaveCalendar = (year: number) =>
  apiGet<{ holidays: Holiday[]; leave: CalendarLeave[]; worked: { employeeId: number; date: string }[] }>(`${base}/calendar?year=${year}`);

export const getHolidays = (year: number) => apiGet<Holiday[]>(`${base}/holidays?year=${year}`);
export const createHoliday = (input: HolidayInput) => apiPost<{ id: number }>(`${base}/holidays`, input);
export const updateHoliday = (id: number, input: HolidayInput) => apiPut<void>(`${base}/holidays/${id}`, input);
export const deleteHoliday = (id: number) => request<void>("DELETE", `${base}/holidays/${id}`);
export const copyHolidays = (fromYear: number, toYear: number) => apiPost<void>(`${base}/holidays/copy`, { fromYear, toYear });

export const getBlockedPeriods = (year: number) => apiGet<BlockedPeriod[]>(`${base}/blocked?year=${year}`);
export const createBlockedPeriod = (input: BlockedInput) => apiPost<{ id: number }>(`${base}/blocked`, input);
export const updateBlockedPeriod = (id: number, input: BlockedInput) => apiPut<void>(`${base}/blocked/${id}`, input);
export const deleteBlockedPeriod = (id: number) => request<void>("DELETE", `${base}/blocked/${id}`);

export const getLeaveCategories = (year: number) => apiGet<LeaveCategory[]>(`${base}/categories?year=${year}`);
export const createLeaveCategory = (input: CategoryInput) => apiPost<{ id: number }>(`${base}/categories`, input);
export const updateLeaveCategory = (id: number, input: CategoryInput) => apiPut<void>(`${base}/categories/${id}`, input);
export const deleteLeaveCategory = (id: number) => request<void>("DELETE", `${base}/categories/${id}`);

// ---- display helpers ----------------------------------------------------------------------

/** 1, 0.5, 0.25, -3.5 → "1", "0.5", "0.25", "−3.5" (a real minus sign, so negatives read). */
export function fmtDays(n: number): string {
  const s = String(Math.round(n * 100) / 100);
  return n < 0 ? "−" + s.slice(1) : s;
}

/** A balance in words: a minus balance is unpaid days (2027 policy) — "3", "2 unpaid". */
export function fmtLeft(n: number): string {
  return n < 0 ? `${fmtDays(-n)} unpaid` : fmtDays(n);
}

const DOW = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const MON = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** "2026-12-04" → a local Date at midnight (no UTC shift). */
export function parseIso(iso: string): Date {
  const [y, m, d] = iso.slice(0, 10).split("-").map(Number);
  return new Date(y, m - 1, d);
}

export function toIso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

/** "Fri 4 Dec" */
export function fmtDay(iso: string): string {
  const d = parseIso(iso);
  return `${DOW[d.getDay()]} ${d.getDate()} ${MON[d.getMonth()]}`;
}

/** "04/12/2026" for grids. */
export function fmtDate(iso: string | null): string {
  if (!iso) return "";
  const d = parseIso(iso);
  return `${String(d.getDate()).padStart(2, "0")}/${String(d.getMonth() + 1).padStart(2, "0")}/${d.getFullYear()}`;
}

/** A request's dates as a phrase: "Fri 4 Dec", "Fri 4 Dec (half day)", "Wed 16 – Fri 18 Dec". */
export function fmtRange(start: string, end: string, dayLength = 1): string {
  if (start === end) {
    const part = dayLength === 0.5 ? " (half day)" : dayLength === 0.25 ? " (quarter day)" : "";
    return fmtDay(start) + part;
  }
  const a = parseIso(start);
  const b = parseIso(end);
  const left = a.getMonth() === b.getMonth() ? `${DOW[a.getDay()]} ${a.getDate()}` : fmtDay(start);
  return `${left} – ${fmtDay(end)}`;
}
