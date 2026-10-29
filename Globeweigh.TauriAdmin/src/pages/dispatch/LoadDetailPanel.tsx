import { Fragment, ReactNode, useEffect, useMemo, useState } from "react";
import styles from "../../widgets/DetailPanel.module.css";
import { BackButton, Field, cx, useEscape } from "../../widgets/DetailPanel";
import OrderGrids from "../../widgets/OrderGrids";
import { confirmUnpick } from "../../widgets/unpick";
import { AuditEvent, AuditTrailColumn } from "../../widgets/AuditTrailBar";
import { AUDIT_ENTITY } from "../../api/audit";
import DeliveryNoteModal from "../completed-orders/DeliveryNoteModal";
import SendLoadEmailModal from "./SendLoadEmailModal";
import { messageDialog } from "../../components/confirmDialog";
import { getApiBaseUrl } from "../../config";
import { fmtDate, fmtDateTime, fmtQty } from "../../format";
import {
  LoadDetail,
  LoadPhoto,
  OrderEditDetail,
  PickedPallet,
  getLoadDetail,
  getOrderEditItems,
  loadPhotoUrl,
  swapLoadPhotos,
} from "../../api/dispatch";

interface Props {
  loadId: number;
  // Opened from the Loads grid (not yet in Pending): the orders are not picked yet, so the invoice button is the
  // Pre-Commercial Invoice — ordered pallets at the products' average weights.
  preCommercial?: boolean;
  // Offer ⋮ Unpick on the picked pallets (Dispatch page only — not Sales My Orders).
  allowUnpick?: boolean;
  onClose: () => void;
}

