import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import {
  createProduct,
  updateProduct,
  getProducts,
  ProductInput,
  Product,
  ReweighedOrder,
  FINISHED_PRODUCT_TYPE_ID,
  POS_PRODUCT_TYPE_ID,
  SOLD_BY_OPTIONS,
  SoldBy,
} from "../../api/products";
import DataTable, { Column } from "../../components/DataTable";
import { fmtDate, fmtQty2 } from "../../format";
import { getReferenceData, ReferenceDataItem } from "../../api/referenceData";
import styles from "../../widgets/DetailPanel.module.css";
import { cx } from "../../widgets/DetailPanel";
import "../dispatch/Dispatch.css";

interface Props {
  existing: Product | null; // null = new
  productTypeId: number; // fixed by the panel the modal was opened from
  onClose: (saved: boolean) => void;
}

// eRefDataType.FinishedProductCategory — source of the "Category Type" dropdown.
const FINISHED_PRODUCT_CATEGORY_REF_TYPE = 6;

// The empty pallet (kg) every product carries for now — Product.PalletWeight's column default.
const DEFAULT_PALLET_WEIGHT = 25;

// Add/edit a product — mirrors the WPF AddEditProductView: Description/Code/GTIN
// for every type, plus Category Type / Pos Product and the Pallet / Bag Setup for Finished
// Products only. The product type is fixed by the list the dialog was opened from.
export default function ProductEditModal({ existing, productTypeId, onClose }: Props) {
  const isNew = existing === null;
  const isFinished = productTypeId === FINISHED_PRODUCT_TYPE_ID;
  const [form, setForm] = useState<ProductInput>({
    gtin: existing?.gtin ?? "",
    description: existing?.description ?? "",
    skuCode: existing?.skuCode ?? "",
    size: existing?.size ?? null,
    upp: existing?.upp ?? 1,
    commodityCode: existing?.commodityCode ?? "",
    productTypeId,
    categoryId: existing?.categoryId ?? 0,
    posId: existing?.posId ?? null,
    isBulkBag: existing?.isBulkBag ?? false,
    averageWeight: existing?.averageWeight ?? null,
    bagWeightGrams: existing?.bagWeightGrams ?? null,
    soldBy: existing?.soldBy ?? "Pallet",
  });
  const [categories, setCategories] = useState<ReferenceDataItem[]>([]);
  const [posProducts, setPosProducts] = useState<Product[]>([]);
  const [busy, setBusy] = useState(false);
  const [reweighed, setReweighed] = useState<ReweighedOrder[] | null>(null);
  const isCubicMeter = form.soldBy === "CubicMeter";

  useEffect(() => {
    if (!isFinished) return;
    (async () => {
      try {
        const [cats, pos] = await Promise.all([
          getReferenceData(FINISHED_PRODUCT_CATEGORY_REF_TYPE),
          getProducts(POS_PRODUCT_TYPE_ID),
        ]);
        setCategories(cats.filter((c) => !c.dateDeleted));
        setPosProducts(pos);
      } catch (err) {
        void messageDialog("Failed to load lookups.\n" + (err as Error).message);
      }
    })();
  }, [isFinished]);

  function set<K extends keyof ProductInput>(key: K, value: ProductInput[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  // Descriptions need not be unique: the same product can exist under several barcodes
  // (e.g. two "Decorative Chip Bark"), so there is no duplicate-name check.
  const trimmedName = form.description?.trim() ?? "";

  const canSave = !!trimmedName && (!isFinished || form.categoryId !== 0);

  async function onSave() {
    if (!canSave) return;
    setBusy(true);
    try {
      // Non-finished types never carry category/size/UPP/POS (mirrors the WPF reset).
      const input: ProductInput = isFinished
        ? form
        : { ...form, categoryId: 0, size: null, upp: 0, posId: null, isBulkBag: false, averageWeight: null, bagWeightGrams: null, soldBy: "Pallet" };
      if (isNew) await createProduct(input);
      else {
        const res = await updateProduct(existing!.id, input);
        // 0 kg picks of this product just took the Average Weight: say which orders before
        // closing, since dispatched ones may have had a delivery note / Sage post already.
        if (res.reweighedOrders.length > 0) {
          setReweighed(res.reweighedOrders);
          return;
        }
      }
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  if (reweighed) {
    const pallets = reweighed.reduce((n, o) => n + o.pallets, 0);
    const columns: Column<ReweighedOrder>[] = [
      { key: "orderNo", header: "Order", fit: true, render: (o) => o.orderNo },
      { key: "customerName", header: "Customer" },
      { key: "pallets", header: "Pallets", fit: true, align: "right", render: (o) => o.pallets },
      {
        key: "dateDispatched",
        header: "Status",
        fit: true,
        render: (o) => (o.dateDispatched ? `Dispatched ${fmtDate(o.dateDispatched)}` : "Open"),
      },
    ];
    return (
      <Modal
        title="Picked Pallets Updated"
        width={560}
        onClose={() => onClose(true)}
        footer={
          <button className="btn-blue" autoFocus onClick={() => onClose(true)}>
            OK
          </button>
        }
      >
        <p style={{ margin: "0 0 10px" }}>
          {pallets} picked {pallets === 1 ? "pallet" : "pallets"} of {form.description} on{" "}
          {reweighed.length} {reweighed.length === 1 ? "order was" : "orders were"} at 0 kg and now{" "}
          {pallets === 1 ? "carries" : "carry"} the Average Weight of {fmtQty2(form.averageWeight)} kg (marked
          as estimated).
        </p>
        <DataTable columns={columns} rows={reweighed} rowKey={(o) => o.orderId} maxHeight={320} />
      </Modal>
    );
  }

  // Finished products get the Pallet / Bag Setup column on the right; the other types only
  // carry the details, so their dialog stays one column wide.
  const detailRows = (
    <>
      <label className={cx(styles.field, "ld-row")}>
        <span className={styles.fieldLabel}>Description *</span>
        <input
          className="ld-input"
          value={form.description ?? ""}
          autoFocus
          onChange={(e) => set("description", e.target.value)}
        />
      </label>
      <label className={cx(styles.field, "ld-row")}>
        <span className={styles.fieldLabel}>Code</span>
        <input className="ld-input" value={form.skuCode ?? ""} onChange={(e) => set("skuCode", e.target.value)} />
      </label>
      <label className={cx(styles.field, "ld-row")}>
        <span className={styles.fieldLabel}>GTIN</span>
        <input className="ld-input" value={form.gtin ?? ""} onChange={(e) => set("gtin", e.target.value)} />
      </label>
      <label className={cx(styles.field, "ld-row")}>
        <span className={styles.fieldLabel}>Commodity Code</span>
        <input
          className="ld-input"
          value={form.commodityCode ?? ""}
          onChange={(e) => set("commodityCode", e.target.value)}
        />
      </label>
      {isFinished && (
        <>
          <label className={cx(styles.field, "ld-row")}>
            <span className={styles.fieldLabel}>Category Type *</span>
            <select
              className="ld-input"
              value={form.categoryId}
              onChange={(e) => set("categoryId", Number(e.target.value))}
            >
              <option value={0}>— select —</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.displayName}
                </option>
              ))}
            </select>
          </label>
          <label className={cx(styles.field, "ld-row")}>
            <span className={styles.fieldLabel}>Pos Product</span>
            <select
              className="ld-input"
              value={form.posId ?? ""}
              onChange={(e) => set("posId", e.target.value === "" ? null : Number(e.target.value))}
            >
              <option value="">— None —</option>
              {posProducts.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.description}
                </option>
              ))}
            </select>
          </label>
          <label className={cx(styles.field, "ld-row")}>
            <span className={styles.fieldLabel}>Sold By</span>
            <select
              className="ld-input"
              value={form.soldBy}
              onChange={(e) => {
                const soldBy = e.target.value as SoldBy;
                // Cubic meter products carry no Average Weight, so switching to it clears the figure.
                setForm((f) => ({ ...f, soldBy, averageWeight: soldBy === "CubicMeter" ? null : f.averageWeight }));
              }}
            >
              {SOLD_BY_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </label>
        </>
      )}
    </>
  );

  return (
    <Modal
      title={isNew ? "Add New Product" : "Edit Product"}
      width={isFinished ? 800 : 520}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || !canSave} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      {/* The Add/Edit Order dialog's white panel (Dispatch.css ld-*): the product's details down
          the left, its pallet and bag figures on the right. */}
      <div className="pe-form">
        <div className="ld-panel">
          <div className={isFinished ? "pe-body" : "pe-body pe-body-single"}>
            <section className={styles.section}>
              <h3 className={cx(styles.sectionTitle, existing?.dateDeleted && styles.sectionTitleRow)}>
                <span>Product Details</span>
                {existing?.dateDeleted && <span className={cx(styles.statusTag, styles.statusDeleted)}>Deleted</span>}
              </h3>
              {detailRows}
            </section>

            {isFinished && (
              <section className={styles.section}>
                <h3 className={styles.sectionTitle}>Pallet / Bag Setup</h3>
                <label className={cx(styles.field, "ld-row")}>
                  <span className={styles.fieldLabel}>Size</span>
                  <input
                    className="ld-input"
                    type="number"
                    value={form.size ?? ""}
                    onChange={(e) => set("size", e.target.value === "" ? null : Number(e.target.value))}
                  />
                </label>
                <label className={cx(styles.field, "ld-row")}>
                  <span className={styles.fieldLabel}>UPP</span>
                  <input
                    className="ld-input"
                    type="number"
                    value={form.upp}
                    onChange={(e) => set("upp", e.target.value === "" ? 0 : Number(e.target.value))}
                  />
                </label>
                <div className={cx(styles.field, "ld-row")}>
                  <span className={styles.fieldLabel}>Bulk Bag</span>
                  <label className="ld-check">
                    <input
                      type="checkbox"
                      checked={form.isBulkBag}
                      onChange={(e) => set("isBulkBag", e.target.checked)}
                    />
                    <span>{form.isBulkBag ? "Yes" : "No"}</span>
                  </label>
                </div>
                <label className={cx(styles.field, "ld-row")}>
                  <span
                    className={styles.fieldLabel}
                    title="kg per pallet. A no-scan pallet of a product with no weighed pallets is booked at this weight."
                  >
                    Average Weight (kg)
                  </span>
                  <input
                    className="ld-input"
                    type="number"
                    min={0}
                    step="0.01"
                    disabled={isCubicMeter}
                    placeholder={isCubicMeter ? "Not used (sold by cubic meter)" : undefined}
                    value={isCubicMeter ? "" : (form.averageWeight ?? "")}
                    onChange={(e) => set("averageWeight", e.target.value === "" ? null : Number(e.target.value))}
                  />
                </label>
                {/* Fixed at 25 kg for every product for now — shown, not edited. */}
                <div className={cx(styles.field, "ld-row")}>
                  <span className={styles.fieldLabel}>Pallet Weight (kg)</span>
                  <span className="ld-static">{fmtQty2(existing?.palletWeight ?? DEFAULT_PALLET_WEIGHT)}</span>
                </div>
                <label className={cx(styles.field, "ld-row")}>
                  <span className={styles.fieldLabel}>Bag Weight (g)</span>
                  <input
                    className="ld-input"
                    type="number"
                    min={0}
                    step="0.01"
                    value={form.bagWeightGrams ?? ""}
                    onChange={(e) => set("bagWeightGrams", e.target.value === "" ? null : Number(e.target.value))}
                  />
                </label>
              </section>
            )}
          </div>
        </div>
      </div>
    </Modal>
  );
}
