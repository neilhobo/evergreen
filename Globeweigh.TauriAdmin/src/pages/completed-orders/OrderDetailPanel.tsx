import { priceUnitsPer } from "../../api/products";
import { useEffect, useMemo, useState } from "react";
import styles from "../../widgets/DetailPanel.module.css";
import { BackButton, Field, cx, useEscape } from "../../widgets/DetailPanel";
import OrderGrids from "../../widgets/OrderGrids";
import DeliveryNoteModal from "./DeliveryNoteModal";
import { fmtDate, fmtDateTime, fmtQty } from "../../format";
import {
  DispatchOrder,
  DispatchOrderItem,
  OrderEditDetail,
  PickedPallet,
  getOrderEditItems,
  getOrderItems,
  getOrderPicked,
  isTapPickedLine,
} from "../../api/dispatch";

interface Props {
  order: DispatchOrder;
  onClose: () => void;
  // Opens the load the order went out on. The load panel flies in over this one, so Back there
  // lands here again.
  onViewLoad?: (loadId: number) => void;
  // True while something (the load panel) sits over this panel; it has Escape, not us.
  covered?: boolean;
}

// Everything recorded for one order, flown in over the page: what the order WAS down the left,
// what was ordered, picked and changed on it down the right. Read-only throughout — an order is
// edited from the Edit dialog, not from here.
export default function OrderDetailPanel({ order, onClose, onViewLoad, covered = false }: Props) {
  const [items, setItems] = useState<DispatchOrderItem[]>([]);
  const [detail, setDetail] = useState<OrderEditDetail | null>(null);
  const [picked, setPicked] = useState<PickedPallet[]>([]);
  const [loadState, setLoadState] = useState<"loading" | "ready" | "failed">("loading");
  // Which printed document is open over the panel, if any.
  const [docOpen, setDocOpen] = useState<"note" | "invoice" | null>(null);

  // The lines, the header fields the grid row does not carry (comments, prices) and the individual
  // pallets, fetched here and only here — the list behind the panel has no use for any of them.
  useEffect(() => {
    let cancelled = false;
    setLoadState("loading");
    Promise.all([getOrderItems(order.id), getOrderEditItems(order.id), getOrderPicked(order.id)])
      .then(([its, det, pals]) => {
        if (cancelled) return;
        setItems(its);
        setDetail(det);
        setPicked(pals);
        setLoadState("ready");
      })
      .catch(() => {
        if (!cancelled) setLoadState("failed");
      });
    return () => {
      cancelled = true;
    };
  }, [order.id]);

  // The delivery note / invoice is an overlay of its own; it gets Escape while it is up.
  useEscape(onClose, !docOpen && !covered);

  const dispatched = order.dateDispatched != null;
  const deleted = order.dateDeleted != null;
  const status = deleted ? "Deleted" : dispatched ? "Dispatched" : "Open";

  // Ordered against picked. A short pick is the common one (the product ran out); an over-pick
  // means somebody put a pallet on the wrong order, and both want seeing.
  // Pallets only: the order view's totals count a peat line's m³ and its single tap-pick as pallets,
  // so once the lines are in, count the pallet lines; the POS board and peat get rows of their own.
  const palletLines = items.filter((i) => !isTapPickedLine(i));
  const linesReady = loadState === "ready";
  const orderedPallets = linesReady
    ? palletLines.reduce((s, i) => s + (i.quantity ?? 0), 0)
    : order.totalQuantityOrdered;
  const pickedPallets = linesReady
    ? palletLines.reduce((s, i) => s + (i.pickedQuantity ?? 0), 0)
    : order.pickedQuantity;
  const shortfall = pickedPallets - orderedPallets;
  const posLine = items.find((i) => i.soldBy === "Pos");
  const peatLines = items.filter((i) => i.soldBy === "CubicMeter");
  const pickTag = (i: DispatchOrderItem) =>
    (i.pickedQuantity ?? 0) > 0 ? (
      <span className={styles.pickedTag} title={i.pickedAt ? `Picked ${fmtDateTime(i.pickedAt)}` : undefined}>
        Picked{i.pickedAt ? ` ${fmtDateTime(i.pickedAt)}` : ""}
      </span>
    ) : (
      <span className={styles.notPickedTag}>Not picked</span>
    );
  const bags = items.reduce((sum, i) => sum + (i.totalUnits ?? 0), 0);

  // Price is per unit and quantity is pallets, so a line's value scales by the product's UPP.
  const priceByProduct = useMemo(() => {
    const map: Record<number, number | null> = {};
    detail?.items.forEach((i) => (map[i.productId] = i.price));
    return map;
  }, [detail]);

  const goodsTotal = useMemo(
    () =>
      items.reduce((sum, i) => {
        const p = priceByProduct[i.productId];
        return sum + (i.quantity != null && p != null ? i.quantity * priceUnitsPer(i.soldBy, i.upp) * p : 0);
      }, 0),
    [items, priceByProduct],
  );
  const anyPriced = items.some((i) => priceByProduct[i.productId] != null);

  return (
    <div className={styles.panel} role="dialog" aria-label="Order details">
      <div className={styles.header}>
        <BackButton onClick={onClose} />
        <span className={styles.headerTitle}>
          {status} Order — {order.customerName}
        </span>
        <span className={styles.headerRef}>Order #{order.orderNo}</span>
      </div>

      <div className={styles.content}>
        {/* ── Left: what the order WAS ── */}
        <aside className={styles.leftPanel}>
          <section className={styles.section}>
            <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
              <span>Order Details</span>
              {/* Where the order has got to is the first thing it is read by, so it rides the
                  heading rather than taking a field row of its own. */}
              <span
                className={cx(
                  styles.statusTag,
                  deleted ? styles.statusDeleted : dispatched && styles.statusDispatched,
                )}
              >
                {status}
              </span>
            </h3>
            <Field
              label="Order No"
              value={
                <>
                  {order.orderNo}
                  {order.weighbridgeRecordId > 0 && (
                    <span className={styles.refNote}>
                      (Load #
                      {onViewLoad ? (
                        <button
                          type="button"
                          className={styles.refLink}
                          title="Open the load"
                          onClick={() => onViewLoad(order.weighbridgeRecordId)}
                        >
                          {order.weighbridgeRecordId}
                        </button>
                      ) : (
                        order.weighbridgeRecordId
                      )}
                      )
                    </span>
                  )}
                </>
              }
            />
            <Field label="Customer" value={order.customerName} />
            <Field label="Address" value={<span className={styles.prose}>{order.addressFull}</span>} />
            <Field label="PO Number" value={order.poNumber} />
            <Field label="Haulier" value={order.haulierName} />
            <Field label="Vehicle Reg" value={order.vehicleReg} />
            <Field label="Trailer No" value={order.trailerNo} />
            <Field
              label="Delivery"
              value={order.deliveryDate ? fmtDate(order.deliveryDate) : "ASAP"}
            />
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Quantities</h3>
            <Field label="Lines" value={loadState === "loading" ? "…" : items.length} />
            <Field label="Ordered" value={`${fmtQty(orderedPallets)} pallets`} />
            <Field label="Picked" value={<strong>{fmtQty(pickedPallets)} pallets</strong>} />
            {shortfall !== 0 && (
              <Field
                label={shortfall < 0 ? "Short By" : "Over By"}
                value={<span className={styles.warn}>{fmtQty(Math.abs(shortfall))} pallets</span>}
              />
            )}
            {peatLines.map((i) => (
              <Field
                key={i.id}
                label={`${fmtQty(i.quantity ?? 0)} m³`}
                value={
                  <>
                    {pickTag(i)}
                    {i.density != null && (
                      <span className={styles.muted} style={{ marginLeft: 6 }}>
                        {i.density.toFixed(2)} kg/m³
                      </span>
                    )}
                  </>
                }
              />
            ))}
            {posLine && <Field label="POS Board" value={pickTag(posLine)} />}
            <Field label="Bags" value={loadState === "ready" ? fmtQty(bags) : "…"} />
            <Field
              label="Picked Weight"
              value={order.pickedWeight > 0 ? `${fmtQty(order.pickedWeight)} kg` : null}
            />
            {anyPriced && <Field label="Goods Total" value={<strong>{goodsTotal.toFixed(2)}</strong>} />}
          </section>

          {/* The two comment fields. Shown whether or not anything was typed, so an order with no
              instructions says so rather than hiding the section. */}
          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Comments</h3>
            <Field
              label="Delivery Note"
              value={
                loadState === "ready" ? (
                  <span className={styles.prose}>{detail?.comments}</span>
                ) : loadState === "failed" ? null : (
                  "…"
                )
              }
            />
            <Field
              label="Picking"
              value={
                loadState === "ready" ? (
                  <span className={styles.prose}>{detail?.pickingComments}</span>
                ) : loadState === "failed" ? null : (
                  "…"
                )
              }
            />
            {/* The paper that travels with the load, or the copy the office files. */}
            <button type="button" className={styles.docketBtn} onClick={() => setDocOpen("note")}>
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                <path d="M6 9V2h12v7M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" />
                <path d="M6 14h12v8H6z" />
              </svg>
              Delivery Note
            </button>
            {/* Same page template as the delivery note, priced — invoice no is LoadNo-OrderNo. */}
            <button type="button" className={styles.docketBtn} onClick={() => setDocOpen("invoice")}>
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                <path d="M14 2v6h6M8 13h8M8 17h8M8 9h2" />
              </svg>
              Commercial Invoice
            </button>
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Times</h3>
            <Field label="Created" value={fmtDateTime(order.dateCreated)} />
            <Field label="Created By" value={order.createdBy} />
            <Field label="Dispatched" value={fmtDateTime(order.dateDispatched)} />
            {deleted && (
              <Field
                label="Deleted"
                value={<span className={styles.bad}>{fmtDateTime(order.dateDeleted)}</span>}
              />
            )}
          </section>
        </aside>

        {/* ── Right: what was ordered, what was picked onto it, and what has been changed ──
            The same white panel as the left, so the two sit level with matching edges; the
            grids part it in two (see .rightPanel / OrderGrids). */}
        <div className={styles.rightPanel}>
          <OrderGrids
            items={items}
            picked={picked}
            priceByProduct={priceByProduct}
            loading={loadState === "loading"}
            failed={loadState === "failed"}
            orderId={order.id}
            auditStrip
          />
        </div>
      </div>

      {docOpen && (
        <DeliveryNoteModal orderId={order.id} kind={docOpen} onClose={() => setDocOpen(null)} />
      )}
    </div>
  );
}