// A load flown in over the page, built the same way as the order panel: what the LOAD is down the
// left, and on the right one of its orders at a time.
//
// The split is the point. A lorry carries several orders, so "which order?" has to be asked before
// anything on the right means anything — hence the tab strip. But the weights, the times and above
// all the PHOTOS belong to the lorry, not to any one order on it, so they stay at the root in the
// left column where they cannot be read as belonging to whichever order happens to be selected.
export default function LoadDetailPanel({ loadId, preCommercial = false, allowUnpick = false, onClose }: Props) {
  const [detail, setDetail] = useState<LoadDetail | null>(null);
  const [loadState, setLoadState] = useState<"loading" | "ready" | "failed">("loading");
  const [selectedOrderId, setSelectedOrderId] = useState<number | null>(null);
  // The printed document open for one of the load's orders (delivery note or commercial invoice).
  const [docOpen, setDocOpen] = useState<{ orderId: number; kind: "note" | "invoice" } | null>(null);
  // The pre-commercial invoices email (Loads grid only).
  const [emailOpen, setEmailOpen] = useState(false);
  const [photoView, setPhotoView] = useState<LoadPhoto | null>(null);
  const [swapOpen, setSwapOpen] = useState(false);
  const [swapBusy, setSwapBusy] = useState(false);
  // The audit card can be folded to a rail, giving the grids its width. Remembered on this
  // machine: somebody who folds it away does not want it back on every load they open.
  const [auditOpen, setAuditOpen] = useState(readAuditOpen);
  const toggleAudit = () => {
    const next = !auditOpen;
    setAuditOpen(next);
    writeAuditOpen(next);
  };

  // One call brings the record, its orders, every line and every pallet on the load, and the
  // photos — so moving between orders costs nothing but a re-render.
  useEffect(() => {
    let cancelled = false;
    setLoadState("loading");
    getLoadDetail(loadId)
      .then((d) => {
        if (cancelled) return;
        setDetail(d);
        // Open on the first drop, which is the one the yard is asking about.
        setSelectedOrderId(d.orders[0]?.id ?? null);
        setLoadState("ready");
      })
      .catch(() => {
        if (!cancelled) setLoadState("failed");
      });
    return () => {
      cancelled = true;
    };
  }, [loadId]);

  // Escape belongs to whatever is on top: the lightbox, the delivery note, then the panel.
  useEscape(onClose, photoView == null && docOpen == null && !emailOpen);

  const rec = detail?.record;
  const orders = detail?.orders ?? [];
  const photos = detail?.photos ?? [];
  // The emails sent for the load go on the audit column, each in its place among the selected
  // order's changes, every address it went to on a line of its own.
  const emailEvents = useMemo<AuditEvent[]>(
    () =>
      (detail?.emails ?? []).map((e) => ({
        id: `email-${e.id}`,
        when: e.dateSent,
        title:
          e.kind === "Dispatched"
            ? "Dispatch confirmation emailed"
            : e.kind === "DispatchedCustomer"
              ? "Dispatch confirmation emailed to customer"
              : e.kind === "PreCommercial"
                ? "Pre-commercial invoices emailed"
                : "Ready for collection emailed",
        lines: e.sentTo
          .split(",")
          .map((s) => s.trim())
          .filter((s) => s.length > 0),
        who: e.sentBy,
      })),
    [detail],
  );
  const selectedOrder = orders.find((o) => o.id === selectedOrderId) ?? null;

  // Prices and comments are per order and are not on the load call, so they are fetched when an
  // order is first opened and kept — moving back to a tab already read costs nothing.
  const [editByOrder, setEditByOrder] = useState<Record<number, OrderEditDetail>>({});
  useEffect(() => {
    if (selectedOrderId == null || editByOrder[selectedOrderId]) return;
    let cancelled = false;
    const id = selectedOrderId;
    getOrderEditItems(id)
      .then((d) => {
        if (!cancelled) setEditByOrder((prev) => ({ ...prev, [id]: d }));
      })
      .catch(() => {
        // A missing price column is not worth an error dialog over — the grid shows blanks.
      });
    return () => {
      cancelled = true;
    };
  }, [selectedOrderId, editByOrder]);

  const orderEdit = selectedOrderId == null ? undefined : editByOrder[selectedOrderId];
  const priceByProduct = useMemo(() => {
    const map: Record<number, number | null> = {};
    orderEdit?.items.forEach((i) => (map[i.productId] = i.price));
    return map;
  }, [orderEdit]);

  // The lines and pallets for the order on show. Both arrays arrive for the whole load.
  const orderItems = useMemo(
    () => (detail?.items ?? []).filter((i) => i.orderId === selectedOrderId),
    [detail, selectedOrderId],
  );
  const orderPicked = useMemo(
    () => (detail?.picked ?? []).filter((p) => p.orderId === selectedOrderId),
    [detail, selectedOrderId],
  );

  // Pallets can be unpicked until the lorry weighs out and the order is dispatched; after that
  // the stock has gone and the pick is history.
  const canUnpick =
    allowUnpick &&
    loadState === "ready" &&
    rec?.dateWeightOut == null &&
    orders.find((o) => o.id === selectedOrderId)?.dateDispatched == null;
  async function onUnpick(p: PickedPallet) {
    if (!(await confirmUnpick(p))) return;
    try {
      setDetail(await getLoadDetail(loadId));
    } catch (err) {
      void messageDialog("Unpicked, but the load could not be re-read.\n" + (err as Error).message);
    }
  }

  // Load totals — every order on the lorry added up, which is what the weighbridge is comparing
  // its own reading against.
  const totals = useMemo(
    () => ({
      ordered: orders.reduce((s, o) => s + o.totalQuantityOrdered, 0),
      picked: orders.reduce((s, o) => s + o.pickedQuantity, 0),
      weight: orders.reduce((s, o) => s + o.pickedWeight, 0),
    }),
    [orders],
  );

  const weighedIn = (rec?.weightIn ?? 0) > 0;
  const weighedOut = (rec?.weightOut ?? 0) > 0;
  // What the BRIDGE has done with it, which is the only progress a weighbridge record knows.
  // "Not Weighed" rather than "Loading": a record in the Loads grid may not have been picked yet
  // either, and the one thing that is certainly true is that it has not been on the bridge.
  const status = weighedOut ? "Weighed Out" : weighedIn ? "In The Yard" : "Not Weighed";
  // An outgoing lorry weighs in empty and out loaded, so the goods are out minus in; an incoming
  // one is the other way round. Either way the net is what the bridge says the load weighs.
  const net =
    rec && weighedIn && weighedOut
      ? rec.isOutgoing
        ? rec.weightOut - rec.weightIn
        : rec.weightIn - rec.weightOut
      : null;

  // Swap the open photo's caption with another's, then re-read the load so the corrected labels
  // show everywhere at once.
  async function doSwap(other: LoadPhoto) {
    if (!photoView) return;
    setSwapBusy(true);
    try {
      await swapLoadPhotos(loadId, photoView.id, other.id);
      const d = await getLoadDetail(loadId);
      setDetail(d);
      setPhotoView(d.photos.find((p) => p.id === photoView.id) ?? null);
      setSwapOpen(false);
    } catch (err) {
      void messageDialog("Failed to swap photos.\n" + (err as Error).message);
    } finally {
      setSwapBusy(false);
    }
  }

  const apiBase = getApiBaseUrl();

  // A typed-in note. While the order's own detail is still on its way there is no answer yet, and
  // an em dash would wrongly say there is nothing there — so that case reads as "…".
  const noteText = (text: string | null | undefined) =>
    orderEdit === undefined ? "…" : text ? <span className={styles.summaryProse}>{text}</span> : null;

  return (
    <div className={styles.panel} role="dialog" aria-label="Load details">
      <div className={styles.header}>
        <BackButton onClick={onClose} />
        <span className={styles.headerTitle}>
          Load{rec?.haulierName ? ` — ${rec.haulierName}` : ""}
        </span>
        <span className={styles.headerRef}>Load #{loadId}</span>
      </div>

      <div className={styles.content}>
        {/* ── Left: the LOAD. Everything here is about the lorry, not about any one order on it ── */}
        <aside className={styles.leftPanel}>
          <section className={styles.section}>
            <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
              <span>Load Details</span>
              <span className={cx(styles.statusTag, weighedOut && styles.statusDispatched)}>
                {status}
              </span>
            </h3>
            <Field label="Load Ref" value={`#${loadId}`} />
            <Field label="Haulier" value={rec?.haulierName} />
            <Field label="Trailer No" value={rec?.trailerNo} />
            <Field label="Collection" value={rec?.collectionDate ? fmtDate(rec.collectionDate) : null} />
            <Field label="Direction" value={rec ? (rec.isOutgoing ? "Outgoing" : "Incoming") : null} />
            <Field
              label="Orders"
              value={loadState === "ready" ? `${orders.length}` : loadState === "loading" ? "…" : null}
            />
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Weights</h3>
            <Field label="Weight In" value={weighedIn ? `${fmtQty(rec!.weightIn)} kg` : null} />
            <Field label="Weight Out" value={weighedOut ? `${fmtQty(rec!.weightOut)} kg` : null} />
            {/* The bridge's figure, then under it the picked pallets' own weights added up, with
                the gap between the two in brackets beside it — which is what the yard wants to
                see at a glance without hunting for it in the Picked section below. Pallets minus
                bridge, so a minus is a lorry heavier than its pallets account for. */}
            <Field label="Net" value={net == null ? null : <strong>{fmtQty(net)} kg</strong>} />
            <Field
              label="Calculated"
              value={
                totals.weight > 0 ? (
                  <>
                    <span className={styles.calc} title="The picked pallets' own weights added up">
                      {fmtQty(totals.weight)} kg
                    </span>
                    {net != null && (
                      <span title="The picked pallets' own weights minus the net on the bridge">
                        {" "}
                        ({totals.weight < net ? "−" : totals.weight > net ? "+" : ""}
                        {fmtQty(Math.abs(net - totals.weight))} kg)
                      </span>
                    )}
                  </>
                ) : null
              }
            />
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Picked</h3>
            <Field label="Ordered" value={`${fmtQty(totals.ordered)} pallets`} />
            <Field label="Picked" value={<strong>{fmtQty(totals.picked)} pallets</strong>} />
            {totals.picked !== totals.ordered && (
              <Field
                label={totals.picked < totals.ordered ? "Short By" : "Over By"}
                value={
                  <span className={styles.warn}>
                    {fmtQty(Math.abs(totals.picked - totals.ordered))} pallets
                  </span>
                }
              />
            )}
            <Field label="Picked Weight" value={totals.weight > 0 ? `${fmtQty(totals.weight)} kg` : null} />
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Times</h3>
            <Field label="Weighed In" value={fmtDateTime(rec?.dateWeightIn)} />
            <Field label="Picking Done" value={fmtDateTime(rec?.datePickingComplete)} />
            <Field label="Weighed Out" value={fmtDateTime(rec?.dateWeightOut)} />
            <Field label="Turnaround" value={turnaround(rec?.dateWeightIn, rec?.dateWeightOut)} />
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Comments</h3>
            <Field label="Load" value={<span className={styles.prose}>{rec?.comments}</span>} />
          </section>

          {/* Pictures of the lorry, taken by the picking app as it was loaded. They belong to the
              LOAD, so they sit here rather than under whichever order happens to be selected. */}
          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Photos ({photos.length})</h3>
            {photos.length === 0 ? (
              <span className={cx(styles.muted, styles.photoEmpty)}>
                {loadState === "loading" ? "Loading…" : "No photos were taken for this load."}
              </span>
            ) : (
              <div className={styles.photoGrid}>
                {photos.map((p) => (
                  <PhotoThumb
                    key={p.id}
                    src={loadPhotoUrl(apiBase, p.id)}
                    photo={p}
                    onClick={() => setPhotoView(p)}
                  />
                ))}
              </div>
            )}
          </section>
        </aside>

        {/* ── Right: ONE of the load's orders, read the same way the order panel reads it ── */}
        <div className={styles.rightPanel}>
          {orders.length === 0 ? (
            <div className={styles.paneEmpty}>
              {loadState === "loading"
                ? "Loading…"
                : loadState === "failed"
                  ? "Could not load this load."
                  : "No orders on this load."}
            </div>
          ) : (
            <>
              {/* In delivery sequence — the first tab is the first drop. */}
              <div className={styles.orderTabs} role="tablist" aria-label="Orders on this load">
                {orders.map((o, i) => (
                  <button
                    key={o.id}
                    type="button"
                    role="tab"
                    aria-selected={o.id === selectedOrderId}
                    className={cx(styles.orderTab, o.id === selectedOrderId && styles.orderTabActive)}
                    onClick={() => setSelectedOrderId(o.id)}
                    title={`Order ${o.orderNo} — ${o.customerName ?? ""}`}
                  >
                    <span className={styles.orderTabNo}>{o.sortOrder ?? i + 1}</span>
                    <span className={styles.orderTabName}>
                      {o.orderNo} · {o.customerName ?? "—"}
                    </span>
                  </button>
                ))}
              </div>

              {selectedOrder && (
                <div className={styles.orderSummary}>
                  {/* Left: what the order IS — who it is for and where it goes, then what it
                      came to. */}
                  <div className={cx(styles.summaryCol, styles.summaryColFit)}>
                    <Pair label="Customer" value={selectedOrder.customerName} />
                    <Pair
                      label="Address"
                      value={<span className={styles.summaryProse}>{selectedOrder.addressFull}</span>}
                    />
                    <Pair label="PO Number" value={selectedOrder.poNumber} />
                    <Pair
                      label="Delivery"
                      value={selectedOrder.deliveryDate ? fmtDate(selectedOrder.deliveryDate) : "ASAP"}
                    />
                    <Pair
                      label="Pallets"
                      value={
                        selectedOrder.pickedQuantity === selectedOrder.totalQuantityOrdered ? (
                          `${fmtQty(selectedOrder.pickedQuantity)} picked`
                        ) : (
                          <span className={styles.bad}>
                            {fmtQty(selectedOrder.pickedQuantity)} of{" "}
                            {fmtQty(selectedOrder.totalQuantityOrdered)} picked
                          </span>
                        )
                      }
                    />
                    <div className={styles.summaryActions}>
                      <button
                        type="button"
                        className={styles.summaryBtn}
                        onClick={() => setDocOpen({ orderId: selectedOrder.id, kind: "note" })}
                      >
                        Delivery Note
                      </button>
                      <button
                        type="button"
                        className={cx(styles.summaryBtn, preCommercial && styles.summaryBtnPre)}
                        title={
                          preCommercial
                            ? "An estimate: the ordered pallets at each product's average weight"
                            : undefined
                        }
                        onClick={() => setDocOpen({ orderId: selectedOrder.id, kind: "invoice" })}
                      >
                        {preCommercial ? "Pre-Commercial Invoice" : "Commercial Invoice"}
                      </button>
                      {/* Email the pre-commercial invoices — every order's on the load, not just
                          the tab on show — to the haulier. */}
                      {preCommercial && (
                        <button
                          type="button"
                          className={cx(styles.summaryBtn, styles.summaryBtnPre, styles.summaryBtnIcon)}
                          title="Email the pre-commercial invoices for this load to the haulier"
                          aria-label="Email pre-commercial invoices"
                          onClick={() => setEmailOpen(true)}
                        >
                          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                            <rect x="3" y="5" width="18" height="14" rx="2" />
                            <path d="M3 7l9 6 9-6" />
                          </svg>
                        </button>
                      )}
                    </div>
                  </div>

                  {/* Right: the notes somebody typed. They get a column of their own because
                      they are the long values — spanning the band they pushed everything else
                      down the page — and they are always shown, so an order with no
                      instructions says so rather than leaving the half empty. */}
                  <div className={cx(styles.summaryCol, styles.summaryColFill)}>
                    <Pair label="Delivery Note" value={noteText(orderEdit?.comments)} />
                    <Pair label="Picking" value={noteText(orderEdit?.pickingComments)} />
                  </div>
                </div>
              )}

              <OrderGrids
                items={orderItems}
                picked={orderPicked}
                priceByProduct={priceByProduct}
                loading={loadState === "loading"}
                failed={loadState === "failed"}
                orderId={selectedOrderId ?? undefined}
                onUnpick={canUnpick ? onUnpick : undefined}
              />
            </>
          )}
        </div>

        {/* ── Far right: what has been changed on the selected order. The left column's twin —
            the same card, the same width, the same title — so the load's record and the order's
            history bracket the orders between them. It follows the tabs: it is the SELECTED
            order's trail, since a load itself keeps none — with the load's emails placed among
            the changes by date, since an email went for every order on it. ── */}
        <aside className={cx(styles.auditPanel, !auditOpen && styles.auditPanelCollapsed)}>
          <AuditTrailColumn
            entityName={AUDIT_ENTITY.dispatchOrder}
            entityId={selectedOrderId}
            open={auditOpen}
            onToggle={toggleAudit}
            events={emailEvents}
          />
        </aside>
      </div>

      {/* The picture full size, over the panel rather than in an app modal — Back still means
          "back to the load", and the photo never escapes the page it belongs to. */}
      {photoView && (
        <div className={styles.lightbox} role="dialog" aria-label="Load photo">
          <div className={styles.lightboxBar}>
            <span>{photoView.description ?? "Photo"}</span>
            <span className={styles.lightboxWhen}>{fmtDateTime(photoView.dateCreated)}</span>
            <div className={styles.lightboxActions}>
              {/* The picking app files photos by slot; a driver who takes them out of order
                  leaves two captions crossed, and this uncrosses them. */}
              <button
                type="button"
                className={styles.lightboxBtn}
                disabled={photos.length < 2 || swapBusy}
                title="Swap this photo's caption with another photo's"
                onClick={() => setSwapOpen((v) => !v)}
              >
                {swapOpen ? "Cancel Swap" : "Swap"}
              </button>
              <button
                type="button"
                className={styles.lightboxBtn}
                onClick={() => {
                  setSwapOpen(false);
                  setPhotoView(null);
                }}
              >
                Close
              </button>
            </div>
          </div>

          {swapOpen ? (
            <>
              <p className={styles.swapNote}>
                Click the photo to swap with — the two exchange captions, so each picture ends up
                under the correct label.
              </p>
              <div className={cx(styles.swapGrid, swapBusy && styles.swapBusy)}>
                {photos
                  .filter((p) => p.id !== photoView.id)
                  .map((p) => (
                    <PhotoThumb
                      key={p.id}
                      src={loadPhotoUrl(apiBase, p.id)}
                      photo={p}
                      onClick={() => void doSwap(p)}
                    />
                  ))}
              </div>
            </>
          ) : (
            <div className={styles.lightboxStage}>
              <FullPhoto key={photoView.id} src={loadPhotoUrl(apiBase, photoView.id)} />
            </div>
          )}
        </div>
      )}

      {docOpen != null && (
        <DeliveryNoteModal
          orderId={docOpen.orderId}
          kind={docOpen.kind}
          estimate={preCommercial && docOpen.kind === "invoice"}
          onClose={() => setDocOpen(null)}
        />
      )}

      {emailOpen && (
        <SendLoadEmailModal
          loadId={loadId}
          kind="precommercial"
          onClose={(sent) => {
            setEmailOpen(false);
            // Re-read the load so the email shows on the audit column.
            if (sent) void getLoadDetail(loadId).then(setDetail).catch(() => {});
          }}
        />
      )}
    </div>
  );
}

