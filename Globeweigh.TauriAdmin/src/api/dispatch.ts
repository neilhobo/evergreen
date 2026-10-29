import { apiDownload, apiGet, apiPost, apiPut, request } from "./client";
import type { SoldBy } from "./products";
export interface DispatchOrder { id:number; orderNo:number; customerId:number; haulierId:number; deliveryDate:string|null; dateCreated:string|null; dateDispatched:string|null; dateDeleted:string|null; customerRef:string|null; customerName:string|null; addressFull:string|null; orderStatus:string|null; vehicleReg:string|null; createdBy:string|null; haulierName:string|null; poNumber:string|null; trailerNo:string|null; pickedQuantity:number; pickedWeight:number; totalQuantityOrdered:number; assigned:boolean|null; weighbridgeRecordId:number; sortOrder:number|null;
  // Open Orders only: the latest order confirmation emailed to the customer (the Emailed column).
  confirmationSentAt?:string|null; confirmationSentTo?:string|null; }
// isUnordered: picked, then the order line was removed by an edit - the pallets were
// never shipped. The API lists them so the picked pallets and the item totals agree.
export interface DispatchOrderItem { id:number; orderId:number; productId:number; productDescription:string|null; size:number|null; upp:number; quantity:number|null; totalUnits:number|null; pickedQuantity:number|null; pickedWeight:number; gtin:string|null; unitDisplay:string|null; orderNo:number|null; soldBy?:SoldBy|"Pos"; density?:number|null; pickedAt?:string|null; isUnordered?:boolean; }
// soldBy "Pos": the synthetic POS Board row the API lists after an order's lines (no product;
// picked = pickedQuantity 1, pickedAt when). "CubicMeter": a peat line — quantity is m³, picked by a
// single tap in the Picking app, which also records its density (kg/m³).
export const isTapPickedLine = (i: DispatchOrderItem) => i.soldBy === "Pos" || i.soldBy === "CubicMeter";
export interface WeighbridgePreLoad { id:number; idRef:string|null; collectionDate:string|null; haulierId:number; haulierName:string|null; vehicleReg:string|null; trailerNo:string|null; orderCount:number|null; emailSentAt:string|null; emailSentTo:string|null; }
export interface WbPending { id:number; idRef:string|null; isOutgoing:boolean; pendingWeighDate:string|null; haulierName:string|null; trailerNo:string|null; weightIn:number; orderCount:number|null; pickedQuantity:number; totalQuantityOrdered:number; }
// emailSentAt/To: the "ready for collection" email the load had on the Loads grid;
// dispatchEmailSentAt/To: the dispatch confirmation sent from the Completed Loads grid, which
// is what its Emailed column shows.
export interface WbCompleted { id:number; haulierName:string|null; trailerNo:string|null; weightIn:number; dateWeightOut:string|null; weightOut:number; netWeight:number|null; orderCount:number|null; pickedQuantity:number; emailSentAt:string|null; emailSentTo:string|null; dispatchEmailSentAt:string|null; dispatchEmailSentTo:string|null; }
export interface OrderItemInput { productId:number; quantity:number|null; price:number|null; }
export interface OrderInput { customerId:number; addressId:number; haulierId:number; deliveryDate:string|null; poNumber:string|null; comments:string|null; pickingComments:string|null; hasPos:boolean; salesRepId?:number|null; vehicleReg:string|null; trailerNo:string|null; orderNo?:number|null; items:OrderItemInput[]; }
export const getOpenOrders = () => apiGet<DispatchOrder[]>(`/dispatch/orders`);
// mine: only orders whose Sales Rep is the signed-in user (My Orders > Completed).
export const getCompletedOrders = (from:string,to:string,mine=false) => apiGet<DispatchOrder[]>(`/dispatch/completed?from=${from}&to=${to}${mine ? "&mine=true" : ""}`);
export const getOrderItems = (orderId:number) => apiGet<DispatchOrderItem[]>(`/dispatch/orders/${orderId}/items`);
// The individual pallets picked onto one order (the per-pallet detail behind the item
// grid's Picked totals) - same row shape as the picked array on a load.
export const getOrderPicked = (orderId:number) => apiGet<PickedPallet[]>(`/dispatch/orders/${orderId}/picked`);
export interface OrderEditItem { productId:number; quantity:number|null; price:number|null; }
// Items plus the header fields the grid rows omit (comments, addressId), for prefilling the edit form.
// orderNoEditable: true only for a pre-hand-over open order, whose ad-hoc number may be corrected.
export interface OrderEditDetail { orderNo:number; orderNoEditable:boolean; comments:string|null; pickingComments:string|null; hasPos:boolean; addressId:number; salesRepId:number|null; salesRepName:string|null; salesRepFixed:boolean; items:OrderEditItem[]; }
export const getOrderEditItems = (orderId:number) => apiGet<OrderEditDetail>(`/dispatch/orders/${orderId}/edit-items`);
export const getLoads = () => apiGet<WeighbridgePreLoad[]>("/dispatch/loads");
export const getDispatchPending = () => apiGet<WbPending[]>("/dispatch/pending");
// No range: the latest 500 (the Completed Loads panel). A range: every load weighed out in it.
export const getDispatchCompleted = (from?:string,to?:string) => apiGet<WbCompleted[]>(from && to ? `/dispatch/completed-weighbridge?from=${from}&to=${to}` : "/dispatch/completed-weighbridge");
export const createOrder = (i:OrderInput) => apiPost<{id:number;orderNo:number}>("/dispatch/orders", i);
export const updateOrder = (id:number,i:OrderInput) => apiPut<{id:number;orderNo:number}>(`/dispatch/orders/${id}`, i);
export const deleteOrder = (id:number) => request<void>("DELETE", `/dispatch/orders/${id}`);

