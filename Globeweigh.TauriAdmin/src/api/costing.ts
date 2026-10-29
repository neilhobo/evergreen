import { apiGet, apiPost, apiPut, request } from "./client";
import { Product } from "./products";

// Mirrors Globeweigh.Api Data/ReadModels/CostingZonePriceRow.cs (camelCase JSON).
export interface CostingZonePrice {
  id: number;
  costingZoneId: number;
  productId: number;
  price: number;
  na: boolean;
  description: string | null;
  secondaryDescription: string | null;
  descriptionDisplay: string | null;
  isEuro: boolean | null;
  groupDescription: string | null;
}

export interface ZonePriceInput {
  price: number;
  na: boolean;
}

// Mirrors Globeweigh.Api Data/Entities/CostingZonePriceHistoryEntity.cs (camelCase JSON).
// Note: constingZonePriceId is intentionally misspelled to match the DB column.
export interface ZonePriceHistory {
  id: number;
  constingZonePriceId: number;
  productId: number;
  zoneId: number;
  oldPrice: number;
  newPrice: number;
  changedBy: string | null;
  dateCreated: string;
}

// Product prices for a costing zone (order-item picker pre-fills from the customer's zone).
export interface ZonePriceRow {
  productId: number;
  price: number;
  na: boolean;
}
export const getZonePrices = (zoneId: number) =>
  apiGet<ZonePriceRow[]>(`/costing/zones/${zoneId}/prices`);

export const getCostingProducts = () => apiGet<Product[]>(`/costing/products`);
export const getProductZones = (productId: number) =>
  apiGet<CostingZonePrice[]>(`/costing/products/${productId}/zones`);
export const updateZonePrice = (id: number, input: ZonePriceInput) =>
  apiPut<CostingZonePrice>(`/costing/zone-price/${id}`, input);
export const getZonePriceHistory = (id: number) =>
  apiGet<ZonePriceHistory[]>(`/costing/zone-price/${id}/history`);
// Every zone price row — the Costing Full matrix pivots these into zone columns.
export const getAllZonePrices = () => apiGet<CostingZonePrice[]>(`/costing/prices`);

// Bulk upsert of one product's price in every zone (mirrors ProductZonePricesInput).
export interface ProductZonePriceItem {
  costingZoneId: number;
  price: number;
  na: boolean;
}
export const updateProductZonePrices = (productId: number, items: ProductZonePriceItem[]) =>
  apiPut<void>(`/costing/products/${productId}/zone-prices`, { items });

// ── Customer-specific pricing (Product detail panel) ──

// Mirrors CostingController.CustomerPriceRow: a customer's own price for one product, with the
// customer's costing zone so it can be set against their zone price.
export interface CustomerPrice {
  id: number;
  customerSupplierId: number;
  customerCode: string | null;
  customerName: string | null;
  customerDeleted: boolean;
  costingZoneId: number;
  costingZone: string | null;
  isEuro: boolean | null;
  price: number;
  dateCreated: string;
  createdBy: string | null;
  dateModified: string | null;
  modifiedBy: string | null;
}

// One add / change / removal (oldPrice null = added, newPrice null = removed).
export interface CustomerPriceHistory {
  id: number;
  customerSupplierId: number;
  customerName: string | null;
  oldPrice: number | null;
  newPrice: number | null;
  changedBy: string | null;
  dateCreated: string;
}

export const getCustomerPrices = (productId: number) =>
  apiGet<CustomerPrice[]>(`/costing/products/${productId}/customer-prices`);
export const addCustomerPrice = (productId: number, customerSupplierId: number, price: number) =>
  apiPost<void>(`/costing/products/${productId}/customer-prices`, { customerSupplierId, price });
export const updateCustomerPrice = (id: number, price: number) =>
  apiPut<void>(`/costing/customer-price/${id}`, { price });
export const deleteCustomerPrice = (id: number) => request<void>("DELETE", `/costing/customer-price/${id}`);
export const getCustomerPriceHistory = (productId: number) =>
  apiGet<CustomerPriceHistory[]>(`/costing/products/${productId}/customer-price-history`);
// The customer's own prices, every product (Add Order Item offers them over the zone price).
export const getPricesForCustomer = (customerId: number) =>
  apiGet<{ productId: number; price: number }[]>(`/costing/customers/${customerId}/prices`);