// One photo tile. The row exists because the picking app filed it, so a picture whose bytes will
// not load says so and keeps its place in the set rather than collapsing and leaving a gap — and
// an empty alt keeps a broken image from stretching the tile with its own filename.
function PhotoThumb({
  src,
  photo,
  onClick,
}: {
  src: string;
  photo: LoadPhoto;
  onClick: () => void;
}) {
  const [failed, setFailed] = useState(false);
  return (
    <button
      type="button"
      className={styles.photo}
      onClick={onClick}
      title={photo.description ?? "Load photo"}
    >
      {failed ? (
        <span className={styles.photoMissing}>No image</span>
      ) : (
        <img src={src} alt="" onError={() => setFailed(true)} />
      )}
      <span className={styles.photoCaption}>
        <span className={styles.photoName}>{photo.description ?? "—"}</span>
        <span className={styles.photoDate}>{fmtDateTime(photo.dateCreated)}</span>
      </span>
    </button>
  );
}

// The same picture full size. Keyed by photo id by its caller, so moving to another picture starts
// from "not failed" rather than inheriting the last one's verdict.
function FullPhoto({ src }: { src: string }) {
  const [failed, setFailed] = useState(false);
  return failed ? (
    <span className={styles.lightboxMissing}>This photo could not be loaded.</span>
  ) : (
    <img src={src} alt="" onError={() => setFailed(true)} />
  );
}