export interface WeighbridgeRecordDetail { id:number; collectionDate:string|null; isOutgoing:boolean; haulierId:number; haulierName:string|null; trailerNo:string|null; weightIn:number; dateWeightIn:string|null; weightOut:number; dateWeightOut:string|null; datePickingComplete:string|null; comments:string|null; }
// isWeightEstimated: the weight is the API's estimate from the product's other pallets (a
// no-scan label, or a scanned code with no pallet record), not this pallet's own figure.
export interface PickedPallet { id:number; orderId:number; orderNo:number; productId:number; productDescription:string|null; gtin:string|null; size:number|null; upp:number|null; palletId:string|null; weight:number; dateCreated:string|null; isNoScan:boolean; isWeightEstimated?:boolean; }
export interface LoadPhoto { id:number; description:string|null; dateCreated:string|null; }
// Photo bytes are streamed by the (anonymous) picking controller.
export const loadPhotoUrl = (baseUrl:string, photoId:number) => `${baseUrl}/api/picking/photos/${photoId}`;
// Swap the descriptions (slots) of two of a load's photos — the images stay put.
export const swapLoadPhotos = (loadId:number, photoId:number, otherPhotoId:number) =>
  apiPost<void>(`/dispatch/loads/${loadId}/photos/swap`, { photoId, otherPhotoId });

// Everything the printed Evergreen delivery note shows (note number = order number).
export interface DeliveryNoteItem { description:string|null; size:number|null; upp:number; pallets:number; totalBags:number; soldBy?:SoldBy; cubicMeters?:number; }
// A note whose every line is sold by the cubic meter (raw peat) drops the pallet columns
// and totals in m³ instead of bags.
export const isCubicMeterNote = (items: DeliveryNoteItem[]) =>
  items.length > 0 && items.every((i) => i.soldBy === "CubicMeter");
