import { useState } from "react";
import Modal from "../../components/Modal";

// Shape returned by the shell's list_printers command.
export interface PrinterList {
  printers: string[];
  default: string | null;
}

export function clampCopies(text: string | number): number {
  const n = typeof text === "number" ? text : parseInt(text, 10);
  return Number.isFinite(n) ? Math.min(99, Math.max(1, n)) : 1;
}

interface Props {
  noteNo: string | number;
  // What is being printed, for the title; defaults to the delivery note.
  docName?: string;
  printers: PrinterList;
  initialPrinter: string;
  initialCopies: number;
  onPrint: (printer: string, copies: number) => void;
  onClose: () => void;
}

// The delivery note's own print dialog: pick a printer, enter the copies, Print.
// Replaces the Edge print preview (window.print()), whose "Headers and footers"
// option cannot be defaulted off — the shell prints silently with the note's settings.
export default function PrintNoteDialog({ noteNo, docName = "Delivery Note", printers, initialPrinter, initialCopies, onPrint, onClose }: Props) {
  const [printer, setPrinter] = useState(initialPrinter);
  const [copiesText, setCopiesText] = useState(String(initialCopies));

  function submit() {
    onPrint(printer, clampCopies(copiesText));
  }

  return (
    <Modal
      title={`Print ${docName} ${noteNo}`}
      width={400}
      onClose={onClose}
      footer={
        <>
          <button className="toolbar-btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-blue" onClick={submit}>
            Print
          </button>
        </>
      }
    >
      <div className="form-col">
        <div className="form-field">
          <label className="form-label">Printer</label>
          <select className="form-input" value={printer} onChange={(e) => setPrinter(e.target.value)}>
            {printers.printers.length === 0 && <option value="">Default printer</option>}
            {printers.printers.map((p) => (
              <option key={p} value={p}>
                {p}
              </option>
            ))}
          </select>
        </div>
        <div className="form-field">
          <label className="form-label">Copies</label>
          <input
            className="form-input dn-print-copies"
            type="number"
            min={1}
            max={99}
            value={copiesText}
            autoFocus
            onFocus={(e) => e.target.select()}
            onChange={(e) => setCopiesText(e.target.value)}
            onBlur={() => setCopiesText(String(clampCopies(copiesText)))}
            onKeyDown={(e) => {
              if (e.key === "Enter") submit();
            }}
          />
        </div>
      </div>
    </Modal>
  );
}
