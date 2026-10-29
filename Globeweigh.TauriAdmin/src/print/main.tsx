import React from "react";
import ReactDOM from "react-dom/client";
import { CommercialInvoice, DeliveryNote, invoiceNumber } from "../api/dispatch";
import DeliveryNoteSheet from "../pages/completed-orders/DeliveryNoteSheet";
import CommercialInvoiceSheet from "../pages/completed-orders/CommercialInvoiceSheet";
// Not App.css — see print-base.css for why dragging in the app stylesheet blanks the PDF
// and fights the note's @page margins.
import "./print-base.css";

// Standalone delivery-note page for SERVER-SIDE printing (headless Chrome on evergreen).
//
// Why this exists rather than driving the app: the note's pagination is measured from
// real rendered geometry, so the only way to produce paper that matches the Completed
// Orders preview is to let a browser lay out the very same component. This entry point
// renders DeliveryNoteSheet and nothing else — no app shell, no login, no API call.
//
// The note data is inlined into the HTML by the API (see DispatchController's
// delivery-note/print endpoint) as window.__note. Inlining rather than fetching keeps the
// page anonymous (the dispatch API is [Authorize]) and removes a network race before the
// layout pass — by the time Chrome runs this script the data is already here.
//
// The commercial invoice prints from this same page (commercial-invoice/print, inlined as
// window.__invoice), for the PDFs the dispatch confirmation's commercial copy attaches.
declare global {
  interface Window {
    __note?: DeliveryNote;
    __invoice?: CommercialInvoice;
    // Flipped once pagination has settled. Chrome's --virtual-time-budget is the actual
    // capture trigger; this is the belt-and-braces signal for a CDP-driven capture and a
    // way to tell "rendered" from "still measuring" when debugging a bad print.
    __noteReady?: boolean;
  }
}

const note = window.__note;
const invoice = window.__invoice;
const root = document.getElementById("root") as HTMLElement;

function markReady() {
  window.__noteReady = true;
  document.documentElement.dataset.ready = "1";
}

if (invoice) {
  document.title = `Commercial Invoice ${invoiceNumber(invoice) || invoice.orderNo}`;
  ReactDOM.createRoot(root).render(
    <React.StrictMode>
      <CommercialInvoiceSheet invoice={invoice} onReady={markReady} />
    </React.StrictMode>,
  );
} else if (!note) {
  // Never silently print a blank sheet — a visible marker in the PDF is how an operator
  // (or the PrintWorker's failure log) can tell "the API sent no data" from "the printer
  // ate the job".
  root.textContent = "No delivery note data (window.__note was not set).";
  markReady();
} else {
  // The order confirmation (order-confirmation/print) is this same note with confirmation set.
  document.title = `${note.confirmation ? "Order Confirmation" : "Delivery Note"} ${note.noteNo}`;
  ReactDOM.createRoot(root).render(
    <React.StrictMode>
      <DeliveryNoteSheet note={note} onReady={markReady} />
    </React.StrictMode>,
  );
}
