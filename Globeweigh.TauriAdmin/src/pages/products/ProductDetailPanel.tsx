import { useCallback, useEffect, useMemo, useState } from "react";
import styles from "../../widgets/DetailPanel.module.css";
import { BackButton, Field, cx, useEscape } from "../../widgets/DetailPanel";
import DataTable, { Column } from "../../components/DataTable";
import Modal from "../../components/Modal";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  CostingZonePrice,
  CustomerPrice,
  CustomerPriceHistory,
  deleteCustomerPrice,
  getCostingProducts,
  getCustomerPriceHistory,
  getCustomerPrices,
  getProductZones,
} from "../../api/costing";
import { CostingZoneRow, currencyLabel, getCostingZones } from "../../api/costingZones";
import { Product, soldByLabel } from "../../api/products";
import { fmtDate, fmtDateTime, fmtQty2 } from "../../format";
import ProductZonePricesModal from "../costing/ProductZonePricesModal";
import CustomerPriceModal from "./CustomerPriceModal";
import ProductEditModal from "./ProductEditModal";
import { fmtMoney, priceDiff } from "./pricing";
import ps from "./ProductDetail.module.css";

interface Props {
  product: Product;
  onClose: () => void;
  // The product was edited from the panel — the list behind refetches and hands the fresh row back.
  onProductSaved: () => void;
}

// One zone with this product's price in it (null = the product has no price row in that zone).
interface ZoneLine {
  zone: CostingZoneRow;
  price: CostingZonePrice | null;
  customerCount: number;
}

