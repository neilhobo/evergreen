import { apiGet } from "./client";
// isManual / alarmAcked back the missed-scan highlighting: manual pallets never pass the
// conveyor scanner (so they sit outside the rule), and alarmAcked marks a missed pallet
// whose alarm a touch PC has already silenced. Both come straight off /production/pallets.
export interface CodicoPallet { id:number; uniqueCode:string|null; codicoCode:string|null; gtin:string|null; weight:number; description:string|null; size:number|null; upp:number|null; dateCreated:string|null; isScanned:boolean; dateScanned:string|null; lineNumber:number|null; isManual:boolean; alarmAcked:boolean; }
export const getProductionPallets = (from:string,to:string) => apiGet<CodicoPallet[]>(`/production/pallets?from=${from}&to=${to}`);
// Live incremental fetch: pallets with id > sinceId (oldest-first), for newest-first prepend.
export const getNewProductionPallets = (from:string,to:string,sinceId:number) => apiGet<CodicoPallet[]>(`/production/pallets?from=${from}&to=${to}&sinceId=${sinceId}`);

// Production summary — totals per product over a date range (byLine adds a Line No).
export interface ProductionSummaryItem { lineNumber:number|null; id:number|null; gtin:string|null; description:string|null; size:number|null; upp:number|null; recordCount:number; bagCount:number; }
export const getProductionSummary = (from:string,to:string,byLine:boolean) =>
  apiGet<ProductionSummaryItem[]>(`/production/summary?from=${from}&to=${to}&byLine=${byLine}`);

// Production Planning — every product on an open order with the lines behind it and its
// in-stock pallet count (mirrors PlanningController). Picked pallets are still on the floor
// until dispatch, so inStock includes them; the client works out cover and when it runs out.
export interface PlanningLine { orderId:number; orderNo:number; customerName:string|null; deliveryDate:string|null; dateCreated:string|null; quantity:number; picked:number; }
export interface PlanningProduct { productId:number; description:string|null; gtin:string|null; size:number|null; upp:number; inStock:number; lines:PlanningLine[]; }
export interface Planning { products:PlanningProduct[]; }
export const getProductionPlanning = () => apiGet<Planning>(`/production/planning`);
