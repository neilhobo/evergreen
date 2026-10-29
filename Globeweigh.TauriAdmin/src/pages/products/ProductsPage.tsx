import { useState } from "react";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import FinishedProductsPanel from "./FinishedProductsPanel";
import PosProductsPanel from "./PosProductsPanel";
import RawMaterialsPanel from "./RawMaterialsPanel";
import { AdminUserDto } from "../../types";
import { BESPOKE_PRODUCT_TYPE_ID, FINISHED_PRODUCT_TYPE_ID, getProducts, POS_PRODUCT_TYPE_ID, Product } from "../../api/products";
import ProductDetailPanel from "./ProductDetailPanel";

// Products master — one panel per product type (Finished Products / Point Of Sale /
// Raw Materials / Bespoke), each with its own columns and sections. The type is picked
// from a dropdown at the left of the panel's toolbar and always opens on Finished Products.
type TypeKey = "finished" | "pos" | "raw" | "bespoke";

interface TypeRow {
  key: TypeKey;
  label: string;
}

const PRODUCT_TYPES: TypeRow[] = [
  { key: "finished", label: "Finished Products" },
  { key: "pos", label: "Point Of Sale" },
  { key: "raw", label: "Raw Materials" },
  { key: "bespoke", label: "Bespoke" },
];

export default function ProductsPage(_props: { user: AdminUserDto }) {
  const [typeKey, setTypeKey] = useState<TypeKey>("finished");
  const typeLabel = PRODUCT_TYPES.find((t) => t.key === typeKey)?.label ?? "";
  // The Product detail panel (costing: zone + customer prices). Rendered here, not by the list,
  // because it flies in over the whole card — header included (see DetailPanel.module.css).
  const [viewing, setViewing] = useState<Product | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  // The product was edited from the panel: refetch the list behind it and the panel's own row.
  async function onProductSaved() {
    setReloadToken((t) => t + 1);
    if (!viewing) return;
    try {
      const rows = await getProducts(FINISHED_PRODUCT_TYPE_ID);
      const fresh = rows.find((r) => r.id === viewing.id);
      if (fresh) setViewing(fresh);
    } catch {
      // The panel keeps the row it had; the list's own refetch reports any failure.
    }
  }

  const typePicker = (
    <select
      className="form-input toolbar-select"
      aria-label="Product Type"
      value={typeKey}
      onChange={(e) => setTypeKey(e.target.value as TypeKey)}
    >
      {PRODUCT_TYPES.map((t) => (
        <option key={t.key} value={t.key}>
          {t.label}
        </option>
      ))}
    </select>
  );

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title="Products" meta={typeLabel} />
        <div className={cc.body}>
          {typeKey === "finished" ? (
            <FinishedProductsPanel typePicker={typePicker} onView={setViewing} reloadToken={reloadToken} />
          ) : typeKey === "pos" ? (
            <PosProductsPanel
              key="pos"
              productTypeId={POS_PRODUCT_TYPE_ID}
              searchLabel="point of sale"
              typePicker={typePicker}
            />
          ) : typeKey === "bespoke" ? (
            <PosProductsPanel
              key="bespoke"
              productTypeId={BESPOKE_PRODUCT_TYPE_ID}
              searchLabel="bespoke"
              typePicker={typePicker}
            />
          ) : (
            <RawMaterialsPanel typePicker={typePicker} />
          )}
        </div>
      </div>
      {viewing && (
        <ProductDetailPanel
          key={viewing.id}
          product={viewing}
          onClose={() => setViewing(null)}
          onProductSaved={() => void onProductSaved()}
        />
      )}
    </div>
  );
}
