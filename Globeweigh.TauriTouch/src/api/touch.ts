import { apiGet, apiPost, apiPut, request } from "./client";

// ---- Device config (screen mode) ----

export interface DeviceConfig {
  id: number;
  deviceName: string | null;
  // Mutually-exclusive screen mode.
  isWeighIn: boolean;
  isWeighOut: boolean;
  factory: boolean;
  // Home-page button toggles — only meaningful in Factory mode.
  sampleWeight: boolean;
  production: boolean;
  productionSummary: boolean;
  camera: boolean;
  manualStock: boolean;
  // Factory: a COM weighscale indicator is attached to this PC (shows the bottom-bar status).
  weighscale: boolean;
  // Factory: a label printer is attached to this PC (shows the bottom-bar status).
  printer: boolean;
  // Factory: show the sign-in screen on startup; off = straight to the Home menu, no login.
  loginEnabled: boolean;
  // Factory: a relay card is attached to this PC.
  relayCard: boolean;
  // Factory: show the app-wide missed-scan alarm popup on this PC (kiosk PCs never see it).
  productionAlarm: boolean;
  // Weigh In: ask for the haulier on a screen of its own, then Product / Supplier / Bog
  // together. Off = all four lists on one screen.
  haulierOwnScreen: boolean;
  // "PC Name": friendly name shown big above the Home menu buttons (null/blank = none).
  displayName: string | null;
  // Per-PC hardware setup (JSON blobs / printer name) — see deviceStore.ts.
  weighscaleConfig: string | null;
  relayConfig: string | null;
  printerName: string | null;
}

export interface DeviceConfigInput {
  isWeighIn: boolean;
  isWeighOut: boolean;
  factory: boolean;
  sampleWeight: boolean;
  production: boolean;
  productionSummary: boolean;
  camera: boolean;
  manualStock: boolean;
  weighscale: boolean;
  printer: boolean;
  loginEnabled: boolean;
  relayCard: boolean;
  productionAlarm: boolean;
  haulierOwnScreen: boolean;
  displayName: string | null;
}

// The OS hostname identifies which dbo.Device row this screen is. Inside the Tauri shell we
// read it from the os plugin; in a plain browser we fall back to a per-machine localStorage key.
export async function getDeviceName(): Promise<string> {
  const override = localStorage.getItem("touchDeviceName");
  if (override) return override;
  const inTauri = typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;
  if (inTauri) {
    try {
      const os = await import("@tauri-apps/plugin-os");
      const name = await os.hostname();
      if (name) return name;
    } catch {
      // fall through to the browser fallback
    }
  }
  return "DEV-TOUCH";
}

export const getDeviceByName = (name: string) =>
  apiGet<DeviceConfig>(`/device/by-name/${encodeURIComponent(name)}`);

export const getDevices = () => apiGet<DeviceConfig[]>("/device");

export const setDeviceConfig = (id: number, config: DeviceConfigInput) =>
  apiPut<DeviceConfig>(`/device/${id}/config`, config);

// ---- Delivery lookups (Haulier -> Product -> Supplier -> Bog) ----

export interface HaulierOption { id: number; name: string | null; }
export interface ProductOption { id: number; description: string | null; }
export interface SupplierOption { id: number; name: string | null; supplierId: number; }
export interface BogOption { id: number; name: string | null; }

export const getHauliers = () => apiGet<HaulierOption[]>("/touch/hauliers");
export const getProducts = () => apiGet<ProductOption[]>("/touch/products");
export const getProductSuppliers = (productId: number) =>
  apiGet<SupplierOption[]>(`/touch/products/${productId}/suppliers`);
export const getSupplierBogs = (supplierId: number) =>
  apiGet<BogOption[]>(`/touch/suppliers/${supplierId}/bogs`);

// ---- Weigh-In (Delivery) ----

export interface DeliveryWeighIn {
  haulierId: number;
  haulierName: string | null;
  product: string | null;
  supplier: string | null;
  bogName: string | null;
  vehicleReg: string;
  docketNumber: string | null;
  weight: number;
}

export const weighInDelivery = (body: DeliveryWeighIn) =>
  apiPost<{ id: number; idRef: string }>("/touch/weigh-in", body);

// ---- Weigh-In (Collection) ----

export interface DispatchOrderRow {
  id: number;
  orderNo?: number;
  customerName?: string | null;
  addressFull?: string | null;
  deliveryDate?: string | null;
  sortOrder?: number | null; // delivery order on the load, 1 = dropped first
  [key: string]: unknown;
}

export interface CollectionLoad {
  id: number;
  idRef: string | null;
  haulierName: string | null;
  orders: DispatchOrderRow[];
}

