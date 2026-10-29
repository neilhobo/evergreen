import { useEffect, useRef } from "react";
import bwipjs from "bwip-js/browser";

// The master customer pallet label template (15 × 10 cm landscape) — the fields
// match the label TauriTouch prints from the Manual Pallet weigh popup. Every
// customer uses this template; per-customer configuration will layer on top of it
// from the customer setup screen.
export interface PalletLabelData {
  product: string;
  customerName: string;
  orderNumber: string;
  /** Rendered as "— kg" when null */
  netWeightKg: number | null;
  /** Rendered as "— m³" when null */
  volumeM3: number | null;
  palletSeq: number;
  palletCount: number;
  gtin: string;
  palletCode: string;
  /** Date the pallet was created, display-formatted as dd.MM.yyyy */
  productionDate: string;
}

// Representative data so the admin preview shows what an operator would print.
export const SAMPLE_LABEL_DATA: PalletLabelData = {
  product: "Professional Growing Medium",
  customerName: "Sample Customer Ltd",
  orderNumber: "PO-12345",
  netWeightKg: 1159.75,
  volumeM3: 4.29,
  palletSeq: 1,
  palletCount: 26,
  gtin: "769503877594",
  palletCode: "21720265028",
  productionDate: "21.07.2026",
};

// DataMatrix in the SAME payload format the conveyor scanner reads off Codico
// pallets (no separators, " kg" after the weight), matching TauriTouch.
function DataMatrix({ text, sizeCm = 2.4 }: { text: string; sizeCm?: number }) {
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

// Renders at true print proportions; `scale` shrinks it to fit its container
// (the wrapper reserves the scaled footprint so surrounding layout stays tight).
// `logoUrl` overrides the default Evergreen logo (per-customer configuration).
export default function PalletLabel({
  data,
  scale = 1,
  logoUrl,
}: {
  data: PalletLabelData;
  scale?: number;
  logoUrl?: string | null;
}) {
  const weightText = data.netWeightKg === null ? "—" : `${data.netWeightKg.toFixed(2)} kg`;
  return (
    <div style={{ width: `calc(15cm * ${scale})`, height: `calc(10cm * ${scale})`, flex: "none" }}>
      <div className="pallet-label" style={{ transform: `scale(${scale})`, transformOrigin: "top left" }}>
        <div className="lbl-head">
          <img className="lbl-logo" src={logoUrl || "images/EvergreenLogo.png"} alt="Logo" />
          <span className="lbl-pallet-no">
            Pallet {data.palletSeq} of {data.palletCount}
          </span>
          <DataMatrix
            text={`ProdCd:${data.gtin}Weight:${(data.netWeightKg ?? 0).toFixed(2)} kgUniqueCode:${data.palletCode}`}
            sizeCm={1.7}
          />
        </div>
        <div className="lbl-body">
          <div className="lbl-product">{data.product}</div>
          <div className="lbl-mid">
            <div className="lbl-fields">
              <div className="lbl-field">
                <div className="k">Net Weight</div>
                <div className="v big">{weightText}</div>
              </div>
              <div className="lbl-field">
                <div className="k">Volume</div>
                <div className="v big">{data.volumeM3 === null ? "—" : `${data.volumeM3.toFixed(2)} m³`}</div>
              </div>
            </div>
            <div className="lbl-field lbl-customer">
              <div className="k">Customer</div>
              <div className="v big">{data.customerName}</div>
            </div>
          </div>
          <div className="lbl-footer">
            <div className="lbl-origin">Made in Ireland</div>
            {/* Order no / production date, unlabelled: "PO-12345 / 16.02.2026".
                No order no → just the date, no slash. */}
            <div className="lbl-order-date">
              {[data.orderNumber, data.productionDate].filter(Boolean).join(" / ")}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