// Everything a product costs, flown in over the Products page: the product down the left, its
// price in every costing zone and the customers who have a price of their own side by side on
// the right. Zone prices are edited with the Costing page's dialog; customer prices are added,
// changed and removed here.
export default function ProductDetailPanel({ product, onClose, onProductSaved }: Props) {
  const [zones, setZones] = useState<CostingZoneRow[]>([]);
  const [zonePrices, setZonePrices] = useState<CostingZonePrice[]>([]);
  const [customerPrices, setCustomerPrices] = useState<CustomerPrice[]>([]);
  const [loadState, setLoadState] = useState<"loading" | "ready" | "failed">("loading");
  const [selected, setSelected] = useState<CustomerPrice | null>(null);
  // Which dialog is up over the panel, if any.
  const [dialog, setDialog] = useState<
    | { kind: "customer"; existing: CustomerPrice | null }
    | { kind: "zones"; products: Product[] }
    | { kind: "history" }
    | { kind: "edit" }
    | null
  >(null);

  const load = useCallback(async () => {
    try {
      const [zs, zp, cp] = await Promise.all([
        getCostingZones(),
        getProductZones(product.id),
        getCustomerPrices(product.id),
      ]);
      setZones(zs.filter((z) => !z.dateDeleted));
      setZonePrices(zp);
      setCustomerPrices(cp);
      setSelected((cur) => (cur ? cp.find((c) => c.id === cur.id) ?? null : null));
      setLoadState("ready");
    } catch {
      setLoadState("failed");
    }
  }, [product.id]);

  useEffect(() => {
    setLoadState("loading");
    setSelected(null);
    void load();
  }, [load]);

  useEscape(onClose, dialog === null);

  const priceByZone = useMemo(() => {
    const m = new Map<number, CostingZonePrice>();
    zonePrices.forEach((p) => m.set(p.costingZoneId, p));
    return m;
  }, [zonePrices]);

  const zoneLines: ZoneLine[] = useMemo(
    () =>
      zones.map((zone) => ({
        zone,
        price: priceByZone.get(zone.id) ?? null,
        customerCount: customerPrices.filter((c) => c.costingZoneId === zone.id).length,
      })),
    [zones, priceByZone, customerPrices],
  );

  const priced = zoneLines.filter((z) => z.price && !z.price.na).length;
  const naCount = zoneLines.filter((z) => z.price?.na).length;
  const unpriced = zoneLines.length - priced - naCount;
  const taken = useMemo(() => new Set(customerPrices.map((c) => c.customerSupplierId)), [customerPrices]);

  async function openZoneEditor() {
    try {
      setDialog({ kind: "zones", products: await getCostingProducts() });
    } catch (err) {
      void messageDialog("Failed to load products.\n" + (err as Error).message);
    }
  }

  async function onRemove() {
    if (!selected) return;
    const ok = await confirmDialog(
      `Remove ${selected.customerName ?? "this customer"}'s price for "${product.description}"?\n\n` +
        "They go back to their costing zone's price.",
    );
    if (!ok) return;
    try {
      await deleteCustomerPrice(selected.id);
      await load();
    } catch (err) {
      void messageDialog("Failed to remove.\n" + (err as Error).message);
    }
  }

  const zoneColumns: Column<ZoneLine>[] = [
    {
      key: "zone",
      header: "Costing Zone",
      render: (r) => (
        <>
          {r.zone.description}
          {r.zone.secondaryDescription && <span className={ps.sub}>{r.zone.secondaryDescription}</span>}
        </>
      ),
      sortValue: (r) => r.zone.description,
    },
    { key: "currency", header: "Currency", width: 80, render: (r) => currencyLabel(r.zone.isEuro), sortValue: (r) => currencyLabel(r.zone.isEuro) },
    {
      key: "price",
      header: "Price",
      width: 90,
      align: "right",
      render: (r) =>
        !r.price ? (
          <span className={styles.muted}>—</span>
        ) : r.price.na ? (
          <span className={styles.muted}>N/A</span>
        ) : (
          <span className={ps.strong}>{fmtMoney(r.price.price, r.zone.isEuro)}</span>
        ),
      sortValue: (r) => (r.price && !r.price.na ? r.price.price : null),
    },
    {
      // How many customers in the zone pay their own price instead of this one.
      key: "customers",
      header: "Own Prices",
      width: 80,
      align: "right",
      render: (r) => (r.customerCount > 0 ? r.customerCount : ""),
      sortValue: (r) => r.customerCount,
    },
  ];

  const customerColumns: Column<CustomerPrice>[] = [
    {
      key: "customerName",
      header: "Customer",
      render: (r) => (
        <>
          {r.customerName}
          {r.customerCode && <span className={ps.sub}>{r.customerCode}</span>}
          {r.customerDeleted && <span className={cx(ps.sub, styles.bad)}>deleted</span>}
        </>
      ),
    },
    { key: "costingZone", header: "Zone", width: 120, render: (r) => r.costingZone ?? <span className={styles.muted}>None</span> },
    {
      key: "zonePrice",
      header: "Zone Price",
      width: 82,
      align: "right",
      render: (r) => {
        const z = priceByZone.get(r.costingZoneId);
        return z ? (z.na ? "N/A" : fmtMoney(z.price, r.isEuro)) : <span className={styles.muted}>—</span>;
      },
      sortValue: (r) => priceByZone.get(r.costingZoneId)?.price ?? null,
    },
    {
      key: "price",
      header: "Their Price",
      width: 86,
      align: "right",
      className: "col-tint-orange",
      render: (r) => <span className={ps.strong}>{fmtMoney(r.price, r.isEuro)}</span>,
    },
    {
      key: "diff",
      header: "vs Zone",
      width: 118,
      align: "right",
      render: (r) => {
        const z = priceByZone.get(r.costingZoneId);
        if (!z || z.na) return "";
        const d = priceDiff(r.price, z.price, r.isEuro);
        return <span className={d.className}>{d.text}</span>;
      },
      sortValue: (r) => {
        const z = priceByZone.get(r.costingZoneId);
        return z && !z.na ? r.price - z.price : null;
      },
    },
    {
      key: "updated",
      header: "Updated",
      width: 84,
      render: (r) => (
        <span title={`${r.modifiedBy ?? r.createdBy ?? ""} — ${fmtDateTime(r.dateModified ?? r.dateCreated)}`}>
          {fmtDate(r.dateModified ?? r.dateCreated)}
        </span>
      ),
      sortValue: (r) => r.dateModified ?? r.dateCreated,
    },
  ];

  const loadingOrFailed = loadState === "loading" ? "Loading…" : loadState === "failed" ? "Failed to load" : null;

  return (
    <div className={styles.panel} role="dialog" aria-label="Product details">
      <div className={styles.header}>
        <BackButton onClick={onClose} />
        <span className={styles.headerTitle}>Product — {product.description}</span>
        {product.skuCode && <span className={styles.headerRef}>SKU {product.skuCode}</span>}
      </div>

      <div className={styles.content}>
        {/* ── Left: the product ── */}
        <aside className={styles.leftPanel}>
          <section className={styles.section}>
            <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
              <span>Product Details</span>
              {product.dateDeleted && <span className={cx(styles.statusTag, styles.statusDeleted)}>Deleted</span>}
            </h3>
            <Field label="Description" value={product.description} />
            <Field label="SKU Code" value={product.skuCode} />
            <Field label="Barcode" value={product.gtin} />
            <Field label="Category" value={product.categoryName} />
            <Field label="LTRS" value={product.size} />
            <Field label="UPP" value={product.upp} />
            <Field label="Sold By" value={soldByLabel(product.soldBy)} />
            <Field label="Bulk Bag" value={product.isBulkBag ? "Yes" : "No"} />
            <Field label="Commodity Code" value={product.commodityCode} />
            <Field label="POS" value={product.posDescription} />
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Weights</h3>
            <Field
              label="Average Weight"
              value={product.averageWeight ? `${fmtQty2(product.averageWeight)} kg` : null}
            />
            <Field
              label="Bag Weight"
              value={product.bagWeightGrams ? `${fmtQty2(product.bagWeightGrams)} g` : null}
            />
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Pricing</h3>
            <Field
              label="Zones Priced"
              value={loadState === "ready" ? `${priced} of ${zoneLines.length}` : "…"}
            />
            {naCount > 0 && <Field label="N/A In" value={`${naCount} zone${naCount === 1 ? "" : "s"}`} />}
            {loadState === "ready" && unpriced > 0 && (
              <Field label="No Price In" value={<span className={styles.warn}>{unpriced} zone{unpriced === 1 ? "" : "s"}</span>} />
            )}
            <Field
              label="Customer Prices"
              value={loadState === "ready" ? customerPrices.length : "…"}
            />
            <button type="button" className={styles.docketBtn} onClick={() => setDialog({ kind: "edit" })}>
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                <path d="M12 20h9" />
                <path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z" />
              </svg>
              Edit Product
            </button>
          </section>
        </aside>

        {/* ── Right: the zone prices beside the customers' own ── */}
        <div className={styles.rightPanel}>
          <div className={styles.gridsRow}>
            <div className={cx(styles.col, styles.palletsCol)} style={{ borderLeft: "none", flex: "1 1 0" }}>
              <div className={styles.blockHead}>
                <h3 className={styles.blockTitle}>Zone Prices</h3>
                <div className={ps.headActions}>
                  <button type="button" className={ps.headBtn} disabled={loadState !== "ready"} onClick={() => void openZoneEditor()}>
                    Edit Zone Prices
                  </button>
                </div>
              </div>
              <DataTable
                columns={zoneColumns}
                rows={zoneLines}
                rowKey={(r) => r.zone.id}
                sortable
                emptyText={loadingOrFailed ?? "No costing zones"}
              />
            </div>

            <div className={cx(styles.col, styles.palletsCol, ps.fixedGrid)} style={{ flex: "1.4 1 0" }}>
              <div className={styles.blockHead}>
                <h3 className={styles.blockTitle}>
                  Customer Pricing
                  {customerPrices.length > 0 && <span className={ps.count}>{customerPrices.length}</span>}
                </h3>
                <div className={ps.headActions}>
                  <button type="button" className={ps.headBtn} onClick={() => setDialog({ kind: "history" })}>
                    History
                  </button>
                  <button
                    type="button"
                    className={ps.headBtn}
                    disabled={!selected}
                    onClick={() => selected && setDialog({ kind: "customer", existing: selected })}
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    className={cx(ps.headBtn, ps.headBtnDanger)}
                    disabled={!selected}
                    onClick={() => void onRemove()}
                  >
                    Remove
                  </button>
                  <button
                    type="button"
                    className={cx(ps.headBtn, ps.headBtnPrimary)}
                    disabled={loadState !== "ready"}
                    onClick={() => setDialog({ kind: "customer", existing: null })}
                  >
                    + Add Customer Pricing
                  </button>
                </div>
              </div>
              {loadState === "ready" && customerPrices.length === 0 ? (
                <div className={ps.empty}>
                  <span>
                    <strong>No customer prices yet.</strong>
                    <br />
                    Every customer pays their costing zone's price for this product.
                  </span>
                  <button
                    type="button"
                    className={cx(ps.headBtn, ps.headBtnPrimary)}
                    onClick={() => setDialog({ kind: "customer", existing: null })}
                  >
                    + Add Customer Pricing
                  </button>
                </div>
              ) : (
                <DataTable
                  columns={customerColumns}
                  rows={customerPrices}
                  rowKey={(r) => r.id}
                  selectedKey={selected?.id ?? null}
                  onSelect={setSelected}
                  onDoubleClick={(r) => setDialog({ kind: "customer", existing: r })}
                  sortable
                  emptyText={loadingOrFailed ?? ""}
                />
              )}
            </div>
          </div>
        </div>
      </div>

      {dialog?.kind === "customer" && (
        <CustomerPriceModal
          product={product}
          existing={dialog.existing}
          zonePrices={priceByZone}
          taken={taken}
          onClose={(saved) => {
            setDialog(null);
            if (saved) void load();
          }}
        />
      )}
      {dialog?.kind === "zones" && (
        <ProductZonePricesModal
          product={product}
          zones={zones}
          existing={priceByZone}
          products={dialog.products}
          onClose={(saved) => {
            setDialog(null);
            if (saved) void load();
          }}
        />
      )}
      {dialog?.kind === "history" && (
        <CustomerPriceHistoryModal product={product} isEuroOf={(id) => customerPrices.find((c) => c.customerSupplierId === id)?.isEuro} onClose={() => setDialog(null)} />
      )}
      {dialog?.kind === "edit" && (
        <ProductEditModal
          existing={product}
          productTypeId={product.productTypeId}
          onClose={(saved) => {
            setDialog(null);
            if (saved) onProductSaved();
          }}
        />
      )}
    </div>
  );
}