export const getCollection = (idRef: string) =>
  apiGet<CollectionLoad>(`/touch/collection/${encodeURIComponent(idRef)}`);

export const weighCollection = (id: number, trailerNo: string, weight: number) =>
  apiPost<void>(`/touch/collection/${id}/weigh`, { trailerNo, weight });

// ---- Weigh-Out ----

export interface PendingRecord {
  id: number;
  haulierName: string | null;
  vehicleReg: string | null;
  trailerNo: string | null;
  product: string | null;
  weightIn: number;
  dateWeightIn: string | null;
  docketNumber: string | null;
  isOutgoing: boolean;
}

export const getPending = () => apiGet<PendingRecord[]>("/touch/pending");

export const weighOut = (id: number, vehicleReg: string, weight: number) =>
  apiPost<void>(`/touch/pending/${id}/weigh-out`, { vehicleReg, weight });

// ---- Sample Weight ----

export interface SampleRecord {
  id: number;
  haulierName: string | null;
  vehicleReg: string | null;
  product: string | null;
  supplier: string | null;
  bogName: string | null;
  weightIn: number;
  netWeight: number | null;
  sampleBoxWeight: number | null;
  sampleCollected: boolean | null;
  dateWeightIn: string | null;
  isOutgoing: boolean;
}

// date as yyyy-MM-dd (local day); omit for today.
export const getToBeSampled = (date?: string) =>
  apiGet<SampleRecord[]>(`/touch/to-be-sampled${date ? `?date=${date}` : ""}`);

export const addSampleWeight = (id: number, weight: number) =>
  apiPost<void>(`/touch/${id}/sample-weight`, { weight });

// ---- Production pallets (reuses the read-only ProductionController) ----

export interface ProductionPallet {
  id: number;
  uniqueCode: string | null;
  codicoCode: string | null;
  gtin: string | null;
  description: string | null;
  size: number | null;
  upp: number | null;
  weight: number;
  dateCreated: string | null;
  isScanned: boolean;
  dateScanned: string | null;
  // How it came to be scanned: null or "Scanner" = the conveyor scanner read it;
  // "Reprint"/"Manual" = an operator cleared the row, which says nothing about the scanner.
  scanSource: string | null;
  isDuplicate: boolean | null;
  // The missed-scan alarm for this pallet has been silenced — its red row renders dimmed.
  alarmAcked: boolean;
  // Created by hand (Manual Stock / manual stock add / stock take), never conveyor-scanned
  // — excluded from the missed-scan rule.
  isManual: boolean;
}

// from/to as yyyy-MM-dd (local days). The screen polls this every 10s.
export const getProductionPallets = (from: string, to: string) =>
  apiGet<ProductionPallet[]>(`/production/pallets?from=${from}&to=${to}`);

// ---- Production missed-scan alarm (shared state across all touch PCs) ----

export interface AlarmPallet {
  id: number;
  uniqueCode: string | null;
}

export interface ProductionAlarmState {
  active: boolean;
  pallets: AlarmPallet[];
}

// TEMPORARY — testing only: simulate a missed scan (un-scans the pallet and clears any
// earlier alarm ack). Remove with the Production screen's "Mark as Missed Scan" button.
export const markPalletMissed = (id: number) =>
  apiPost<void>(`/production/pallets/${id}/mark-missed`, {});

// Marks a pallet scanned and clears any alarm ack — the Production screen's "Mark As Scanned"
// button, for clearing a missed pallet without printing a label. Recorded as ScanSource
// "Manual", so the row still shows the scanner never read it.
export const markPalletPassed = (id: number) =>
  apiPost<void>(`/production/pallets/${id}/mark-passed`, {});

// Polled every few seconds by every PC that shows the alarm popup or drives the relay siren.
export const getProductionAlarm = () => apiGet<ProductionAlarmState>("/production/alarm");

// Silence the current alarm (acks the currently-missed pallets). Allowed from ANY touch PC;
// a NEW missed pallet re-fires the alarm.
export const suppressProductionAlarm = (by: string | null) =>
  apiPost<ProductionAlarmState>("/production/alarm/suppress", { by });

export interface ProductionSummary {
  lineNumber: number | null;
  id: number | null;
  gtin: string | null;
  description: string | null;
  size: number | null;
  upp: number | null;
  recordCount: number;
  bagCount: number;
}

// Production totals per product over a date range (from/to as yyyy-MM-dd).
export const getProductionSummary = (from: string, to: string) =>
  apiGet<ProductionSummary[]>(`/production/summary?from=${from}&to=${to}`);

// ---- Manual Stock (pallet batches) ----

export interface FinishedProduct {
  id: number;
  description: string | null;
  gtin: string | null;
  size: number | null;
  upp: number | null;
  averageWeight: number | null; // kg per pallet (Product.AverageWeight)
}

