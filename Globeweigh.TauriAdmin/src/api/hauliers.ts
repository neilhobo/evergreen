import { apiGet, apiPost, apiPut, request } from "./client";

// Mirrors Globeweigh.Api Controllers/HauliersController.cs HaulierRow (camelCase JSON).
export interface HaulierRow {
  id: number;
  code: string | null;
  name: string | null;
  contactName: string | null;
  tel: string | null;
  // Every address on the haulier (HaulierEmail rows), in the order they were added.
  emails: string[];
  addressLine1: string | null;
  addressLine2: string | null;
  addressLine3: string | null;
  addressLine4: string | null;
  addressLine5: string | null;
  vatNumber: string | null;
  nominalCode: string | null;
  vatCode: string | null;
  intakeHaulier: boolean;
  dateCreated: string | null;
  dateDeleted: string | null;
  // List only: the creator's name, resolved from GlobeweighUser by the API. Absent on
  // the create/update responses — the grid refreshes after save.
  createdBy?: string | null;
}

// Writable fields (mirrors HaulierInput). `emails` is the haulier's whole list: on an
// update, addresses left out are removed and new ones added.
export interface HaulierInput {
  code: string | null;
  name: string | null;
  contactName: string | null;
  tel: string | null;
  emails: string[];
  addressLine1: string | null;
  addressLine2: string | null;
  addressLine3: string | null;
  addressLine4: string | null;
  addressLine5: string | null;
  vatNumber: string | null;
  nominalCode: string | null;
  vatCode: string | null;
  intakeHaulier: boolean;
}

export const getHauliers = (includeDeleted = false) =>
  apiGet<HaulierRow[]>(`/hauliers?includeDeleted=${includeDeleted}`);

export const createHaulier = (input: HaulierInput) => apiPost<HaulierRow>("/hauliers", input);
export const updateHaulier = (id: number, input: HaulierInput) =>
  apiPut<HaulierRow>(`/hauliers/${id}`, input);
export const deleteHaulier = (id: number) => request<void>("DELETE", `/hauliers/${id}`);
