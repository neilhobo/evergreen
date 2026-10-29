import { apiGet, apiPost, apiPut, request } from "./client";

// Current stock — per-product summary of the CodicoPallet master list (never pallet-level).
export interface StockSummaryItem { productId:number|null; code13:string|null; gtin:string|null; description:string|null; size:number|null; upp:number|null; categoryId:number|null; category:string|null; palletCount:number; totalWeight:number; }
// locationId: undefined = All (real in-stock pallet summary); a location id returns that
// location's standing counts (its latest made-current stock take, no weights).
export const getStockSummary = (locationId?: number) =>
  apiGet<StockSummaryItem[]>(`/stock/summary${locationId ? `?locationId=${locationId}` : ""}`);

// Stock takes — counts entered per product (no pallet identity). The UI edits a local
// draft (dispatch-order style) and saves the whole take in one call; Cancel saves
// nothing. Saving changes nothing else yet; "Make Current Stock" is a later phase.
export interface StockLocation { id:number; name:string|null; }
// Takes and transfers share this grid: transfers (isTransfer) move counts
// fromLocationName -> locationName and can only be deleted, never edited/made current.
export interface StockTake { id:number; dateCreated:string; dateCompleted:string|null; dateMadeCurrentStock:string|null; createdBy:string|null; locationId:number|null; locationName:string|null; isTransfer:boolean; fromLocationName:string|null; productCount:number; palletCount:number; }
export interface StockTakeItem { id:number; productId:number; gtin:string|null; description:string|null; size:number|null; upp:number|null; palletCount:number; }
export interface StockTakeDetail extends StockTake { items: StockTakeItem[]; }
export interface StockTakeItemInput { productId:number; palletCount:number; }

export const getStockLocations = () => apiGet<StockLocation[]>(`/stock/locations`);

// Live stock movement feed: production/stock-take additions (+1), order depletions
// (-1) and transfers (0, net-zero moves between locations). productId is null on
// whole-take rows.
export interface StockMovement { id:string; date:string|null; type:string; productId:number|null; description:string|null; size:number|null; reference:string; qty:number; direction:number; }
export const getStockHistory = (from: string, to: string) =>
  apiGet<StockMovement[]>(`/stock/history?from=${from}&to=${to}`);

// Sidebar counts for Current Stock: All = real in-stock pallets; per location = its
// standing counts (latest made-current take's pallet total, 0 if none applied).
export interface StockLocationCount { id:number; name:string|null; palletCount:number; }
export interface StockLocationCounts { allPallets:number; locations:StockLocationCount[]; }
export const getStockLocationCounts = () => apiGet<StockLocationCounts>(`/stock/location-counts`);
export const getStockTakes = () => apiGet<StockTake[]>(`/stock/takes`);
export const getStockTake = (id: number) => apiGet<StockTakeDetail>(`/stock/takes/${id}`);
export const createStockTake = (locationId: number, items: StockTakeItemInput[]) =>
  apiPost<StockTake>(`/stock/takes`, { locationId, items });
export const updateStockTake = (id: number, locationId: number, items: StockTakeItemInput[]) =>
  apiPut<StockTake>(`/stock/takes/${id}`, { locationId, items });
export const deleteStockTake = (id: number) => request<void>("DELETE", `/stock/takes/${id}`);

// Move counted stock between locations (counts-only; pallet in/out state untouched).
export const createStockTransfer = (fromLocationId: number, toLocationId: number, items: StockTakeItemInput[]) =>
  apiPost<StockTake>(`/stock/transfers`, { fromLocationId, toLocationId, items });

// Manual stock correction: add N pallets of a product at a location (the count was short).
export const manualAddStock = (productId: number, locationId: number, palletCount: number) =>
  apiPost<void>(`/stock/manual-add`, { productId, locationId, palletCount });

// Apply an open stock take to the pallet list: newest pallets matching the effective
// counts (this take + the latest made-current take of each other location) stay in
// stock — previously-out pallets come back if now covered — everything else goes out.
export interface MakeCurrentResult { broughtBack:number; markedOut:number; created:number; inStock:number; }
export const makeCurrentStock = (id: number) =>
  apiPost<MakeCurrentResult>(`/stock/takes/${id}/make-current`, {});
