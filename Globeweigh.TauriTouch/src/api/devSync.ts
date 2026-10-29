// Dev-only data sync API — backs the "Sync Data" button. Clones the whole PROD database
// into the LOCAL database (wipe + replace). Always targets the LOCAL API explicitly, so it
// works regardless of which DB target the switcher is currently on (and can never run
// against prod — the endpoint only exists on a Development-environment API anyway).
import { apiPostTo, requestTo } from "./client";
import { DB_TARGET_URLS } from "../config";

export interface TableSyncResult {
  table: string;
  rowsCopied: number;
  error: string | null;
}

export interface SyncResult {
  success: boolean;
  durationMs: number;
  tables: TableSyncResult[];
  message: string | null;
}

// Prod connection entered in the dialog. Leave user/password blank for integrated (Windows) auth.
export interface ProdConn {
  server: string;
  database: string;
  user: string;
  password: string;
}

export interface TestResult {
  success: boolean;
  message: string;
}

export interface TablePreview {
  table: string;
  inserts: number;
  updates: number;
  deletes: number;
}

export interface PreviewResult {
  success: boolean;
  durationMs: number;
  tables: TablePreview[];
  message: string | null;
}

export const testProdConnection = (conn: ProdConn) =>
  apiPostTo<TestResult>(DB_TARGET_URLS.local, "/dev-sync/test-connection", conn);

// tables: a list of "schema.table" to restrict to, or null/undefined for all tables.
// Both calls scan every table on both servers, so both take a signal — aborting it is what lets
// the dialog's Cancel button stop a run that is taking longer than the developer wants to wait.
export const previewSync = (conn: ProdConn, tables?: string[] | null, signal?: AbortSignal) =>
  apiPostTo<PreviewResult>(
    DB_TARGET_URLS.local, "/dev-sync/preview", { ...conn, tables: tables ?? null }, signal);

export const syncFromProd = (conn: ProdConn, tables?: string[] | null, signal?: AbortSignal) =>
  apiPostTo<SyncResult>(
    DB_TARGET_URLS.local, "/dev-sync/from-prod", { ...conn, tables: tables ?? null }, signal);

// All local (target) tables as "schema.table", for the table picker.
export const getSyncTables = () =>
  requestTo<string[]>(DB_TARGET_URLS.local, "GET", "/dev-sync/tables");
