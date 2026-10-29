// Renders the master pallet label (15 × 10 cm landscape) to a canvas at printer
// resolution (203 dpi → 1199 × 799). This is what gets printed via the normal Windows
// driver path (print_image), so the paper matches the on-screen template exactly:
// greyscale logo + pallet no + DataMatrix header, product row, weights + customer
// columns, "Made in Ireland" / order + production date footer.
import bwipjs from "bwip-js/browser";

export interface PalletLabelRenderData {
  product: string;
  customerName: string | null;
  orderNumber: string | null;
  netWeightKg: number;
  volumeM3: number | null;
  palletSeq: number;
  palletCount: number;
  gtin: string;
  palletCode: string;
  /** dd.MM.yyyy */
  productionDate: string;
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

export async function renderPalletLabelCanvas(d: PalletLabelRenderData): Promise<HTMLCanvasElement> {
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

  // Header: DataMatrix top-right (same payload the conveyor scanner reads).
  const bcSize = dots(1.7);
  const payload = `ProdCd:${d.gtin}Weight:${d.netWeightKg.toFixed(2)} kgUniqueCode:${d.palletCode}`;
  try {
    const bc = document.createElement("canvas");
    bwipjs.toCanvas(bc, { bcid: "datamatrix", text: payload, scale: 4 });
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(bc, W - mx - bcSize, my, bcSize, bcSize);
    ctx.imageSmoothingEnabled = true;
  } catch {
    // bad payload — leave the corner blank
  }

  // Header: pallet no, right-aligned beside the barcode.
  ctx.font = "700 36px Arial";
  ctx.textAlign = "right";
  ctx.textBaseline = "middle";
  ctx.fillText(`Pallet ${d.palletSeq} of ${d.palletCount}`, W - mx - bcSize - 28, my + logoH / 2);
  ctx.textAlign = "left";
  ctx.textBaseline = "alphabetic";

  // Header rule.
  const ruleY = my + logoH + 14;
  ctx.fillRect(mx, ruleY, W - 2 * mx, 6);

  // Product name, full width.
  ctx.font = "700 54px Arial";
  ctx.fillText(d.product, mx, ruleY + 80);

  const key = (t: string, x: number, y: number) => {
    ctx.font = "600 24px Arial";
    ctx.fillStyle = "#444444";
    ctx.fillText(t.toUpperCase(), x, y);
    ctx.fillStyle = "#111111";
  };
  const val = (t: string, x: number, y: number, size = 62) => {
    ctx.font = `700 ${size}px Arial`;
    ctx.fillText(t, x, y);
  };

  // Column 1: weights. Column 2: customer.
  key("Net Weight", mx, ruleY + 150);
  val(`${d.netWeightKg.toFixed(2)} kg`, mx, ruleY + 216);
  key("Volume", mx, ruleY + 300);
  val(d.volumeM3 === null ? "—" : `${d.volumeM3.toFixed(2)} m³`, mx, ruleY + 366);
  const cx = Math.round(W * 0.55);
  key("Customer", cx, ruleY + 150);
  val(d.customerName || "—", cx, ruleY + 208, 46);

  // Footer.
  ctx.font = "700 33px Arial";
  ctx.fillText("Made in Ireland", mx, H - my - 4);
  ctx.font = "600 30px Arial";
  ctx.textAlign = "right";
  ctx.fillText([d.orderNumber, d.productionDate].filter(Boolean).join(" / "), W - mx, H - my - 4);
  ctx.textAlign = "left";

  return canvas;
}
