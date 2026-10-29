import { apiGet, apiPost, apiPut, request } from "./client";
// The list row. `emails` is the record's own email list (CustomerSupplierEmail rows), in
// the order they were added — nothing to do with an address's contact email. It only comes
// back on the list: the create/update responses are the bare entity (the page refreshes).
export interface CustomerSupplier { id:number; typeId:number; code:string|null; name:string|null; costingZoneId:number; costingZone:string|null; salesRepId:number|null; addressCount:number; emails:string[]; }
export interface CustomerAddress { id:number; customerSupplierId:number; addressName:string|null; address1:string|null; address2:string|null; address3:string|null; address4:string|null; address5:string|null; telephone:string|null; telephone2:string|null; email:string|null; contactName:string|null; deliveryNote:string|null; }
export interface CostingZoneRef { id:number; description:string|null; }
// Writable fields. `emails` is the record's whole list: on an update, addresses left out
// are removed and new ones added.
// assignToMe (create only): the caller becomes its sales rep — a customer added from My Orders > Customers.
export interface CustomerSupplierInput { typeId:number; code:string|null; name:string|null; costingZoneId:number; isBoth:boolean; inactive:boolean; emails:string[]; assignToMe?:boolean; }
export interface AddressInput { addressName:string|null; address1:string|null; address2:string|null; address3:string|null; address4:string|null; address5:string|null; telephone:string|null; telephone2:string|null; email:string|null; contactName:string|null; }
// mine: only the customers whose Sales Rep is the signed-in user (My Orders > Customers).
export const getCustomersSuppliers = (typeId:number, mine = false) => apiGet<CustomerSupplier[]>(`/customers-suppliers?typeId=${typeId}${mine ? "&mine=true" : ""}`);
export const getAddresses = (id:number) => apiGet<CustomerAddress[]>(`/customers-suppliers/${id}/addresses`);
export const getCostingZones = () => apiGet<CostingZoneRef[]>("/customers-suppliers/costing-zones");
export const createCustomerSupplier = (i:CustomerSupplierInput) => apiPost<CustomerSupplier>("/customers-suppliers", i);
export const updateCustomerSupplier = (id:number,i:CustomerSupplierInput) => apiPut<CustomerSupplier>(`/customers-suppliers/${id}`, i);
export const setCustomerCostingZone = (id:number,costingZoneId:number) => apiPut<void>(`/customers-suppliers/${id}/costing-zone`, { costingZoneId });
export const deleteCustomerSupplier = (id:number) => request<void>("DELETE", `/customers-suppliers/${id}`);
// The Sales Rep column: users with the Sales role (disabled ones flagged, so an existing rep still
// shows), and setting only a customer's rep (null = none).
export interface SalesRepRef { id:number; name:string; disabled:boolean; }
export const getSalesReps = () => apiGet<SalesRepRef[]>("/customers-suppliers/sales-reps");
export const setCustomerSalesRep = (id:number,salesRepId:number|null) => apiPut<void>(`/customers-suppliers/${id}/sales-rep`, { salesRepId });
export interface CustomerLabelConfig { hasCustomLogo:boolean; logoFileName:string|null; logoContentType:string|null; logoBase64:string|null; }
export interface LabelLogoInput { fileName:string; contentType:string; base64:string; }
export const getLabelConfig = (id:number) => apiGet<CustomerLabelConfig>(`/customers-suppliers/${id}/label-config`);
export const setLabelLogo = (id:number,i:LabelLogoInput) => apiPut<void>(`/customers-suppliers/${id}/label-config/logo`, i);
export const deleteLabelLogo = (id:number) => request<void>("DELETE", `/customers-suppliers/${id}/label-config/logo`);
export const createAddress = (csId:number,i:AddressInput) => apiPost<CustomerAddress>(`/customers-suppliers/${csId}/addresses`, i);
export const updateAddress = (addrId:number,i:AddressInput) => apiPut<CustomerAddress>(`/customers-suppliers/addresses/${addrId}`, i);
export const deleteAddress = (addrId:number) => request<void>("DELETE", `/customers-suppliers/addresses/${addrId}`);
// The address's reusable Delivery Note Info (Edit Customer > Delivery Note Info tab); empty clears it.
export const setAddressDeliveryNote = (addrId:number, text:string) => apiPut<void>(`/customers-suppliers/addresses/${addrId}/delivery-note`, { text });
