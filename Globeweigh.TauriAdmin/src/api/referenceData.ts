import { apiGet, apiPost, apiPut, request } from "./client";
export interface EntityType { id:number; entityName:string|null; entityTypeName:string|null; entityTypeId:number; isReferenceDataType:boolean; displayName:string|null; sortOrder:number; adminAccessOnly:boolean; superAdminAccessOnly:boolean; }
export interface ReferenceDataItem { id:number; refType:string|null; refTypeId:number; code:string|null; displayName:string|null; alternativeDisplayName:string|null; intData:number; dateDeleted:string|null; }
export interface RefDataInput { refTypeId:number; code:string|null; displayName:string|null; alternativeDisplayName:string|null; }
export const getRefDataTypes = () => apiGet<EntityType[]>(`/reference-data/types`);
export const getReferenceData = (refTypeId:number) => apiGet<ReferenceDataItem[]>(`/reference-data?refTypeId=${refTypeId}`);
export const createReferenceData = (input:RefDataInput) => apiPost<ReferenceDataItem>("/reference-data", input);
export const updateReferenceData = (id:number, input:RefDataInput) => apiPut<ReferenceDataItem>(`/reference-data/${id}`, input);
export const deleteReferenceData = (id:number) => request<void>("DELETE", `/reference-data/${id}`);
