// Renders the PRODUCTION reprint label (15 × 10 cm landscape) to a canvas at printer
// resolution (203 dpi → 1199 × 799). Forked from the Manual Stock master label
// (labelCanvas.ts) so the production label can evolve independently — change THIS file
// for production reprints, not labelCanvas.ts.
// Layout: greyscale logo header (no date, no "Pallet x of y" — Codico pallets have no batch
// sequence), the whole description centred on one big line ("60L Multipurpose Compost 80pp"),
// the DataMatrix centred below it at 3 cm square, order number bottom-right.
import bwipjs from "bwip-js/browser";
import { productionLabelPayload, productionLabelTitle } from "../components/touch/ProductionLabelPreview";

export interface ProductionLabelRenderData {
  product: string;
  orderNumber: string | null;
  netWeightKg: number;
  size: number | null;
  upp: number | null;
  gtin: string;
  palletCode: string;
}

const DPI = 203;
const dots = (cm: number) => Math.round((cm / 2.54) * DPI);
const W = dots(15);
const H = dots(10);

const loadImage = (src: string) =>
  new Promise<HTMLImageElement>((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error(`Could not load ${src}`));
    img.src = src;
  });

export async function renderProductionLabelCanvas(d: ProductionLabelRenderData): Promise<HTMLCanvasElement> {
  const canvas = document.createElement("canvas");
  canvas.width = W;
  canvas.height = H;
  const ctx = canvas.getContext("2d")!;
  ctx.fillStyle = "#ffffff";
  ctx.fillRect(0, 0, W, H);
  ctx.fillStyle = "#111111";

  const mx = dots(0.6);
  const my = dots(0.5);
  const logoH = dots(1.7);

  // Header: greyscale logo left.
  try {
    const logo = await loadImage("images/EvergreenLogo.png");
    const logoW = Math.round((logo.width / logo.height) * logoH);
    ctx.filter = "grayscale(1)";
    ctx.drawImage(logo, mx, my, logoW, logoH);
    ctx.filter = "none";
  } catch {
    // no logo asset — label still prints, just without it
  }

  // Header rule.
  const ruleY = my + logoH + 14;
  ctx.fillRect(mx, ruleY, W - 2 * mx, 6);

  // The whole description on one big centred line ("60L Multipurpose Compost 80pp"), shrunk
  // to fit the margins — no date, net weight, size, volume, customer or origin line.
  const baseY = ruleY + 96;
  const title = productionLabelTitle(d);
  // 63px here = the preview's 30px scaled to print resolution (×2.11).
  let titlePx = 63;
  ctx.font = `700 ${titlePx}px Arial`;
  while (titlePx > 34 && ctx.measureText(title).width > W - 2 * mx) {
    titlePx -= 2;
    ctx.font = `700 ${titlePx}px Arial`;
  }
  ctx.textAlign = "center";
  ctx.fillText(title, W / 2, baseY);
  ctx.textAlign = "left";

  // DataMatrix centred below the description, 3 cm square — the pallet's ORIGINAL payload,
  // so the reprint scans through the conveyor exactly like the label it replaces.
  const bcSize = dots(3);
  try {
    const bc = document.createElement("canvas");
    bwipjs.toCanvas(bc, { bcid: "datamatrix", text: productionLabelPayload(d), scale: 4 });
    ctx.imageSmoothingEnabled = false;
    const bcTop = baseY + Math.round((H - my - baseY - bcSize) / 2);
    ctx.drawImage(bc, Math.round((W - bcSize) / 2), bcTop, bcSize, bcSize);
    ctx.imageSmoothingEnabled = true;
  } catch {
    // bad payload — leave the space blank
  }
  if (d.orderNumber) {
    ctx.font = "600 30px Arial";
    ctx.textAlign = "right";
    ctx.fillText(d.orderNumber, W - mx, H - my - 4);
    ctx.textAlign = "left";
  }

  return canvas;
}
