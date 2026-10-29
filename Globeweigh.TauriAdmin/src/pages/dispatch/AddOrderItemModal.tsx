import { useEffect, useMemo, useState } from "react";
import Modal from "../../components/Modal";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import {
  BESPOKE_PRODUCT_TYPE_ID,
  FINISHED_PRODUCT_TYPE_ID,
  getProducts,
  getProductTypes,
  priceUnitsOf,
  priceUnitsPer,
  Product,
  ProductTypeRef,
} from "../../api/products";
import { getPricesForCustomer, getZonePrices } from "../../api/costing";
import { getStockSummary, StockSummaryItem } from "../../api/stock";
import { ORDER_DIALOG_WIDTH, ORDER_DIALOG_HEIGHT } from "./orderDialogSize";

export interface OrderItemDraft {
  productId: number;
  quantity: number | null;
  price: number | null;
}

interface Props {
  // The order's customer: a product they have a price of their own for (Products > Costing)
  // offers that price as an alternative to the zone price.
  customerId: number;
  // No In Stock column (a sales rep's order from My Orders): the stock is not fetched either.
  hideStock?: boolean;
  costingZoneId: number;
  costingZoneName: string | null;
  alreadySelectedProductIds: number[];
  existing: OrderItemDraft | null; // null = add
  onAdd: (item: OrderItemDraft) => void;
  onClose: () => void;
}

