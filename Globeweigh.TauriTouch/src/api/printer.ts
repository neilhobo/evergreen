// Label printer for this PC, via the Tauri shell. Which printer to use is a property
// of the PC (like the weighscale connection), so the selection lives in localStorage.
import { inTauriShell } from "./serial";
import { getStoredValue, setStoredValue } from "./deviceStore";

export interface PrinterInfo {
  name: string;
  isDefault: boolean;
}

// SAFETY: dev builds NEVER spool to a printer — same rule as the alarm relay in
// ProductionAlarmAgent. A dev shell carries the site's saved printer name, so testing a
// reprint would otherwise fire real labels out of the machine on the factory floor.
// Everything around the print still runs (the reprint is logged, the pallet is marked
// scanned), so the flow stays testable end to end — only the spool is skipped.
export const PRINTING_ENABLED = !import.meta.env.DEV;

export async function listPrinters(): Promise<PrinterInfo[]> {
  if (!inTauriShell())
    throw new Error("Printers are only available inside the Globeweigh Touch app.");
  const { invoke } = await import("@tauri-apps/api/core");
  return invoke<PrinterInfo[]>("list_printers");
}

// Raw bytes (ZPL) straight to a named Windows printer via the spooler's RAW datatype.
export async function printRaw(printerName: string, data: string): Promise<void> {
  if (!PRINTING_ENABLED) {
    console.info(`[dev] print skipped — would have sent ${data.length} bytes to ${printerName}`);
    return;
  }
  if (!inTauriShell())
    throw new Error("Printing is only available inside the Globeweigh Touch app.");
  const { invoke } = await import("@tauri-apps/api/core");
  await invoke("print_raw", { printer: printerName, data });
}

// Print a rendered label through the normal Windows driver path (like printing a Word
// document). The landscape canvas is rotated to portrait (the printer feeds 10 cm-wide
// media), converted to top-down BGRA pixels, and handed to the shell, which re-rotates to
// the driver's page orientation if needed and scales to fit preserving aspect ratio.
export async function printImage(printerName: string, label: HTMLCanvasElement): Promise<void> {
  if (!PRINTING_ENABLED) {
    console.info(
      `[dev] print skipped — would have sent a ${label.width}×${label.height} label to ${printerName}`,
    );
    return;
  }
  if (!inTauriShell())
    throw new Error("Printing is only available inside the Globeweigh Touch app.");

  const rotated = document.createElement("canvas");
  rotated.width = label.height;
  rotated.height = label.width;
  const ctx = rotated.getContext("2d")!;
  ctx.translate(rotated.width, 0);
  ctx.rotate(Math.PI / 2);
  ctx.drawImage(label, 0, 0);

  const img = ctx.getImageData(0, 0, rotated.width, rotated.height);
  const px = img.data;
  // RGBA → BGRA, which is what a Windows DIB expects.
  for (let i = 0; i < px.length; i += 4) {
    const r = px[i];
    px[i] = px[i + 2];
    px[i + 2] = r;
  }
  let binary = "";
  const CHUNK = 0x8000;
  for (let i = 0; i < px.length; i += CHUNK) {
    binary += String.fromCharCode(...px.subarray(i, Math.min(i + CHUNK, px.length)));
  }

  const { invoke } = await import("@tauri-apps/api/core");
  await invoke("print_image", {
    printer: printerName,
    width: rotated.width,
    height: rotated.height,
    bgraBase64: btoa(binary),
  });
}

// Stored on this PC's Device row via deviceStore — localStorage was wiped by reinstalls.
export function getSavedPrinter(): string | null {
  return getStoredValue("printerName");
}

export function savePrinter(name: string | null) {
  setStoredValue("printerName", name);
}
