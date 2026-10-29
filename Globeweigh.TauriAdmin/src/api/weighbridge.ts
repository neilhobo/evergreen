import { apiGet, apiPost, apiPut, request } from "./client";

export interface WeighbridgeComplete {
  id: number;
  haulierName: string | null;
  vehicleReg: string | null;
  trailerNo: string | null;
  dateWeightIn: string | null;
  dateWeightOut: string | null;
  weightIn: number;
  weightOut: number;
  netWeight: number | null;
  orderCount: number | null;
  totalWeightPicked: number;
  product: string | null;
  supplier: string | null;
  bogName: string | null;
  isOutgoing: boolean;
  docketNumber: string | null;
  sampleBoxWeight: number | null;
  cubicM3: number | null;
}

export interface WeighbridgePending {
  id: number;
  haulierId: number;
  haulierName: string | null;
  vehicleReg: string | null;
  trailerNo: string | null;
  pendingWeight: number;
  pendingWeighDate: string | null;
  orderCount: number | null;
  isOutgoing: boolean;
  product: string | null;
  supplier: string | null;
  bogName: string | null;
  weightIn: number;
  dateWeightIn: string | null;
  docketNumber: string | null;
}

// Full record behind the grids (mirrors Globeweigh.Api WeighbridgeRecord entity) —
// used to prefill the edit form, since the grid views omit write-only fields.
export interface WeighbridgeRecordDetail {
  id: number;
  collectionDate: string | null;
  isOutgoing: boolean;
  haulierId: number;
  haulierName: string | null;
  product: string | null;
  supplier: string | null;
  bogName: string | null;
  vehicleReg: string | null;
  trailerNo: string | null;
  docketNumber: string | null;
  weightIn: number;
  dateWeightIn: string | null;
  weightOut: number;
  dateWeightOut: string | null;
  sampleBoxWeight: number | null;
  comments: string | null;
  // Also on the server's record; read by the Delivery detail panel.
  dateCreated?: string | null;
  dateDeleted?: string | null;
}

// Writable fields (mirrors WeighbridgeRecordInput).
export interface WeighbridgeRecordInput {
  isOutgoing: boolean;
  haulierId: number;
  vehicleReg: string | null;
  trailerNo: string | null;
  docketNumber: string | null;
  product: string | null;
  supplier: string | null;
  bogName: string | null;
  weightIn: number;
  dateWeightIn: string | null;
  weightOut: number;
  dateWeightOut: string | null;
  sampleBoxWeight: number | null;
  comments: string | null;
}

export const getCompleteRecords = (from: string, to: string) =>
  apiGet<WeighbridgeComplete[]>(`/weighbridge/complete?from=${from}&to=${to}`);

export const getPendingRecords = () => apiGet<WeighbridgePending[]>(`/weighbridge/pending`);

export const getWeighbridgeRecord = (id: number) =>
  apiGet<WeighbridgeRecordDetail>(`/weighbridge/${id}`);

export const createWeighbridgeRecord = (input: WeighbridgeRecordInput) =>
  apiPost<WeighbridgeRecordDetail>("/weighbridge", input);

export const updateWeighbridgeRecord = (id: number, input: WeighbridgeRecordInput) =>
  apiPut<WeighbridgeRecordDetail>(`/weighbridge/${id}`, input);

export const deleteWeighbridgeRecord = (id: number) =>
  request<void>("DELETE", `/weighbridge/${id}`);
