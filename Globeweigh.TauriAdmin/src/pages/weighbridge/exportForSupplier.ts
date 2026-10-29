import type { WeighbridgeComplete } from "../../api/weighbridge";

// Match the WPF "Weighed In Date" format exactly: dd/MM/yyyy HH:mm.
function fmtWeighIn(iso: string | null): string {
  if (!iso) return "";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "";
  const p = (n: number) => String(n).padStart(2, "0");
  return `${p(d.getDate())}/${p(d.getMonth() + 1)}/${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

// The exact columns the WPF "Export for Supplier" produces.
export interface ExportRow {
  haulier: string;
  docket: string;
  vehicleReg: string;
  product: string;
  supplier: string;
  bog: string;
  weighedIn: string;
  cubicM3: number | null;
}

export function toExportRows(rows: WeighbridgeComplete[]): ExportRow[] {
  return rows.map((r) => ({
    haulier: r.haulierName ?? "",
    docket: r.docketNumber ?? "",
    vehicleReg: r.vehicleReg ?? "",
    product: r.product ?? "",
    supplier: r.supplier ?? "",
    bog: r.bogName ?? "",
    weighedIn: fmtWeighIn(r.dateWeightIn),
    cubicM3: r.cubicM3 ?? null,
  }));
}

export function cubicTotal(rows: WeighbridgeComplete[]): number {
  return rows.reduce((s, r) => s + (r.cubicM3 ?? 0), 0);
}

// Build the styled .xlsx (ports Utilities.ExportCompletedOrdersQuickExcel): bold/centered header,
// Cubic M3 right-aligned #,##0.00, blank row + bold TOTAL row, frozen header. exceljs is lazy-loaded.
async function buildBuffer(data: ExportRow[], total: number): Promise<Uint8Array> {
  const ExcelJS = (await import("exceljs")).default;
  const wb = new ExcelJS.Workbook();
  const ws = wb.addWorksheet("Completed Orders");

  ws.addRow(["Haulier", "Docket No", "Vehicle Reg", "Product", "Supplier", "Bog Name", "Weighed In Date", "Cubic M3"]);
  const header = ws.getRow(1);
  header.font = { bold: true };
  header.alignment = { horizontal: "center" };

  for (const r of data) {
    const row = ws.addRow([r.haulier, r.docket, r.vehicleReg, r.product, r.supplier, r.bog, r.weighedIn, r.cubicM3]);
    const cubic = row.getCell(8);
    cubic.numFmt = "#,##0.00";
    cubic.alignment = { horizontal: "right" };
  }

  if (data.length > 0) {
    ws.addRow([]);
    const totalRow = ws.addRow(["", "", "", "", "", "", "TOTAL:", total]);
    totalRow.font = { bold: true };
    totalRow.getCell(7).alignment = { horizontal: "right" };
    const t = totalRow.getCell(8);
    t.alignment = { horizontal: "right" };
    t.numFmt = "#,##0.00";
  } else {
    ws.addRow(["No visible records after filtering."]);
  }

  [22, 12, 13, 20, 18, 16, 18, 12].forEach((w, i) => (ws.getColumn(i + 1).width = w));
  ws.views = [{ state: "frozen", ySplit: 1 }];

  const buf = await wb.xlsx.writeBuffer();
  return new Uint8Array(buf as ArrayBuffer);
}

function defaultName(): string {
  const n = new Date();
  const s = `${n.getFullYear()}-${String(n.getMonth() + 1).padStart(2, "0")}-${String(n.getDate()).padStart(2, "0")}`;
  return `Completed Orders ${s}.xlsx`;
}

// Generate + save. In the Tauri window the browser can't download, so we use the dialog plugin
// for a Save-As path and a small Rust command (save_bytes) to write. In a plain browser we fall
// back to an anchor download. Returns true if saved, false if the user cancelled the dialog.
export async function saveExport(rows: WeighbridgeComplete[]): Promise<boolean> {
  const buf = await buildBuffer(toExportRows(rows), cubicTotal(rows));
  const filename = defaultName();
  const inTauri = typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;

  if (inTauri) {
    // Tauri v2 blocks custom app commands from the server-hosted (remote) UI, so the file is
    // saved via the fs plugin (a permitted plugin command), to the dialog-chosen path.
    const { save } = await import("@tauri-apps/plugin-dialog");
    const { writeFile } = await import("@tauri-apps/plugin-fs");
    const path = await save({
      defaultPath: filename,
      filters: [{ name: "Excel", extensions: ["xlsx"] }],
    });
    if (!path) return false; // cancelled
    try {
      await writeFile(path, buf);
    } catch (err) {
      const detail = typeof err === "string" ? err : err instanceof Error ? err.message : JSON.stringify(err);
      throw new Error(`Could not write the file.\n${detail}`);
    }
    return true;
  }

  const blob = new Blob([buf], {
    type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  return true;
}