// template: which delivery-note design prints (Admin > Delivery Note); absent = the original.
export type DeliveryNoteTemplate = "classic" | "ledger" | "greenband";
// confirmation: the order confirmation (emailed from Open Orders) — the same note titled Order
// Confirmation, its lines as ordered rather than picked.
export interface DeliveryNote { template?:DeliveryNoteTemplate; confirmation?:boolean; orderId:number; noteNo:number; date:string|null; poNumber:string|null; accountRef:string|null; customerName:string|null; addressLines:string[]; addressFull:string|null; telephone:string|null; comments:string|null; haulierName:string|null; vehicleReg:string|null; trailerNo:string|null; items:DeliveryNoteItem[]; }
export const getDeliveryNote = (orderId:number) => apiGet<DeliveryNote>(`/dispatch/orders/${orderId}/delivery-note`);
// Commercial invoice: the delivery note's template with prices. invoiceNo is the LOAD number (printed as invoiceNumber());
// weights are the goods only, never the lorry: totalGross (whole kg) = the order's calculated (picked) weight; totalNet
// = that less each pallet's empty pallet + UPP empty bags (2dp). rate is per bag.
// weight / nettWeight = the same for the line's own picked pallets; null when nothing is picked on it.
export interface CommercialInvoiceItem { description:string|null; size:number|null; upp:number; pallets:number; hsCode:string|null; units:number; weight?:number|null; nettWeight?:number|null; rate:number|null; nett:number; }
// A product on the invoice with no pallet weight / no bag weight set (Products): the preview warns, nothing prints.
export interface CommercialInvoiceMissingWeight { productId:number; description:string|null; size:number|null; noPalletWeight:boolean; noBagWeight:boolean; }
// One HS tariff code's share of the weights (the Combined Gross / Nett Weight tables). hsCode "" = no code on the product.
export interface CommercialInvoiceWeight { hsCode:string; gross:number; nett:number; }
// template: follows the delivery note's design (Admin > Delivery Note).
// estimate: the pre-commercial invoice — ordered pallets at the products' average weights (the order is not picked yet).
export interface CommercialInvoice { template?:DeliveryNoteTemplate; estimate?:boolean; orderId:number; invoiceNo:number; orderNo:number; date:string|null; accountRef:string|null; invoiceName:string|null; invoiceLines:string[]; deliverName:string|null; deliverLines:string[]; deliverFull:string|null; totalGross:number|null; totalNet:number|null; items:CommercialInvoiceItem[]; weightsByHsCode?:CommercialInvoiceWeight[]; missingWeights?:CommercialInvoiceMissingWeight[]; nett:number; vat:number; total:number; }
export const getCommercialInvoice = (orderId:number, estimate = false) => apiGet<CommercialInvoice>(`/dispatch/orders/${orderId}/commercial-invoice${estimate ? "?estimate=true" : ""}`);
// The printed invoice number: "LoadNo-OrderNo" (e.g. 5077-26501); blank until the order is on a load.
export const invoiceNumber = (inv:CommercialInvoice) => inv.invoiceNo > 0 ? `${inv.invoiceNo}-${inv.orderNo}` : "";
// One email sent for the load (Send Email), newest first on the detail. kind: "Ready" for the
// "ready for collection" message, "Dispatched" for the dispatch confirmation, "DispatchedCustomer"
// for a customer's copy of it. "PreCommercial" for the pre-commercial invoices email.
export interface LoadEmailSent { id:number; kind:"Ready"|"Dispatched"|"DispatchedCustomer"|"PreCommercial"; dateSent:string; sentTo:string; subject:string; sentBy:string|null; }
export interface LoadDetail { record:WeighbridgeRecordDetail; orders:DispatchOrder[]; items:DispatchOrderItem[]; picked:PickedPallet[]; photos:LoadPhoto[]; emails:LoadEmailSent[]; }
// Delivery order: 1 = the first drop of the load, N = the last. Held in DispatchOrder.SortOrder,
// which is the sequence picking and touch already work through.
export interface LoadOrderInput { orderId:number; deliveryOrder:number; }
// weightIn: only sent when editing a pending load (hand-corrected in-weight); null leaves it alone.
export interface LoadInput { haulierId:number; collectionDate:string|null; trailerNo:string|null; comments:string|null; weightIn:number|null; orders:LoadOrderInput[]; }
export const getLoadDetail = (id:number) => apiGet<LoadDetail>(`/dispatch/loads/${id}`);
// The two emails a load gets, each composed by the API: the preview shows it as it will go
// out, and send posts the addresses to put in To (one call per email). "ready" is the "ready
// for collection" message sent from the Loads grid; "dispatched" is the dispatch confirmation
// sent from the Completed Loads grid once the load has weighed out.
// "precommercial" is the pre-commercial invoices email, from Load detail while the load is on the Loads grid.
// "confirmation" is the odd one out: an ORDER's email, the order confirmation to the customer from
// Open Orders — its id is the order's, not a load's.
export type LoadEmailKind = "ready" | "dispatched" | "precommercial" | "confirmation";
// A file the message carries: an order's delivery note as PDF (the dispatch confirmation has
// one per order). Rendered by the API when the email is sent, and viewable beforehand from
// getDeliveryNotePdf.
// kind: "note" = the order's delivery note; "invoice" = its commercial invoice (attached to the haulier's dispatch confirmation on request).
// "preinvoice" = its pre-commercial invoice (the estimate, on the pre-commercial email).
// "confirmation" = its order confirmation (on the order confirmation email).
export interface LoadEmailAttachment { orderId:number; orderNo:number; fileName:string; kind:"note"|"invoice"|"preinvoice"|"confirmation"; }
// One customer's copy of the dispatch confirmation: their orders, their delivery notes, their addresses.
export interface CustomerLoadEmail { customerId:number; customerName:string; recipients:string[]; subject:string; html:string; text:string; attachments:LoadEmailAttachment[]; }
// customers: each customer's copy (dispatched only; empty on the ready email).
// customerName: the order confirmation's recipient (its recipients are the customer's addresses).
export interface LoadEmailPreview { ref:string; haulierName:string; customerName?:string|null; recipients:string[]; subject:string; html:string; text:string; attachments:LoadEmailAttachment[]; customers:CustomerLoadEmail[]; commercialInvoices?:LoadEmailAttachment[]; fromAddress:string|null; fromName:string|null; emailReady:boolean; }
export const getDeliveryNotePdf = (orderId:number) => apiDownload(`/dispatch/orders/${orderId}/delivery-note/pdf`);
export const getCommercialInvoicePdf = (orderId:number, estimate = false) => apiDownload(`/dispatch/orders/${orderId}/commercial-invoice/pdf${estimate ? "?estimate=true" : ""}`);
export const getOrderConfirmationPdf = (orderId:number) => apiDownload(`/dispatch/orders/${orderId}/order-confirmation/pdf`);
const loadEmailPath = (id:number, kind:LoadEmailKind) =>
  kind === "confirmation"
    ? `/dispatch/orders/${id}/confirmation-email`
    : `/dispatch/loads/${id}/${kind === "ready" ? "email" : kind === "precommercial" ? "pre-commercial-email" : "dispatch-email"}`;
