import { apiGet, apiPost, apiPut, request } from "./client";
import { SageSyncState } from "./sage";

// Mirrors Globeweigh.Api Controllers/ReportsController.cs + Services/MemberSalesReport.cs
// (camelCase JSON). Sage Financial Reports are saved reports (SageReport), each a member-sales
// report adding up its own named groups of Sage accounts from the Sage sales mirror the
// sidecar keeps; "AlliedMerchant" is the Allied Merchant Buying Group.

export interface SageReport {
  key: string;
  name: string;
  memberCount: number;
  accountCount: number;
}

// A report as the editor saves it: the name and the whole member list (ids kept, 0 = new,
// missing = deleted; order = the order on the report).
export interface SageReportInput {
  name: string;
  groups: { id: number; name: string; accountRefs: string }[];
}

export interface MemberSalesAccount {
  accountRef: string;
  name: string;
  turnover: number;
  invoices: number;
  credits: number;
}

export interface MemberSalesMember {
  name: string;
  accounts: MemberSalesAccount[];
  turnover: number;
  invoices: number;
  credits: number;
  share: number; // 0..1 of the grand total
}

export interface MemberSalesTotals {
  turnover: number;
  invoices: number;
  credits: number;
}

export interface MemberSalesMonth {
  month: string; // yyyy-MM
  label: string; // "Jan 2026"
  byMember: Record<string, number>;
  total: number;
}

export interface MemberSalesProduct {
  stockCode: string;
  description: string;
  value: number;
  qty: number;
  byMember: Record<string, { value: number; qty: number }>;
}

// An audit-trail entry with no invoice record left behind it (deleted after posting): in the
// turnover, not in the product split.
export interface MemberSalesUnsplit {
  date: string;
  accountRef: string;
  member: string;
  type: string; // Invoice | Credit
  invRef: string | null;
  details: string | null;
  net: number;
}

export interface MemberSalesTransaction {
  date: string;
  month: string;
  accountRef: string;
  member: string;
  type: string;
  invRef: string | null;
  details: string | null;
  net: number; // signed: credits negative
  vat: number;
}

export interface MemberSalesInvoice {
  date: string;
  number: number;
  type: string;
  accountRef: string;
  member: string;
  name: string | null;
  custOrderNumber: string | null;
  net: number;
  vat: number;
  carriage: number;
  posted: boolean;
}

export interface MemberSalesLine {
  date: string;
  number: number;
  type: string;
  accountRef: string;
  member: string;
  stockCode: string;
  description: string | null;
  qty: number; // signed
  unitPrice: number;
  net: number; // signed
}

export interface MemberSalesSync {
  lastRunUtc: string | null;
  lastSuccessUtc: string | null;
  lastError: string | null;
  rowCount: number; // transactions in the mirror
  detailCount: number; // invoice lines in the mirror
  machine: string | null;
  requestedUtc: string | null;
  windowFrom: string | null; // the first date the mirror covers
}

export interface MemberSalesReport {
  reportKey: string;
  from: string;
  to: string;
  sync: MemberSalesSync;
  members: MemberSalesMember[];
  totals: MemberSalesTotals;
  months: MemberSalesMonth[];
  products: MemberSalesProduct[]; // by value, descending
  unsplit: MemberSalesUnsplit[];
  transactions: MemberSalesTransaction[];
  invoices: MemberSalesInvoice[];
  lines: MemberSalesLine[];
}

export interface MemberGroup {
  id: number;
  name: string;
  accountRefs: string; // comma-separated Sage refs
  sortOrder: number;
}

export const getSageReports = () => apiGet<SageReport[]>("/reports/sage");
export const createSageReport = (input: SageReportInput) => apiPost<SageReport>("/reports/sage", input);
export const updateSageReport = (key: string, input: SageReportInput) =>
  apiPut<SageReport>(`/reports/sage/${encodeURIComponent(key)}`, input);
export const deleteSageReport = (key: string) => request<void>("DELETE", `/reports/sage/${encodeURIComponent(key)}`);

export const getMemberSalesReport = (reportKey: string, from: string, to: string) =>
  apiGet<MemberSalesReport>(
    `/reports/member-sales/${encodeURIComponent(reportKey)}?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
  );
export const getMemberGroups = (reportKey: string) =>
  apiGet<MemberGroup[]>(`/reports/member-sales/${encodeURIComponent(reportKey)}/groups`);
// Asks the sidecar on the Sage machine for a fresh read of the sales mirror (a few minutes).
export const requestSageSalesRefresh = () => apiPost<SageSyncState>("/sage/sync/sales/refresh", {});
// Withdraws it: a pending request is dropped, a read in progress stops within seconds.
export const cancelSageSalesRefresh = () => apiPost<SageSyncState>("/sage/sync/sales/cancel", {});
