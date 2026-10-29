import { useEffect, useRef } from "react";
import bwipjs from "bwip-js/browser";

// DataMatrix in the SAME payload format the conveyor scanner reads off Codico pallets
// (verified against the live ScannerService log — no separators, " kg" after the weight):
//   ProdCd:769503877594Weight:1159.75 kgUniqueCode:21720265028
// so a label printed from this template scans through GlobeweighService/BarcodeWorker
// exactly like a fed one.
export function DataMatrix({ text, sizeCm = 2.4 }: { text: string; sizeCm?: number }) {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    if (!ref.current) return;
    try {
      bwipjs.toCanvas(ref.current, { bcid: "datamatrix", text, scale: 4 });
    } catch {
      // bad payload (e.g. empty) — leave the canvas blank
    }
  }, [text]);
  return <canvas ref={ref} style={{ width: `${sizeCm}cm`, height: `${sizeCm}cm` }} />;
}

export interface PalletLabelPreviewData {
  product: string;
  customerName: string | null;
  orderNumber: string | null;
  /** Null while a live weigh hasn't produced a reading yet — shows "—". */
  netWeightKg: number | null;
  weightDecimals: number;
  volumeM3: number | null;
  palletSeq: number;
  palletCount: number;
  gtin: string;
  palletCode: string;
  /** dd.MM.yyyy */
  productionDate: string;
}

// On-screen label preview at print proportions — the Manual Stock master label template
// (15 × 10 cm landscape). Must stay in lockstep with renderPalletLabelCanvas (labelCanvas.ts),
// which is what actually prints. Production reprints use their own fork (ProductionLabelPreview).
export default function PalletLabelPreview({ d }: { d: PalletLabelPreviewData }) {
  return (
    <div className="tt-label-preview">
      <div className="lbl-head">
        <img className="lbl-logo" src="images/EvergreenLogo.png" alt="Evergreen Horticulture" />
        <span className="lbl-pallet-no">Pallet {d.palletSeq} of {d.palletCount}</span>
        <DataMatrix
          text={`ProdCd:${d.gtin}Weight:${(d.netWeightKg ?? 0).toFixed(2)} kgUniqueCode:${d.palletCode}`}
          sizeCm={1.7}
        />
      </div>
      <div className="lbl-body">
        <div className="lbl-product">{d.product}</div>
        <div className="lbl-mid">
          <div className="lbl-fields">
            <div className="lbl-field">
              <div className="k">Net Weight</div>
              <div className="v big">
                {d.netWeightKg === null ? "—" : `${d.netWeightKg.toFixed(d.weightDecimals)} kg`}
              </div>
            </div>
            <div className="lbl-field">
              <div className="k">Volume</div>
              <div className="v big">{d.volumeM3 === null ? "—" : `${d.volumeM3.toFixed(2)} m³`}</div>
            </div>
          </div>
          <div className="lbl-field">
            <div className="k">Customer</div>
            <div className="v big">{d.customerName || "—"}</div>
          </div>
        </div>
        <div className="lbl-footer">
          <div className="lbl-origin">Made in Ireland</div>
          <div className="lbl-order-date">
            {[d.orderNumber, d.productionDate].filter(Boolean).join(" / ")}
          </div>
        </div>
      </div>
    </div>
  );
}
