import { apiDownload, apiGet, apiPost } from "./client";

// Mirrors Globeweigh.Api Controllers/SageCustomersController.cs (camelCase JSON).

// One mirrored Sage entity's freshness ("Customers"): when the sidecar last pulled it,
// whether that worked, and whether a refresh has been asked for and not yet delivered.
export interface SageSyncState {
  entity: string;
  lastRunUtc: string | null;
  lastSuccessUtc: string | null;
  lastError: string | null;
  rowCount: number;
  detailCount: number;
  durationMs: number;
  machine: string | null;
  requestedUtc: string | null;
  requestedBy: string | null;
}

export interface SageCustomerRow {
  id: number;
  accountRef: string;
  name: string | null;
  address1: string | null;
  address2: string | null;
  address3: string | null;
  address4: string | null;
  address5: string | null;
  contact: string | null;
  telephone: string | null;
  email: string | null;
  defTaxCode: number; // Sage's T-code number (T20 = 20)
  balance: number;
  onHold: boolean;
  inactive: boolean;
  addressCount: number;
  deliveryAddressCount: number;
  lastSeenUtc: string;
}

export interface SageAddressRow {
  addressNumber: number;
  name: string | null;
  description: string | null;
  type: string | null; // Registered | Delivery | Contact
  address1: string | null;
  address2: string | null;
  address3: string | null;
  address4: string | null;
  address5: string | null;
  telephone: string | null;
  contact: string | null;
  email: string | null;
}

export interface SageCustomersResult {
  sync: SageSyncState;
  customers: SageCustomerRow[];
}

export const getSageCustomers = () => apiGet<SageCustomersResult>("/sage/customers");
export const getSageCustomerAddresses = (accountRef: string) =>
  apiGet<SageAddressRow[]>(`/sage/customers/${encodeURIComponent(accountRef)}/addresses`);
export const getSageSync = () => apiGet<SageSyncState[]>("/sage/sync");
// Asks the sidecar on the Sage machine for a fresh pull; it answers on its next poll.
export const requestSageCustomerRefresh = () => apiPost<SageSyncState>("/sage/sync/customers/refresh", {});
// Withdraws it: a pending request is dropped, a pull in progress stops within seconds.
export const cancelSageCustomerRefresh = () => apiPost<SageSyncState>("/sage/sync/customers/cancel", {});

// ---- Comparison: Sage as the source of truth (Services/SageComparison.cs) ----

// One thing out of date on the Evergreen side.
export interface SageComparisonIssue {
  category: string;
  accountRef: string;
  name: string | null;
  detail: string;
  evergreen: string | null;
  sage: string | null;
  sageAddressType: string | null;
  evergreenCustomerId: number | null;
  orders: number;
  lastOrder: string | null;
}

export interface SageComparisonSummary {
  sageCustomers: number;
  evergreenCustomers: number;
  matched: number;
  customersWithIssues: number;
  customersWithOrdersWithIssues: number;
  // Category → count, in the order the report lists categories (zero counts included).
  byCategory: Record<string, number>;
}

export interface SageComparisonResult {
  sync: SageSyncState;
  summary: SageComparisonSummary;
  issues: SageComparisonIssue[];
}

export interface SageAddressSide {
  id: number | null; // Evergreen address id
  number: number | null; // Sage address number
  type: string | null; // Sage: Registered | Delivery | Contact
  name: string | null;
  description: string | null;
  lines: string[];
  telephone: string | null;
  contact: string | null;
  email: string | null;
  modified: string | null; // Evergreen: DateLastModified
}

export interface SageCustomerSide {
  id: number | null;
  name: string | null;
  inactive: boolean;
  onHold: boolean | null;
  taxCode: number | null;
  balance: number | null;
  orders: number;
  lastOrder: string | null;
  addresses: SageAddressSide[];
}

// One account as both systems hold it.
export interface SageSideBySide {
  accountRef: string;
  evergreen: SageCustomerSide | null;
  sage: SageCustomerSide | null;
}

export const getSageComparison = (includeBlanks: boolean) =>
  apiGet<SageComparisonResult>(`/sage/comparison?includeBlanks=${includeBlanks}`);
export const getSageComparisonDetail = (accountRef: string) =>
  apiGet<SageSideBySide>(`/sage/comparison/${encodeURIComponent(accountRef)}`);
export const downloadSageComparisonCsv = (p: { includeBlanks: boolean; withOrders: boolean; category?: string }) =>
  apiDownload(
    `/sage/comparison/export.csv?includeBlanks=${p.includeBlanks}&withOrders=${p.withOrders}` +
      (p.category ? `&category=${encodeURIComponent(p.category)}` : ""),
  );

// Comparison > Missing in Evergreen > "Copy to Evergreen" (Services/SageComparison.cs,
// PlanMissingAsync / ImportMissingAsync): the plan shown in the popup, then the copy itself.
export interface SageImportAddress {
  sageNumber: number | null;
  type: string | null;
  name: string | null;
  description: string | null;
  lines: string[];
  telephone: string | null;
  contact: string | null;
  email: string | null;
}
export interface SageImportCustomer {
  accountRef: string;
  name: string;
  inactive: boolean;
  onHold: boolean;
  addresses: SageImportAddress[];
}
export interface SageImportPlan {
  customers: SageImportCustomer[];
  skipped: { accountRef: string; name: string | null; reason: string }[];
}
export interface SageImportResult {
  customers: number;
  addresses: number;
  noLongerMissing: number; // added to Evergreen by someone else since the plan was shown
}
export const getSageMissingPlan = () => apiGet<SageImportPlan>("/sage/comparison/missing");
export const importSageMissing = (accountRefs: string[]) =>
  apiPost<SageImportResult>("/sage/comparison/missing/import", { accountRefs });
// Accounts that will never be copied: Missing in Evergreen -> the Ignored category, and back.
export const ignoreSageAccounts = (accountRefs: string[]) =>
  apiPost<{ changed: number }>("/sage/comparison/ignore", { accountRefs });
export const unignoreSageAccounts = (accountRefs: string[]) =>
  apiPost<{ changed: number }>("/sage/comparison/unignore", { accountRefs });