// Every add, change and removal of a customer price for the product, newest first.
function CustomerPriceHistoryModal({
  product,
  isEuroOf,
  onClose,
}: {
  product: Product;
  isEuroOf: (customerId: number) => boolean | null | undefined;
  onClose: () => void;
}) {
  const [rows, setRows] = useState<CustomerPriceHistory[] | null>(null);

  useEffect(() => {
    getCustomerPriceHistory(product.id)
      .then(setRows)
      .catch((err) => {
        setRows([]);
        void messageDialog("Failed to load history.\n" + (err as Error).message);
      });
  }, [product.id]);

  const money = (n: number | null, id: number) => (n == null ? "" : fmtMoney(n, isEuroOf(id)));
  const columns: Column<CustomerPriceHistory>[] = [
    { key: "dateCreated", header: "When", width: 130, render: (r) => fmtDateTime(r.dateCreated) },
    { key: "customerName", header: "Customer", render: (r) => r.customerName },
    {
      key: "change",
      header: "Change",
      width: 170,
      render: (r) =>
        r.oldPrice == null
          ? `Added at ${money(r.newPrice, r.customerSupplierId)}`
          : r.newPrice == null
            ? `Removed (was ${money(r.oldPrice, r.customerSupplierId)})`
            : `${money(r.oldPrice, r.customerSupplierId)} → ${money(r.newPrice, r.customerSupplierId)}`,
    },
    { key: "changedBy", header: "By", width: 110, render: (r) => r.changedBy },
  ];

  return (
    <Modal
      title={`Customer Price History — ${product.description ?? ""}`}
      width={640}
      onClose={onClose}
      footer={
        <button className="toolbar-btn" onClick={onClose}>
          Close
        </button>
      }
    >
      <DataTable
        columns={columns}
        rows={rows ?? []}
        rowKey={(r) => r.id}
        maxHeight={420}
        emptyText={rows === null ? "Loading…" : "No customer price changes yet"}
      />
    </Modal>
  );
}
