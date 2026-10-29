import { apiGet, apiPost, apiPut, request } from "./client";

// Mirrors Globeweigh.Api Data/ReadModels/ProductRow.cs (camelCase JSON).
export interface Product {
  id: number;
  description: string | null;
  productType: string | null;
  skuCode: string | null;
  size: number | null;
  upp: number;
  productTypeId: number;
  categoryId: number;
  categoryName: string | null;
  gtin: string | null;
  commodityCode: string | null;
  hasPos: boolean | null;
  posId: number | null;
  posDescription: string | null;
  isBulkBag: boolean;
  // kg per pallet: the no-scan pick estimate for a product with no weighed pallets.
  averageWeight: number | null;
  palletWeight: number; // kg, read-only for now (25)
  bagWeightGrams: number | null;
  soldBy: SoldBy;
  dateDeleted: string | null;
}

// Product.SoldBy (ProductSoldBy in Product.cs). Pallet is the default for every product.
export type SoldBy = "Pallet" | "CubicMeter";
export const SOLD_BY_OPTIONS: { value: SoldBy; label: string }[] = [
  { value: "Pallet", label: "Pallet" },
  { value: "CubicMeter", label: "Cubic Meter" },
];
export const soldByLabel = (v: SoldBy | null | undefined) =>
  SOLD_BY_OPTIONS.find((o) => o.value === v)?.label ?? "Pallet";

// Lookup for the Product Type dropdown (mirrors ProductTypeRef).
export interface ProductTypeRef {
  id: number;
  typeDescription: string | null;
}

// Lookup for the Category dropdown (mirrors CategoryRef).
export interface CategoryRef {
  id: number;
  name: string | null;
}

// Writable fields (mirrors ProductInput).
export interface ProductInput {
  gtin: string | null;
  description: string | null;
  skuCode: string | null;
  size: number | null;
  upp: number;
  commodityCode: string | null;
  productTypeId: number;
  categoryId: number;
  posId: number | null;
  isBulkBag: boolean;
  averageWeight: number | null;
  bagWeightGrams: number | null;
  soldBy: SoldBy;
}

// eProductType ids (Globeweigh.Model eReferenceData.cs).
export const FINISHED_PRODUCT_TYPE_ID = 1;
export const POS_PRODUCT_TYPE_ID = 3;
export const RAW_MATERIAL_TYPE_ID = 4;
export const BESPOKE_PRODUCT_TYPE_ID = 6;

// A supplier linked to a raw-material product (vwProduct_Supplier).
export interface ProductSupplierRef {
  id: number; // Product_Supplier row id
  name: string | null;
  supplierId: number;
}

// A supplier's bog/field (Supplier_Bog).
export interface SupplierBogRef {
  id: number;
  name: string | null;
}

export const getProducts = (productTypeId = 0, includeDeleted = false) =>
  apiGet<Product[]>(`/products?productTypeId=${productTypeId}&includeDeleted=${includeDeleted}`);

export const getProductTypes = () => apiGet<ProductTypeRef[]>("/products/product-types");
export const getCategories = () => apiGet<CategoryRef[]>("/products/categories");
export const createProduct = (input: ProductInput) => apiPost<Product>("/products", input);
// An order whose 0 kg picks of the product took its new Average Weight on save.
export interface ReweighedOrder {
  orderId: number;
  orderNo: number;
  customerName: string | null;
  dateDispatched: string | null;
  pallets: number;
}
// Mirrors ProductUpdateResult: reweighedOrders is empty when no 0 kg picks were found.
export interface ProductUpdateResult {
  product: Product;
  reweighedOrders: ReweighedOrder[];
}
export const updateProduct = (id: number, input: ProductInput) =>
  apiPut<ProductUpdateResult>(`/products/${id}`, input);
export const deleteProduct = (id: number) => request<void>("DELETE", `/products/${id}`);

// Raw Materials cascade (Product -> Supplier -> Bog).
export const getProductSuppliers = (productId: number) =>
  apiGet<ProductSupplierRef[]>(`/products/${productId}/suppliers`);
export const addProductSupplier = (productId: number, supplierId: number) =>
  apiPost<void>(`/products/${productId}/suppliers`, { supplierId });
export const removeProductSupplier = (id: number) =>
  request<void>("DELETE", `/products/suppliers/${id}`);
export const getSupplierBogs = (supplierId: number) =>
  apiGet<SupplierBogRef[]>(`/products/suppliers/${supplierId}/bogs`);
export const createSupplierBog = (supplierId: number, name: string) =>
  apiPost<void>(`/products/suppliers/${supplierId}/bogs`, { name });
export const updateSupplierBog = (id: number, name: string) =>
  apiPut<void>(`/products/bogs/${id}`, { name });
export const deleteSupplierBog = (id: number) => request<void>("DELETE", `/products/bogs/${id}`);

// Units an order line's price applies to, per unit of quantity. A Pallet line's quantity is
// pallets and its price is per unit, so the pallet's UPP scales it; a CubicMeter line's
// quantity is m³ and its price is per m³, so it is 1.
export const priceUnitsPer = (soldBy: string | null | undefined, upp: number) =>
  soldBy === "CubicMeter" ? 1 : upp;
export const priceUnitsOf = (p: Product | undefined) => (p ? priceUnitsPer(p.soldBy, p.upp) : 1);