// Mirrors the WPF AddEditDispatchOrderItemView popup: Product Type list (left),
// searchable products grid with the customer's zone price (middle), and a
// Quantity/Price panel (right). Price defaults from the customer's costing zone.
export default function AddOrderItemModal({
  customerId,
  hideStock = false,
  costingZoneId,
  costingZoneName,
  alreadySelectedProductIds,
  existing,
  onAdd,
  onClose,
}: Props) {
  const [productTypes, setProductTypes] = useState<ProductTypeRef[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [priceMap, setPriceMap] = useState<Record<number, number>>({});
  // The customer's own price per product, where they have one.
  const [customerPriceMap, setCustomerPriceMap] = useState<Record<number, number>>({});
  // Pallets in stock per product (current stock summary, all locations).
  const [stockMap, setStockMap] = useState<Record<number, number>>({});
  const [typeId, setTypeId] = useState(0); // 0 = all
  const [search, setSearch] = useState("");
  const [sizeSearch, setSizeSearch] = useState("");
  const [selected, setSelected] = useState<Product | null>(null);
  const [quantity, setQuantity] = useState<number | null>(existing?.quantity ?? null);
  const [price, setPrice] = useState<number | null>(existing?.price ?? null);
  const [loading, setLoading] = useState(true);
  // Add mode: items queued on this screen (right panel) and committed together on Save.
  const [toAdd, setToAdd] = useState<OrderItemDraft[]>([]);

  const added = useMemo(
    () => new Set([...alreadySelectedProductIds, ...toAdd.map((i) => i.productId)]),
    [alreadySelectedProductIds, toAdd],
  );

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [types, prods, zonePrices, stock, customerPrices] = await Promise.all([
          getProductTypes(),
          getProducts(),
          costingZoneId > 0 ? getZonePrices(costingZoneId) : Promise.resolve([]),
          // Stock is a nice-to-have here — a failed summary shouldn't block adding items.
          hideStock ? Promise.resolve([] as StockSummaryItem[]) : getStockSummary().catch(() => [] as StockSummaryItem[]),
          // So are customer prices: without them every product simply prices from the zone.
          customerId > 0 ? getPricesForCustomer(customerId).catch(() => []) : Promise.resolve([]),
        ]);
        if (cancelled) return;
        // Finished Products first, Bespoke second, then the rest in the API's (alphabetical) order.
        const tabRank = (id: number) =>
          id === FINISHED_PRODUCT_TYPE_ID ? 0 : id === BESPOKE_PRODUCT_TYPE_ID ? 1 : 2;
        setProductTypes([...types].sort((a, b) => tabRank(a.id) - tabRank(b.id)));
        setProducts(prods);
        const counts: Record<number, number> = {};
        stock.forEach((s) => {
          if (s.productId != null) counts[s.productId] = (counts[s.productId] ?? 0) + s.palletCount;
        });
        setStockMap(counts);
        const map: Record<number, number> = {};
        zonePrices.forEach((z) => (map[z.productId] = z.price));
        setPriceMap(map);
        const own: Record<number, number> = {};
        customerPrices.forEach((c) => (own[c.productId] = c.price));
        setCustomerPriceMap(own);
        if (existing) {
          const p = prods.find((x) => x.id === existing.productId) ?? null;
          setSelected(p);
          if (p) setTypeId(p.productTypeId);
        } else {
          // Default the type filter to Finished Product.
          const finished = types.find((t) => /finished/i.test(t.typeDescription ?? ""));
          if (finished) setTypeId(finished.id);
        }
      } catch (err) {
        if (!cancelled) void messageDialog("Failed to load products.\n" + (err as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [customerId, costingZoneId, existing, hideStock]);

  function selectProduct(p: Product) {
    if (added.has(p.id) && existing?.productId !== p.id) return; // already on the order
    setSelected(p);
    if (!existing || existing.productId !== p.id) {
      setPrice(priceMap[p.id] ?? null);
      setQuantity((q) => q ?? null);
    }
  }

  // Queue the current selection into the right-hand list and reset for the next pick.
  function onAddToList() {
    if (!selected) return;
    if (quantity == null || quantity <= 0) {
      void messageDialog("Enter a quantity.");
      return;
    }
    if (price == null) {
      void messageDialog("Enter a price.");
      return;
    }
    setToAdd((list) => [...list, { productId: selected.id, quantity, price }]);
    setSelected(null);
    setQuantity(null);
    setPrice(null);
  }

  function removeFromList(idx: number) {
    setToAdd((list) => list.filter((_, i) => i !== idx));
  }

  function onSave() {
    if (existing) {
      // Edit mode: a single item is being changed.
      if (!selected) {
        void messageDialog("Select a product.");
        return;
      }
      if (quantity == null || quantity <= 0) {
        void messageDialog("Enter a quantity.");
        return;
      }
      onAdd({ productId: selected.id, quantity, price });
      onClose();
      return;
    }
    // Add mode: commit the queued list, plus a valid not-yet-queued selection.
    const items = [...toAdd];
    if (selected && quantity != null && quantity > 0 && !added.has(selected.id)) {
      if (price == null) {
        void messageDialog("Enter a price.");
        return;
      }
      items.push({ productId: selected.id, quantity, price });
    }
    if (items.length === 0) {
      void messageDialog("Add at least one item.");
      return;
    }
    items.forEach(onAdd);
    onClose();
  }

  const term = search.trim().toLowerCase();
  const sizeTerm = sizeSearch.trim();
  const filtered = useMemo(() => {
    return products.filter(
      (p) =>
        (typeId === 0 || p.productTypeId === typeId) &&
        (!term ||
          [p.description, p.skuCode, p.gtin].some((v) => v?.toLowerCase().includes(term))) &&
        (!sizeTerm || (p.size != null && String(p.size).startsWith(sizeTerm))),
    );
  }, [products, typeId, term, sizeTerm]);

  const columns: Column<Product>[] = [
    ...(hideStock
      ? []
      : [
          {
            key: "stock",
            header: "In Stock",
            fit: true,
            align: "right" as const,
            className: "col-tint-amber",
            // Cubic meter products (raw peat) carry no stock: a dash, not a misleading 0.
            render: (p: Product) => (p.soldBy === "CubicMeter" ? "—" : (stockMap[p.id] ?? 0)),
            sortValue: (p: Product) => stockMap[p.id] ?? 0,
          },
        ]),
    {
      key: "description",
      header: "Product",
      render: (p) => (
        <span style={added.has(p.id) ? { color: "#16a34a" } : undefined}>
          <Highlight text={p.description} term={term} />
          {added.has(p.id) ? " ✓" : ""}
        </span>
      ),
      filter: { value: search, onChange: setSearch },
    },
    { key: "size", header: "Size", width: 55, align: "right", filter: { value: sizeSearch, onChange: setSizeSearch } },
    { key: "upp", header: "UPP", width: 55, align: "right" },
    { key: "hasPos", header: "POS", width: 50, align: "center", render: (p) => (p.hasPos ? "✓" : "") },
    {
      key: "price",
      header: "Price",
      width: 80,
      align: "right",
      // The zone price; a product the customer has a price of their own for carries a blue
      // marker, and its choice is offered in the Order Item card when it is picked.
      render: (p) => (
        <>
          {customerPriceMap[p.id] != null && (
            <span
              className="cust-price-mark"
              title={`Customer price ${customerPriceMap[p.id].toFixed(2)}`}
            >
              ●
            </span>
          )}
          {priceMap[p.id] != null ? priceMap[p.id].toFixed(2) : ""}
        </>
      ),
    },
  ];

  // Price is per unit with quantity in pallets (scaled by UPP), or per m³ for a cubic meter product.
  const total =
    quantity != null && price != null && selected != null
      ? (quantity * priceUnitsPer(selected.soldBy, selected.upp) * price).toFixed(2)
      : "";

  return (
    <Modal
      title={`${existing ? "Edit" : "Add"} Order Item`}
      width={ORDER_DIALOG_WIDTH}
      height={ORDER_DIALOG_HEIGHT}
      onClose={onClose}
      footer={
        <>
          <button className="toolbar-btn" onClick={onClose}>
            Cancel
          </button>
          <button
            className="btn-blue"
            disabled={
              existing
                ? !selected || quantity == null
                : toAdd.length === 0 && (!selected || quantity == null)
            }
            onClick={onSave}
          >
            Save
          </button>
        </>
      }
    >
      {loading ? (
        <div className="data-table-empty">Loading…</div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", height: "100%", minHeight: 0, minWidth: 0, overflowX: "hidden" }}>
          {/* Product types span the full dialog width, above both panes */}
          <div style={{ display: "flex", flexWrap: "wrap", gap: 4, marginBottom: 8 }}>
            {productTypes.map((t) => (
              <div
                key={t.id}
                className={`stock-type-item${typeId === t.id ? " active" : ""}`}
                onClick={() => setTypeId(t.id)}
              >
                {t.typeDescription}
              </div>
            ))}
          </div>
          <div style={{ borderBottom: "1px solid var(--border)", marginBottom: 10 }} />

          <div style={{ display: "grid", gridTemplateColumns: "minmax(0, 1fr) 140px minmax(0, 1fr)", gap: 10, flex: 1, minHeight: 0, minWidth: 0 }}>
          {/* Left: products grid */}
          {/* -fit (auto layout) only while the best-fit In Stock column is there. */}
          <div className={`dsp-prod-grid${hideStock ? "" : " dsp-prod-grid-fit"}`} style={{ display: "flex", flexDirection: "column", minWidth: 0, minHeight: 0 }}>
            <DataTable
              columns={columns}
              rows={filtered}
              rowKey={(p) => p.id}
              selectedKey={selected?.id ?? null}
              onSelect={selectProduct}
              onDoubleClick={(p) => {
                selectProduct(p);
              }}
              emptyText="No products"
            />
          </div>

          {/* Middle: quantity + price card for the selected product */}
          <div
            style={{
              minWidth: 0,
              alignSelf: "start",
              background: "#fff",
              border: "1px solid var(--border)",
              borderRadius: 6,
              padding: 12,
              display: "flex",
              flexDirection: "column",
              gap: 10,
            }}
          >
            <h3 className="po-side-title" style={{ height: "auto", margin: 0 }}>
              Order Item
            </h3>
            <div style={{ borderBottom: "1px solid var(--border)" }} />
            {!selected ? (
              <p style={{ color: "var(--text-secondary)", fontSize: 12, margin: 0 }}>Select a product.</p>
            ) : added.has(selected.id) && existing?.productId !== selected.id ? (
              <p style={{ color: "#16a34a", fontSize: 12, margin: 0 }}>Already on this order.</p>
            ) : (
              <>
                <div className="form-field">
                  <label className="form-label">
                    {selected.soldBy === "CubicMeter" ? "Quantity (m³)" : "Quantity (pallets)"}
                  </label>
                  <input
                    className="form-input"
                    type="number"
                    style={{ width: "100%", textAlign: "left" }}
                    value={quantity ?? ""}
                    onChange={(e) => setQuantity(e.target.value === "" ? null : Number(e.target.value))}
                  />
                </div>
                {customerPriceMap[selected.id] != null && (
                  // The customer has a price of their own for this product: pick it or keep the
                  // zone price. The zone price stays the default; typing a price of your own
                  // below leaves neither ticked.
                  <div className="form-field">
                    <label className="form-label">Price From</label>
                    <label className="stock-radio cust-price-option">
                      <input
                        type="radio"
                        name="price-source"
                        checked={price === priceMap[selected.id] && price !== customerPriceMap[selected.id]}
                        disabled={priceMap[selected.id] == null}
                        onChange={() => setPrice(priceMap[selected.id] ?? null)}
                      />
                      <span>Zone</span>
                      <strong>{priceMap[selected.id] != null ? priceMap[selected.id].toFixed(2) : "—"}</strong>
                    </label>
                    <label className="stock-radio cust-price-option">
                      <input
                        type="radio"
                        name="price-source"
                        checked={price === customerPriceMap[selected.id]}
                        onChange={() => setPrice(customerPriceMap[selected.id])}
                      />
                      <span>Customer</span>
                      <strong className="cust-price-value">{customerPriceMap[selected.id].toFixed(2)}</strong>
                    </label>
                  </div>
                )}
                <div className="form-field">
                  <label className="form-label">Price</label>
                  <input
                    className="form-input"
                    type="number"
                    step="0.01"
                    style={{ width: "100%", textAlign: "left" }}
                    value={price ?? ""}
                    onChange={(e) => setPrice(e.target.value === "" ? null : Number(e.target.value))}
                  />
                </div>
                <div className="form-field">
                  <label className="form-label">Costing Zone</label>
                  <span className="form-static">{costingZoneName ?? "—"}</span>
                </div>
                <div style={{ borderBottom: "1px solid var(--border)" }} />
                <div style={{ display: "flex", alignItems: "baseline", justifyContent: "space-between" }}>
                  <span className="form-label" style={{ marginBottom: 0 }}>
                    Total
                  </span>
                  <span style={{ fontSize: 15, fontWeight: 600, color: "var(--text-heading)" }}>
                    {total || "—"}
                  </span>
                </div>
                {!existing && (
                  <button
                    className="btn-primary"
                    style={{ width: "100%" }}
                    disabled={quantity == null || quantity <= 0 || price == null}
                    onClick={onAddToList}
                  >
                    Add →
                  </button>
                )}
              </>
            )}
          </div>

          {/* Far right: items queued on this screen, committed together on Save */}
          <div
            className="po-side"
            style={{ width: "100%", minWidth: 0, overflow: "hidden", display: "flex", flexDirection: "column" }}
          >
            {!existing && (
              <>
                <h3 className="dsp-panel-title" style={{ marginTop: 0 }}>
                  Items to Add
                </h3>
                <DataTable
                  columns={
                    [
                      {
                        key: "product",
                        header: "Product",
                        render: (i: OrderItemDraft) =>
                          products.find((x) => x.id === i.productId)?.description ?? `#${i.productId}`,
                      },
                      {
                        key: "size",
                        header: "Size",
                        width: 55,
                        align: "right",
                        render: (i: OrderItemDraft) => products.find((p) => p.id === i.productId)?.size ?? "",
                      },
                      { key: "quantity", header: "Quantity", width: 90, align: "right" },
                      {
                        key: "price",
                        header: "Price",
                        width: 80,
                        align: "right",
                        render: (i: OrderItemDraft) => (i.price != null ? i.price.toFixed(2) : ""),
                      },
                      {
                        key: "total",
                        header: "Total",
                        width: 90,
                        align: "right",
                        // Price is per unit with quantity in pallets (scaled by UPP), or per m³ for a cubic meter product.
                        render: (i: OrderItemDraft) =>
                          i.quantity != null && i.price != null
                            ? (i.quantity * priceUnitsOf(products.find((p) => p.id === i.productId)) * i.price).toFixed(2)
                            : "",
                      },
                      {
                        key: "actions",
                        header: " ",
                        // Fixed, not fit: .po-side grids are table-layout fixed, which crushes
                        // a fit column to 1% and hid the Remove button.
                        width: 70,
                        align: "center",
                        render: (i: OrderItemDraft) => (
                          <button
                            className="link-btn"
                            onClick={(e) => {
                              e.stopPropagation();
                              removeFromList(toAdd.indexOf(i));
                            }}
                          >
                            Remove
                          </button>
                        ),
                      },
                    ] as Column<OrderItemDraft>[]
                  }
                  rows={toAdd}
                  rowKey={(i) => i.productId}
                  emptyText="No items yet — pick a product, enter a quantity and click Add."
                />
              </>
            )}
          </div>
          </div>
        </div>
      )}
    </Modal>
  );
}
