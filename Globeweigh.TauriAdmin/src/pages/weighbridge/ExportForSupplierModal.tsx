import { useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import { WeighbridgeComplete } from "../../api/weighbridge";
import { cubicTotal, saveExport, toExportRows } from "./exportForSupplier";
import { fmtQty2 } from "../../format";

interface Props {
  rows: WeighbridgeComplete[];
  onClose: () => void;
}

// Preview of the "Export for Supplier" output, with an Export button. Shows exactly the rows
// and TOTAL that will be written to the .xlsx (the currently-visible/filtered Completed Orders).
export default function ExportForSupplierModal({ rows, onClose }: Props) {
  const [busy, setBusy] = useState(false);
  const data = toExportRows(rows);
  const total = cubicTotal(rows);

  async function onExport() {
    setBusy(true);
    try {
      const saved = await saveExport(rows);
      if (saved) onClose();
      else setBusy(false); // dialog cancelled — stay on the preview
    } catch (e) {
      const msg = e instanceof Error ? e.message : typeof e === "string" ? e : JSON.stringify(e);
      void messageDialog("Export failed.\n" + (msg || "Unknown error"));
      setBusy(false);
    }
  }

  return (
    <Modal
      title={`Export for Supplier — ${data.length} record${data.length === 1 ? "" : "s"}`}
      fullScreen
      onClose={onClose}
      footer={
        <>
          <button className="toolbar-btn" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn-blue" onClick={onExport} disabled={busy || data.length === 0}>
            {busy ? "Exporting…" : "Export to Excel"}
          </button>
        </>
      }
    >
      <div className="export-preview">
        <table className="export-table">
          <thead>
            <tr>
              <th>Haulier</th>
              <th>Docket No</th>
              <th>Vehicle Reg</th>
              <th>Product</th>
              <th>Supplier</th>
              <th>Bog Name</th>
              <th>Weighed In Date</th>
              <th className="num">Cubic M3</th>
            </tr>
          </thead>
          <tbody>
            {data.map((r, i) => (
              <tr key={i}>
                <td>{r.haulier}</td>
                <td>{r.docket}</td>
                <td>{r.vehicleReg}</td>
                <td>{r.product}</td>
                <td>{r.supplier}</td>
                <td>{r.bog}</td>
                <td>{r.weighedIn}</td>
                <td className="num">{r.cubicM3 == null ? "" : fmtQty2(r.cubicM3)}</td>
              </tr>
            ))}
            {data.length === 0 && (
              <tr>
                <td colSpan={8} className="export-empty">No visible records to export.</td>
              </tr>
            )}
          </tbody>
          {data.length > 0 && (
            <tfoot>
              <tr className="export-total">
                <td colSpan={7} className="num">TOTAL:</td>
                <td className="num">{fmtQty2(total)}</td>
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    </Modal>
  );
}