export interface PalletBatchRow {
  id: number;
  orderNumber: string | null;
  productId: number;
  product: string | null;
  gtin: string | null;
  // Bag size + units per pallet, printed in the label description ("60L … 80pp").
  size: number | null;
  upp: number | null;
  palletCount: number;
  palletsWeighed: number;
  createdBy: string | null;
  dateCreated: string;
  // false = all pallets created up front at palletWeight (no weighing).
  requireIndividualWeight: boolean;
  palletWeight: number | null;
}

export interface ManualPallet {
  id: number;
  uniqueCode: string;
  weight: number;
  dateCreated: string | null;
  printed: boolean; // label sent to a printer (CodicoPallet.DateLabelPrinted)
}

export interface PalletBatchInput {
  orderNumber: string | null;
  productId: number;
  palletCount: number;
  createdBy: string | null;
  requireIndividualWeight: boolean;
  palletWeight: number | null; // required when requireIndividualWeight is false
}

// Standard bulk density (kg/m³): volume m³ = weight kg / bulkDensity.
export const getManualStockConfig = () => apiGet<{ bulkDensity: number }>("/manual-stock/config");
export const setManualStockConfig = (bulkDensity: number) =>
  apiPut<{ bulkDensity: number }>("/manual-stock/config", { bulkDensity });

export const getFinishedProducts = () => apiGet<FinishedProduct[]>("/manual-stock/products");
export const getPalletBatches = () => apiGet<PalletBatchRow[]>("/manual-stock/batches");
export const createPalletBatch = (body: PalletBatchInput) =>
  apiPost<PalletBatchRow>("/manual-stock/batches", body);
export const getBatchPallets = (batchId: number) =>
  apiGet<ManualPallet[]>(`/manual-stock/batches/${batchId}/pallets`);
export const addBatchPallet = (batchId: number, weight: number) =>
  apiPost<ManualPallet>(`/manual-stock/batches/${batchId}/pallets`, { weight });
export const deleteBatchPallet = (palletId: number) =>
  request<void>("DELETE", `/manual-stock/pallets/${palletId}`);
export const deletePalletBatch = (batchId: number) =>
  request<void>("DELETE", `/manual-stock/batches/${batchId}`);
export const markPalletsPrinted = (palletIds: number[]) =>
  apiPost<void>("/manual-stock/pallets/printed", { palletIds });

// ---- Cameras (reuses the Gate/Barrier config: go2rtc base + camera list) ----

export interface Camera {
  name: string;
  ip: string;
  stream: string; // go2rtc stream name
}

export interface GateBarrierConfig {
  gateEnabled: boolean;
  videoBaseUrl: string | null;
  cameras: Camera[];
}

export const getGateBarrierConfig = () => apiGet<GateBarrierConfig>("/gate-barrier/config");

// ---- Live weight + barrier (reused gate-barrier proxy) ----

export interface LiveWeight {
  // GlobeweighService returns weight as a STRING (e.g. "-40"); tolerate number too.
  weight?: number | string;
  isConnected?: boolean;
  ageMs?: number;
}

export const getLiveWeight = () => apiGet<LiveWeight>("/gate-barrier/weight");

export interface RelayState {
  connected: boolean;
  gateOpen: boolean;
  barrierOpen: boolean;
}

export const getRelayState = () => apiGet<RelayState>("/gate-barrier/state");
export const setBarrier = (open: boolean) => apiPost<RelayState>("/gate-barrier/barrier", { open });

// ---- Label reprint audit ----

// Records that a pallet label was reprinted on this PC. Printing is entirely client-side
// (canvas → the printer attached to this PC), so this call is the ONLY record that the
// label went out a second time.
//
// It ALSO marks an unscanned pallet as scanned (source "Reprint") — reprinting the label is
// how the factory tracks that a missed pallet has been dealt with.
//
// Fire-and-forget by design: it runs AFTER the label has already printed, so a flat API
// must never turn a successful print into an error on screen. Callers use logLabelReprint,
// which swallows the failure — the audit row is lost, the print is not. Failing that way is
// safe: the pallet simply stays unscanned, so the missed-scan alarm keeps flagging it rather
// than the row being quietly cleared without a label having gone out.
export interface LabelReprintLog {
  palletId: number;
  uniqueCode: string | null;
  source: "Production" | "ManualStock";
  deviceName: string | null;
  printerName: string | null;
  userName: string | null;
}

export function logLabelReprint(entry: LabelReprintLog): void {
  void apiPost<void>("/labels/reprint", entry).catch(() => {
    // API unreachable or the table is missing (migration not yet run) — the label is
    // already out of the printer, so there is nothing useful to tell the operator.
  });
}
