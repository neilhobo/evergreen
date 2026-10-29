import { apiGet, apiPost, apiPut, request } from "./client";
import { OrderInput, OrderItemInput } from "./dispatch";

// My Orders (the Sales role's tab) — orders a salesperson builds under their own login and
// sends to the office for approval. Mirrors Controllers/SalesOrdersController.cs; every call
// is scoped to the signed-in user.

// Where an order has got to. draft/pending/returned are the salesperson's side; approved,
// planned (on a load), dispatched and cancelled come from the office order it became.
export type SalesOrderStatus =
  | "draft"
  | "pending"
  | "returned"
  | "approved"
  | "planned"
  | "dispatched"
  | "cancelled";

export interface SalesOrderRow {
  id: number;
  ref: string; // S-0001
  status: SalesOrderStatus;
  customerId: number;
  customerName: string | null;
  addressId: number;
  addressText: string | null; // name + lines, newline-joined
  deliveryDate: string | null; // null = ASAP
  poNumber: string | null;
  comments: string | null;
  pickingComments: string | null;
  hasPos: boolean;
  officeNote: string | null; // why the office returned it
  dateCreated: string;
  dateSent: string | null;
  dateApproved: string | null;
  orderNo: number | null; // once approved
  loadId?: number | null; // the load it is on (View Load Detail)
  loadDate: string | null;
  loadHaulier: string | null;
  dateDispatched: string | null;
  salesRepName?: string | null; // the office list only
  // Entered by the office with this rep as its Sales Rep (not sent from My Orders): read-only,
  // id is minus the office order's id, ref is "Office order".
  fromOffice?: boolean;
  items: OrderItemInput[];
}

export interface SalesOrderEvent {
  date: string;
  who: string; // "You", or the office user's name
  text: string;
  changes: string[] | null; // an edit's changes, one sentence each; null for other events
}

export interface SalesOrderDetail {
  order: SalesOrderRow;
  events: SalesOrderEvent[];
}

export interface SalesOrderInput {
  customerId: number;
  addressId: number;
  deliveryDate: string | null;
  poNumber: string | null;
  comments: string | null;
  pickingComments: string | null;
  hasPos: boolean;
  copiedFromId?: number;
  items: OrderItemInput[];
}

export const getSalesOrders = () => apiGet<SalesOrderRow[]>("/sales-orders");
// The office's list (Orders / Dispatch, "Orders (from sales)"): every rep's orders awaiting approval.
export const getOfficeSalesOrders = () => apiGet<SalesOrderRow[]>("/sales-orders/office");
export const getSalesOrder = (id: number) => apiGet<SalesOrderDetail>(`/sales-orders/${id}`);
export const createSalesOrder = (i: SalesOrderInput) => apiPost<SalesOrderRow>("/sales-orders", i);
export const updateSalesOrder = (id: number, i: SalesOrderInput) => apiPut<SalesOrderRow>(`/sales-orders/${id}`, i);
export const sendSalesOrder = (id: number) => apiPost<void>(`/sales-orders/${id}/send`, {});
export const recallSalesOrder = (id: number) => apiPost<void>(`/sales-orders/${id}/recall`, {});
export const deleteSalesOrder = (id: number) => request<void>("DELETE", `/sales-orders/${id}`);

// The office's answer to an order sent from sales. Approve makes it a real open order (the
// office's version of it — they may change it and add the haulier, vehicle and trailer);
// return sends it back to the rep with a note saying why.
export const approveSalesOrder = (id: number, order: OrderInput) =>
  apiPost<{ id: number; orderNo: number }>(`/sales-orders/${id}/approve`, order);
export const returnSalesOrder = (id: number, note: string) =>
  apiPost<void>(`/sales-orders/${id}/return`, { note });
// The office deleting a sent order outright; it goes from the rep's My Orders too.
export const deleteOfficeSalesOrder = (id: number) => request<void>("DELETE", `/sales-orders/office/${id}`);
