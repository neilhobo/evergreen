import { apiGet, apiPost, apiPut, request } from "./client";

// Mirrors Globeweigh.Api Data/Entities/CostingZone.cs (camelCase JSON).
export interface CostingZoneRow {
  id: number;
  description: string | null;
  secondaryDescription: string | null;
  // null = Both, true = Euro, false = Sterling (matches the WPF currency radio).
  isEuro: boolean | null;
  groupId: number;
  dateDeleted: string | null;
}

// Writable fields (mirrors CostingZoneEditInput).
export interface CostingZoneInput {
  description: string | null;
  secondaryDescription: string | null;
  isEuro: boolean | null;
}

export const getCostingZones = () => apiGet<CostingZoneRow[]>("/costing/zones");
export const createCostingZone = (input: CostingZoneInput) =>
  apiPost<CostingZoneRow>("/costing/zones", input);
export const updateCostingZone = (id: number, input: CostingZoneInput) =>
  apiPut<CostingZoneRow>(`/costing/zones/${id}`, input);
export const deleteCostingZone = (id: number) => request<void>("DELETE", `/costing/zones/${id}`);

// Currency label for a zone (null IsEuro = Both).
export function currencyLabel(isEuro: boolean | null): string {
  if (isEuro === null) return "Both";
  return isEuro ? "Euro" : "Sterling";
}
