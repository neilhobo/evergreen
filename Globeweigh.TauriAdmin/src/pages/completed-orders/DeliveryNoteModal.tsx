import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { save } from "@tauri-apps/plugin-dialog";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import {
  CommercialInvoice,
  DeliveryNote,
  getCommercialInvoice,
  getDeliveryNote,
  invoiceNumber,
} from "../../api/dispatch";
import DeliveryNoteSheet from "./DeliveryNoteSheet";
import CommercialInvoiceSheet from "./CommercialInvoiceSheet";
import PrintNoteDialog, { PrinterList, clampCopies } from "./PrintNoteDialog";
import "./DeliveryNote.css";

interface Props {
  orderId: number;
  // Which document to preview: the delivery note (default) or the commercial invoice,
  // which prints on the same page template through the same Print / Export path.
  kind?: "note" | "invoice";
  // Invoice only: the pre-commercial invoice — ordered pallets at average weights.
  estimate?: boolean;
  onClose: () => void;
}

type Doc =
  | { kind: "note"; note: DeliveryNote }
  | { kind: "invoice"; invoice: CommercialInvoice };

// Last-used printer / copies, per PC + user (browser storage).
const PRINTER_KEY = "dn-print-printer";
const COPIES_KEY = "dn-print-copies";

// Preview of the printed Evergreen delivery note for one order (note no = order no).
// The note renders as real A4 page divs (DeliveryNoteSheet) — the header on page 1,
// item lines flowing across pages, signatures + small print on the last page — and
// printing prints those exact pages, so the preview always matches the paper output.
// The same sheet component is what the server renders for auto-printing, so this
// preview and the Weighbridge Printer's output cannot drift apart.
// (The old "Best Fit Footer" toggle was removed 2026-09-22; the sheet's bestFit prop
// stays for the server print page and defaults to false.)
export default function DeliveryNoteModal({ orderId, kind = "note", estimate = false, onClose }: Props) {
  const [doc, setDoc] = useState<Doc | null>(null);
  const [loading, setLoading] = useState(true);
  const docName =
    kind === "invoice" ? (estimate ? "Pre-Commercial Invoice" : "Commercial Invoice") : "Delivery Note";
  // Invoice no = LoadNo-OrderNo (blank when the order is not on a load yet); note no = order no.
  const docNo = !doc
    ? ""
    : doc.kind === "invoice"
      ? invoiceNumber(doc.invoice)
      : doc.note.noteNo;

  // Printers from the shell. null = the shell command is unavailable (browser dev, or
  // an older shell) and Print falls back to window.print().
  const [printers, setPrinters] = useState<PrinterList | null>(null);
  const [printer, setPrinter] = useState("");
  const [copies, setCopies] = useState(1);
  const [printDialog, setPrintDialog] = useState(false);
  const [printing, setPrinting] = useState(false);
  const [printStatus, setPrintStatus] = useState("");

  // Flag the body while the preview is open so the print CSS can hide the app
  // and print just the note pages (see DeliveryNote.css @media print), and force the
  // note's page box to win.
  //
  // Why the <style> is injected rather than just left in DeliveryNote.css: App.css
  // declares its own anonymous @page (13mm 13mm 18mm, for the weighbridge docket).
  // @page has no specificity, so the winner is purely whichever stylesheet the
  // bundler emits last — and Vite emits App.css last, so the note's rule loses. The
  // note is then laid out in a 184mm-wide page box, shrunk to 87% to fit, and comes
  // off the printer with a ~2cm border. Appending the rule to <head> puts it after
  // every bundled stylesheet in document order, so it wins whatever the bundler does
  // and on every WebView2 version (no named-page support required). Removed on close
  // so the docket keeps its own margins.
  useEffect(() => {
    document.body.classList.add("dn-print-mode");
    const style = document.createElement("style");
    style.id = "dn-page-rule";
    style.textContent = "@page { size: A4; margin: 10mm }";
    document.head.appendChild(style);
    return () => {
      document.body.classList.remove("dn-print-mode");
      style.remove();
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const d: Doc =
          kind === "invoice"
            ? { kind, invoice: await getCommercialInvoice(orderId, estimate) }
            : { kind, note: await getDeliveryNote(orderId) };
        if (!cancelled) setDoc(d);
      } catch (err) {
        if (!cancelled)
          void messageDialog(`Failed to load ${docName.toLowerCase()}.\n` + (err as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [orderId, kind, estimate, docName]);

  // Ask the shell for the installed printers; preselect the last-used one, else the
  // Windows default. Any failure (no shell, old shell) just leaves the fallback path.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await invoke<PrinterList>("list_printers");
        if (cancelled) return;
        let remembered = "";
        let rememberedCopies = "";
        try {
          remembered = localStorage.getItem(PRINTER_KEY) ?? "";
          rememberedCopies = localStorage.getItem(COPIES_KEY) ?? "";
        } catch {
          /* storage unavailable — defaults below */
        }
        const pick = list.printers.includes(remembered)
          ? remembered
          : list.default && list.printers.includes(list.default)
            ? list.default
            : (list.printers[0] ?? "");
        setPrinter(pick);
        if (rememberedCopies) setCopies(clampCopies(rememberedCopies));
        setPrinters(list);
      } catch {
        /* not in the shell, or a shell without list_printers: Print uses window.print() */
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Print button: open the note's own printer/copies dialog. Outside the shell (browser
  // dev) or on a shell that predates list_printers, fall back to window.print().
  function onPrintClick() {
    if (!doc) return;
    if (!printers) {
      window.print();
      return;
    }
    setPrintDialog(true);
  }

  // Print via the shell (WebView2 Print with the note's own settings: A4, 10mm margins,
  // backgrounds on, NO browser header/footer) straight to the chosen printer.
  //
  // Not window.print(): the Edge print preview that opens has a "Headers and footers"
  // option that defaults on (date/title top, URL/page bottom) and there is no way for
  // the page to switch it off — Chromium only hides it when the page has no top/bottom
  // margin, and the note's 10mm page box is what makes it print at true size. This
  // modal already IS the preview, so the shell prints the same pages silently with
  // the exact settings the Export PDF uses.
  async function printNote(chosenPrinter: string, chosenCopies: number) {
    setPrintDialog(false);
    setPrinter(chosenPrinter);
    setCopies(chosenCopies);
    setPrinting(true);
    setPrintStatus("");
    // Let React unmount the dialog before WebView2 lays the page out in print media —
    // an open dialog is a .modal-overlay too, and the print CSS would put it on paper.
    await new Promise<void>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
    try {
      await invoke("print_note", { printer: chosenPrinter, copies: chosenCopies });
      try {
        localStorage.setItem(PRINTER_KEY, chosenPrinter);
        localStorage.setItem(COPIES_KEY, String(chosenCopies));
      } catch {
        /* storage unavailable — fine */
      }
      setPrintStatus(
        `${chosenCopies} ${chosenCopies === 1 ? "copy" : "copies"} sent to ${chosenPrinter || "the default printer"}`,
      );
    } catch (err) {
      void messageDialog("Failed to print.\n" + String(err));
    } finally {
      setPrinting(false);
    }
  }

  // Export the note to a PDF via the shell (WebView2 PrintToPdf, same print settings).
  async function exportPdf() {
    if (!doc) return;
    try {
      const path = await save({
        defaultPath:
          doc.kind === "invoice"
            ? `${doc.invoice.estimate ? "PreCommercialInvoice" : "CommercialInvoice"}-${invoiceNumber(doc.invoice) || doc.invoice.orderNo}.pdf`
            : `DeliveryNote-${doc.note.noteNo}.pdf`,
        filters: [{ name: "PDF", extensions: ["pdf"] }],
      });
      if (!path) return;
      await invoke("export_pdf", { path });
    } catch (err) {
      void messageDialog("Failed to export PDF.\n" + String(err));
    }
  }

  // The invoice's products with no pallet / bag weight: the nett weights leave their
  // packaging in. A strip under the preview names them (set up under Products); nothing
  // shows when every product is set up.
  const missing = doc?.kind === "invoice" ? (doc.invoice.missingWeights ?? []) : [];
  const weightCheck =
    missing.length > 0 ? (
      <div className="dn-weight-check" role="status">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
          <path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" />
          <path d="M12 9v4M12 17h.01" />
        </svg>
        <div>
          <div className="dn-weight-check-title">
            {missing.length === 1 ? "1 product is" : `${missing.length} products are`} missing packaging
            weights, so the nett weights still include them. Set them up under Products.
          </div>
          <ul>
            {missing.map((m) => (
              <li key={m.productId}>
                <b>
                  {m.size != null ? `${m.size}L ` : ""}
                  {m.description ?? `Product ${m.productId}`}
                </b>
                {" — no "}
                {[m.noPalletWeight && "pallet weight", m.noBagWeight && "bag weight"].filter(Boolean).join(" or ")}
              </li>
            ))}
          </ul>
        </div>
      </div>
    ) : null;

  return (
    <Modal
      title={`${docName} ${docNo}`}
      notice={weightCheck}
      // Width hugs the sheet as previewed — the 190mm page plus the 10mm paper
      // surround each side (210mm ≈ 794px) + body padding + the vertical scrollbar's
      // gutter, so no horizontal scrollbar appears; height fits one page the same
      // way (296mm ≈ 1119px) plus the dialog chrome — on smaller screens the
      // viewport max-height clamps it.
      width={866}
      height={1241}
      draggable
      resizable
      onClose={onClose}
      footer={
        <>
          <span className="dn-print-status">{printing ? "Printing…" : printStatus}</span>
          <button className="btn-blue" disabled={!doc || printing} onClick={onPrintClick}>
            Print
          </button>
          <button className="btn-blue" disabled={!doc} onClick={() => void exportPdf()}>
            Export
          </button>
          <button className="toolbar-btn" onClick={onClose}>
            Close
          </button>
        </>
      }
    >
      {loading || !doc ? (
        <div className="data-table-empty">{loading ? "Loading…" : "Not found"}</div>
      ) : (
        // .dn-preview: screen-only paper surround (DeliveryNote.css) — purely
        // cosmetic, the sheet's page box and its pagination are the same as printed.
        <div className="dn-preview">
          {doc.kind === "invoice" ? (
            <CommercialInvoiceSheet invoice={doc.invoice} />
          ) : (
            <DeliveryNoteSheet note={doc.note} />
          )}
        </div>
      )}
      {printDialog && doc && printers && (
        <PrintNoteDialog
          noteNo={docNo}
          docName={docName}
          printers={printers}
          initialPrinter={printer}
          initialCopies={copies}
          onPrint={(p, c) => void printNote(p, c)}
          onClose={() => setPrintDialog(false)}
        />
      )}
    </Modal>
  );
}