export const getLoadEmail = (id:number, kind:LoadEmailKind = "ready") => apiGet<LoadEmailPreview>(loadEmailPath(id, kind));
// customerId: on the dispatch confirmation, send that customer's copy instead of the haulier's.
// commercial: on the haulier's dispatch confirmation, attach each order's commercial invoice too.
export const sendLoadEmail = (id:number, to:string[], overrideTo?:string, kind:LoadEmailKind = "ready", customerId?:number, commercial = false) => apiPost<{sent:string[]}>(loadEmailPath(id, kind), { to, overrideTo, customerId, commercial });
export const createLoad = (i:LoadInput) => apiPost<{id:number}>("/dispatch/loads", i);
export const updateLoad = (id:number, i:LoadInput) => apiPut<{id:number}>(`/dispatch/loads/${id}`, i);
export const deleteLoad = (id:number) => request<void>("DELETE", `/dispatch/loads/${id}`);
export const completePicking = (id:number) => apiPost<void>(`/dispatch/loads/${id}/complete-picking`, {});
export const weighIn = (id:number, weight:number, trailerNo?:string) => apiPost<void>(`/dispatch/loads/${id}/weigh-in`, { weight, trailerNo });
export const weighOut = (id:number, weight:number) => apiPost<void>(`/dispatch/pending/${id}/weigh-out`, { weight });
export const addOutgoing = (haulierId:number, weight:number, comments:string|null) => apiPost<{id:number}>("/dispatch/outgoing", { haulierId, weight, comments });
// Orders that deleting this pending record would put back into the unassigned pool.
export interface LinkedOrder { id:number; orderNo:number; customerName:string|null; }
export const getPendingLinkedOrders = (id:number) => apiGet<LinkedOrder[]>(`/dispatch/pending/${id}/linked-orders`);
// Soft-deletes the record and unassigns its still-open orders, whose numbers it returns.
export const deletePending = (id:number) => request<{unassignedOrderNos:number[]}>("DELETE", `/dispatch/pending/${id}`);
// "Remove from pending": clears the in-weight so the load steps back to the Loads grid.
export const returnPendingToLoads = (id:number) => apiPost<void>(`/dispatch/pending/${id}/return-to-loads`, {});
// "Un-complete": clears the out-weight so a completed load steps back to Pending.
export const uncompleteLoad =(id:number) => apiPost<void>(`/dispatch/completed-weighbridge/${id}/uncomplete`, {});