// A label/value pair in one column of the selected order's summary band. The value takes whatever
// the label leaves, so nothing needs to span: each column is its own two-track grid.
function Pair({ label, value }: { label: string; value: ReactNode }) {
  const empty = value === null || value === undefined || value === "";
  return (
    <Fragment>
      <span className={styles.summaryLabel}>{label}</span>
      <span className={styles.summaryValue}>
        {empty ? <span className={styles.muted}>—</span> : value}
      </span>
    </Fragment>
  );
}

// "2h 14m" between the two weighings, or null until the lorry has done both.
function turnaround(from: string | null | undefined, to: string | null | undefined): string | null {
  if (!from || !to) return null;
  const ms = new Date(to).getTime() - new Date(from).getTime();
  if (!Number.isFinite(ms) || ms < 0) return null;
  const mins = Math.round(ms / 60000);
  return mins < 60 ? `${mins}m` : `${Math.floor(mins / 60)}h ${mins % 60}m`;
}

// Whether the audit card is open, kept in this browser's storage. A per-machine convenience, so
// it may be unreadable (a locked-down profile) — and then the card simply opens.
const AUDIT_OPEN_KEY = "loadDetail.auditOpen";

function readAuditOpen(): boolean {
  try {
    return localStorage.getItem(AUDIT_OPEN_KEY) !== "0";
  } catch {
    return true;
  }
}

function writeAuditOpen(open: boolean) {
  try {
    localStorage.setItem(AUDIT_OPEN_KEY, open ? "1" : "0");
  } catch {
    // The choice just will not survive the session.
  }
}
