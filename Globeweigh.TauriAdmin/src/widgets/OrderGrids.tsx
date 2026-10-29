import { priceUnitsPer } from "../api/products";
import { ReactNode, useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import styles from "./DetailPanel.module.css";
import { cx } from "./DetailPanel";
import AuditTrailBar from "./AuditTrailBar";
import DataTable, { Column } from "../components/DataTable";
import { AUDIT_ENTITY } from "../api/audit";
import { DispatchOrderItem, PickedPallet, isTapPickedLine } from "../api/dispatch";
import { fmtDateTime, fmtQty } from "../format";
import { useRowMenu } from "../pages/dispatch/useRowMenu";

interface Props {
  // One order's lines, and the pallets picked onto that same order.
  items: DispatchOrderItem[];
  picked: PickedPallet[];
  // Unit price by product id, for the Price / Total columns. Leave it out where prices are not
  // loaded (or not wanted) and those two columns stay blank.
  priceByProduct?: Record<number, number | null>;
  loading?: boolean;
  failed?: boolean;
  // The order the lines belong to. A different order is a different set of lines, so the selected
  // line is cleared when it changes.
  orderId?: number;
  // Whether that order's audit trail runs as a strip along the bottom of the lines. Off where the
  // panel gives the trail a place of its own (the load panel's right-hand column).
  auditStrip?: boolean;
  // Anything extra for the lines column's header row (a Delivery Note button, say).
  itemsAction?: ReactNode;
  // Given, the pallets grid gets a "⋮" column with Unpick. Left out where the order can no longer
  // change (dispatched) or the viewer may not change it (Sales My Orders).
  onUnpick?: (pallet: PickedPallet) => void;
}

// A line still owed: a pallet line picked to a different count, or a tap-picked line (POS board,
// cubic meter peat) not yet picked.
const lineShort = (r: DispatchOrderItem) =>
  isTapPickedLine(r) ? (r.pickedQuantity ?? 0) === 0 : (r.pickedQuantity ?? 0) !== (r.quantity ?? 0);

// An order's lines beside the pallets picked against them. Selecting a line narrows the pallets to
// that line's, which is the question the office actually asks of a short pick: "which pallets DID
// go on for this product?" Side by side rather than stacked because the right grid ANSWERS the
// left one, and nobody should have to look up and down to make that connection.
export default function OrderGrids({
  items,
  picked,
  priceByProduct,
  loading,
  failed,
  orderId,
  auditStrip,
  itemsAction,
  onUnpick,
}: Props) {
  const [palletMenu, palletMenuApi] = useRowMenu<PickedPallet>();
  // The line whose pallets are showing. Held as an id and looked up rather than kept as an object,
  // so a reload that drops the line falls back to every pallet instead of filtering to one that is
  // no longer there.
  const [selectedItemId, setSelectedItemId] = useState<number | null>(null);
  // A different order is a different set of lines; the selection cannot survive it.
  useEffect(() => setSelectedItemId(null), [orderId]);
  const selectedLine = items.find((i) => i.id === selectedItemId) ?? null;
  const filterProductId = selectedLine?.productId ?? null;

  // Newest pallet last: picking reads forwards, and the last one on is the one somebody is most
  // likely to be asking about.
  const pickedInOrder = useMemo(
    () =>
      [...picked].sort(
        (a, b) => (a.dateCreated ?? "").localeCompare(b.dateCreated ?? "") || a.id - b.id,
      ),
    [picked],
  );

  // A pallet carries the product it was picked as, not a line id — and an edit reconciles lines BY
  // product, so one product is one line and matching on it gives the same answer.
  const shownPallets = useMemo(
    () =>
      filterProductId == null
        ? pickedInOrder
        : pickedInOrder.filter((p) => p.productId === filterProductId),
    [pickedInOrder, filterProductId],
  );

  const prices = priceByProduct ?? {};
  const goodsTotal = useMemo(
    () =>
      items.reduce((sum, i) => {
        const p = prices[i.productId];
        return sum + (i.quantity != null && p != null ? i.quantity * priceUnitsPer(i.soldBy, i.upp) * p : 0);
      }, 0),
    [items, prices],
  );
  const anyPriced = items.some((i) => prices[i.productId] != null);

  const itemColumns = useMemo<Column<DispatchOrderItem>[]>(
    () => [
      {
        key: "productDescription",
        header: "Product",
        sortValue: (r) => r.productDescription,
        render: (r) => (
          <span title={r.productDescription ?? undefined}>
            {r.productDescription}
            {/* The density the picker entered when marking a peat line picked. */}
            {r.soldBy === "CubicMeter" && r.density != null && (
              <span className={styles.muted} style={{ marginLeft: 6 }} title="Density entered by the picker">
                {r.density.toFixed(2)} kg/m³
              </span>
            )}
            {/* Picked, then the line was removed by an edit — the pallets never shipped. */}
            {r.isUnordered && (
              <span
                className={styles.estTag}
                style={{ marginLeft: 6 }}
                title="This line was removed by an edit after pallets had been picked onto it."
              >
                Off order
              </span>
            )}
          </span>
        ),
      },
      { key: "size", header: "Size", width: 46, align: "right", render: (r) => r.size },
      {
        key: "quantity",
        header: "Ordered",
        width: 62,
        align: "right",
        // A peat line's quantity is m³ and the POS board is just "1", so neither joins the pallet
        // footer; and a tap-picked line is short only while it is still unpicked.
        render: (r) => {
          const text = r.soldBy === "CubicMeter" ? `${fmtQty(r.quantity ?? 0)} m³` : r.quantity;
          return lineShort(r) ? (
            <span className={styles.bad} style={{ fontWeight: 600 }}>{text}</span>
          ) : (
            text
          );
        },
        footer: (rows) =>
          fmtQty(rows.filter((r) => !isTapPickedLine(r)).reduce((s, r) => s + (r.quantity ?? 0), 0)),
      },
      {
        key: "pickedQuantity",
        header: "Picked",
        width: 64,
        align: "right",
        render: (r) =>
          isTapPickedLine(r) ? (
            (r.pickedQuantity ?? 0) > 0 ? (
              <span
                className={styles.pickedTag}
                title={r.pickedAt ? `Picked ${fmtDateTime(r.pickedAt)}` : "Picked in the Picking app"}
              >
                Picked
              </span>
            ) : (
              <span className={styles.notPickedTag}>Not picked</span>
            )
          ) : lineShort(r) ? (
            <span className={styles.bad} style={{ fontWeight: 600 }}>{r.pickedQuantity ?? 0}</span>
          ) : (
            r.pickedQuantity
          ),
        footer: (rows) =>
          fmtQty(rows.filter((r) => !isTapPickedLine(r)).reduce((s, r) => s + (r.pickedQuantity ?? 0), 0)),
      },
      {
        key: "pickedWeight",
        header: "Weight (kg)",
        width: 78,
        align: "right",
        render: (r) => fmtQty(r.pickedWeight),
        footer: (rows) => fmtQty(rows.reduce((s, r) => s + r.pickedWeight, 0)),
      },
      {
        key: "price",
        header: "Price",
        width: 54,
        align: "right",
        render: (r) => {
          const p = prices[r.productId];
          return p != null ? p.toFixed(2) : "";
        },
      },
      {
        key: "total",
        header: "Total",
        width: 70,
        align: "right",
        render: (r) => {
          const p = prices[r.productId];
          return r.quantity != null && p != null ? (r.quantity * priceUnitsPer(r.soldBy, r.upp) * p).toFixed(2) : "";
        },
        footer: () => (anyPriced ? goodsTotal.toFixed(2) : ""),
      },
    ],
    [prices, goodsTotal, anyPriced],
  );

  // The pallets grid. The product name is what a reader scans down, so it is the ONLY flexible
  // column and every other one best-fits: a pallet code, a weight and a timestamp are all
  // fixed-length, and pinning them to round numbers of pixels would only take width off the one
  // column that needs it.
  //
  // The product column stays even when the grid is filtered to a single line, where every row
  // repeats it. Dropping it there would reshuffle the columns each time the filter goes on and
  // off, and a grid that keeps still is worth more than the width it would save.
  const palletColumns = useMemo<Column<PickedPallet>[]>(
    () => [
      {
        key: "productDescription",
        header: "Product",
        sortValue: (r) => r.productDescription,
        render: (r) =>
          r.productDescription ? (
            <span title={r.productDescription}>{r.productDescription}</span>
          ) : (
            <span className={styles.muted}>—</span>
          ),
      },
      {
        key: "palletId",
        header: "Pallet",
        fit: true,
        sortValue: (r) => r.palletId,
        render: (r) => (
          <>
            {/* A cubic meter (peat) line's single pick: tapped picked, no pallet behind it. */}
            {r.palletId ? (
              <span className={styles.code}>{r.palletId}</span>
            ) : r.isNoScan ? (
              <span className={styles.code}>—</span>
            ) : (
              <span className={styles.pickedTag} title="Marked picked in the Picking app — nothing scanned">
                Tapped picked
              </span>
            )}
            {/* Put on the order by hand — the scanner never read this one. */}
            {r.isNoScan && (
              <span
                className={styles.estTag}
                style={{ marginLeft: 6 }}
                title="Added by hand because the scanner never read this pallet."
              >
                No scan
              </span>
            )}
          </>
        ),
      },
      {
        key: "weight",
        header: "Weight (kg)",
        fit: true,
        align: "right",
        render: (r) => (
          <>
            {fmtQty(r.weight)}
            {/* No measured weight for this pallet — the API's estimate from the product's other pallets. */}
            {r.isWeightEstimated && (
              <span
                className={styles.estTag}
                style={{ marginLeft: 6 }}
                title="Estimated: this pallet had no measured weight, so the median of the product's other pallets in stock is used."
              >
                Est.
              </span>
            )}
          </>
        ),
        footer: (rows) => fmtQty(rows.reduce((s, r) => s + r.weight, 0)),
      },
      {
        key: "dateCreated",
        header: "Picked",
        fit: true,
        render: (r) => fmtDateTime(r.dateCreated),
      },
      ...(onUnpick
        ? [
            {
              // Far-right "⋮" — opens the per-pallet actions menu (see palletMenu).
              key: "menu",
              header: "",
              fit: true,
              className: "col-menu-tight",
              align: "center",
              noSort: true,
              render: (r) => (
                <button
                  type="button"
                  className="dsp-row-menu-btn"
                  title="More actions"
                  aria-haspopup="menu"
                  aria-expanded={palletMenu?.row.id === r.id}
                  onMouseDown={(e) => e.stopPropagation()}
                  onClick={(e) => {
                    e.stopPropagation();
                    palletMenuApi.open(r, e.currentTarget);
                  }}
                >
                  ⋮
                </button>
              ),
            } satisfies Column<PickedPallet>,
          ]
        : []),
    ],
    // palletMenuApi is a fresh object each render but only wraps stable setters and a ref.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [onUnpick, palletMenu?.row.id],
  );

  return (
    <div className={styles.gridsRow}>
      <section className={cx(styles.col, styles.itemsCol)}>
        <div className={styles.blockHead}>
          <h3 className={styles.blockTitle}>Order Items ({items.length})</h3>
          {itemsAction}
        </div>
        <DataTable
          columns={itemColumns}
          rows={items}
          rowKey={(r) => r.id}
          sortable
          selectedKey={selectedLine?.id ?? null}
          // Clicking the selected line again clears the filter, so the grid itself is a way back
          // to all the pallets as well as the button above them.
          onSelect={(r) => setSelectedItemId((prev) => (prev === r.id ? null : r.id))}
          emptyText={
            loading ? "Loading…" : failed ? "Could not load the order's items." : "No items on this order"
          }
        />
        {auditStrip && orderId != null && (
          <AuditTrailBar entityName={AUDIT_ENTITY.dispatchOrder} entityId={orderId} />
        )}
      </section>

      <section className={cx(styles.col, styles.palletsCol)}>
        <div className={styles.blockHead}>
          <h3 className={styles.blockTitle}>
            Picked Pallets ({shownPallets.length}
            {selectedLine ? ` of ${picked.length}` : ""})
          </h3>
          {selectedLine && (
            <button
              type="button"
              className={styles.showAll}
              onClick={() => setSelectedItemId(null)}
              title="Show every pallet picked onto this order"
            >
              Show all
            </button>
          )}
        </div>
        <DataTable
          columns={palletColumns}
          rows={shownPallets}
          rowKey={(r) => r.id}
          sortable
          emptyText={
            loading
              ? "Loading…"
              : selectedLine
                ? "No pallets were picked against this line"
                : "Nothing has been picked onto this order"
          }
        />
      </section>

      {/* At page level: the panel slides in on a transform, which would carry a fixed menu with it. */}
      {palletMenu &&
        onUnpick &&
        createPortal(
          <div
            ref={palletMenuApi.ref}
            className="dsp-row-menu"
            role="menu"
            style={{ top: palletMenu.top, right: palletMenu.right }}
          >
            <button
              type="button"
              role="menuitem"
              className="dsp-row-menu-item danger"
              onClick={() => {
                palletMenuApi.close();
                onUnpick(palletMenu.row);
              }}
            >
              Unpick
            </button>
          </div>,
          document.body,
        )}
    </div>
  );
}
