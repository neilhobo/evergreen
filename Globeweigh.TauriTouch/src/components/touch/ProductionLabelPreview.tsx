import { DataMatrix } from "./PalletLabelPreview";

// On-screen preview of the PRODUCTION reprint label at print proportions (15 × 10 cm).
// Forked from the Manual Stock master template (PalletLabelPreview) so it can evolve
// independently — change THIS component (and renderProductionLabelCanvas, which prints it)
// for production reprints, not the Manual Stock pair.
// The extra lbl-prod class scopes any production-only CSS tweaks.
export interface ProductionLabelData {
  product: string;
  orderNumber: string | null;
  netWeightKg: number;
  size: number | null;
  upp: number | null;
  gtin: string;
  palletCode: string;
}

/**
 * The whole printed description in one line: size in front, UPP appended —
 * "60L Multipurpose Compost 80pp". Neither is printed as its own field.
 */
export function productionLabelTitle(d: { product: string; size: number | null; upp: number | null }): string {
  const head = d.size === null ? d.product : `${d.size}L ${d.product}`;
  return d.upp === null ? head : `${head} ${d.upp}pp`;
}

/** The pallet's ORIGINAL scanner payload — a reprint must scan like the label it replaces. */
export const productionLabelPayload = (d: ProductionLabelData) =>
  `ProdCd:${d.gtin}Weight:${d.netWeightKg.toFixed(2)} kgUniqueCode:${d.palletCode}`;

export default function ProductionLabelPreview({ d }: { d: ProductionLabelData }) {
  return (
    <div className="tt-label-preview lbl-prod">
      <div className="lbl-head">
        <img className="lbl-logo" src="images/EvergreenLogo.png" alt="Evergreen Horticulture" />
      </div>
      <div className="lbl-body">
        {/* Big centred description — no date, weight, size field or "Made in Ireland". */}
        <div className="lbl-product">{productionLabelTitle(d)}</div>
        {/* DataMatrix centred below it, 3 cm square. */}
        <div className="lbl-code">
          <DataMatrix text={productionLabelPayload(d)} sizeCm={3} />
        </div>
        {d.orderNumber && (
          <div className="lbl-footer">
            <span className="lbl-order-date">{d.orderNumber}</span>
          </div>
        )}
      </div>
    </div>
  );
}
