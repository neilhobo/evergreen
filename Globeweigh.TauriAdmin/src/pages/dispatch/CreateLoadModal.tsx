import { useEffect, useMemo, useState } from "react";
import Modal from "../../components/Modal";
import DataTable, { Column } from "../../components/DataTable";
import { messageDialog } from "../../components/confirmDialog";
import {
  DispatchOrder,
  LoadInput,
  createLoad,
  updateLoad,
  getLoadDetail,
  getOpenOrders,
} from "../../api/dispatch";
import { getHauliers, HaulierRow } from "../../api/hauliers";
import { fmtDate, toDateInput } from "../../format";
import styles from "../../widgets/DetailPanel.module.css";
import { cx } from "../../widgets/DetailPanel";
import EmailsGrid, { EmailParty } from "./EmailsGrid";

interface Props {
  loadId: number | null; // null = new
  onClose: (saved: boolean) => void;
}

// Mirrors CreateLoadView — build/edit a weighbridge pre-load: pick a haulier,
// week-commencing date, comments, and tick the open orders to include.
export default function CreateLoadModal({ loadId, onClose }: Props) {
  const isNew = loadId === null;

  const [hauliers, setHauliers] = useState<HaulierRow[]>([]);
  const [orders, setOrders] = useState<DispatchOrder[]>([]);
  const [haulierId, setHaulierId] = useState(0);
  const [collectionDate, setCollectionDate] = useState("");
  const [trailerNo, setTrailerNo] = useState("");
  const [comments, setComments] = useState("");
  // Pending = weighed in but not out. Only then is the in-weight shown, and editable
  // (a mis-keyed or mis-read weighbridge reading can be corrected here).
  const [isPending, setIsPending] = useState(false);
  const [weightIn, setWeightIn] = useState("");
  // Ticked orders → their delivery order (1 = delivered first). Always a full 1..N set.
  const [seq, setSeq] = useState<Map<number, number>>(new Map());

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [hl, op] = await Promise.all([getHauliers(), getOpenOrders()]);
        if (cancelled) return;
        setHauliers(hl);
        setOrders(op);
        if (loadId != null) {
          const detail = await getLoadDetail(loadId);
          if (cancelled) return;
          setHaulierId(detail.record.haulierId);
          setCollectionDate(toDateInput(detail.record.collectionDate));
          setTrailerNo(detail.record.trailerNo ?? "");
          setComments(detail.record.comments ?? "");
          const pending = detail.record.dateWeightIn != null && detail.record.dateWeightOut == null;
          setIsPending(pending);
          setWeightIn(pending ? String(detail.record.weightIn ?? 0) : "");
          // Pre-tick orders already on this load, numbered 1..N in their stored
          // sequence (older loads were saved 0-based, so renumber by rank).
          setSeq(
            new Map(
              op
                .filter((o) => o.weighbridgeRecordId === loadId)
                .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
                .map((o, i) => [o.id, i + 1] as const),
            ),
          );
        }
      } catch (err) {
        if (!cancelled) void messageDialog("Failed to load.\n" + (err as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [loadId]);

  // Ticking gives the order the next delivery number; unticking closes the gap so the
  // remaining orders stay a 1..N sequence.
  function toggle(id: number) {
    setSeq((prev) => {
      if (!prev.has(id)) return new Map(prev).set(id, prev.size + 1);
      const rest = [...prev].filter(([oid]) => oid !== id).sort((a, b) => a[1] - b[1]);
      return new Map(rest.map(([oid], i) => [oid, i + 1]));
    });
  }

  // Picking a number that another order already holds swaps the two, so the load can
  // never end up with a repeated delivery order.
  function setDelivery(id: number, value: number) {
    setSeq((prev) => {
      const current = prev.get(id);
      if (current == null || current === value) return prev;
      const next = new Map(prev);
      for (const [oid, n] of prev) if (n === value) next.set(oid, current);
      next.set(id, value);
      return next;
    });
  }

  // Where the load's paperwork goes: the chosen haulier's addresses, one row with them stacked.
  // A haulier with no address still gets the row, so a missing one is seen rather than silent.
  const emailParties = useMemo(() => {
    const h = hauliers.find((x) => x.id === haulierId);
    if (!h) return [] as EmailParty[];
    const emails = [...new Set(h.emails.map((e) => e.trim()).filter(Boolean))];
    return [{ key: `h${h.id}`, name: h.name ?? "", emails }];
  }, [hauliers, haulierId]);

  async function onSave() {
    if (haulierId <= 0) {
      void messageDialog("Haulier is required.");
      return;
    }
    if (isPending && !/^\d+$/.test(weightIn.trim())) {
      void messageDialog("Weight In must be a whole number of Kg.");
      return;
    }
    const payload: LoadInput = {
      haulierId,
      collectionDate: collectionDate ? new Date(collectionDate).toISOString() : null,
      trailerNo: trailerNo.trim() || null,
      comments: comments.trim() || null,
      weightIn: isPending ? Number(weightIn.trim()) : null,
      orders: [...seq]
        .sort((a, b) => a[1] - b[1])
        .map(([orderId, deliveryOrder]) => ({ orderId, deliveryOrder })),
    };
    setBusy(true);
    try {
      if (isNew) await createLoad(payload);
      else await updateLoad(loadId!, payload);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  // Only unassigned orders are offered — plus, when editing, the ones already on
  // THIS load (so they can be unticked).
  const visibleOrders = orders.filter(
    (o) => o.weighbridgeRecordId <= 0 || o.weighbridgeRecordId === loadId,
  );

  const columns: Column<DispatchOrder>[] = [
    {
      key: "check",
      header: " ",
      width: 36,
      align: "center",
      render: (r) => (
        <input
          type="checkbox"
          checked={seq.has(r.id)}
          onChange={() => toggle(r.id)}
          onClick={(e) => e.stopPropagation()}
        />
      ),
    },
    {
      key: "deliveryOrder",
      header: (
        <>
          Delivery
          <br />
          Order
        </>
      ),
      width: 76,
      align: "center",
      // 1 = first drop, N = last. Choosing a number another order holds swaps them.
      render: (r) =>
        seq.has(r.id) ? (
          <select
            className="form-input cl-delivery-order"
            value={seq.get(r.id)}
            onChange={(e) => setDelivery(r.id, Number(e.target.value))}
            onClick={(e) => e.stopPropagation()}
          >
            {Array.from({ length: seq.size }, (_, i) => i + 1).map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        ) : (
          ""
        ),
    },
    { key: "orderNo", header: "Order", width: 70, render: (r) => r.orderNo },
    { key: "customerName", header: "Customer", render: (r) => r.customerName },
    {
      key: "deliveryDate",
      header: "Delivery",
      width: 90,
      render: (r) => (r.deliveryDate ? fmtDate(r.deliveryDate) : "ASAP"),
    },
  ];

  return (
    <Modal
      title={`${isNew ? "Create" : "Edit"} Load`}
      width={930}
      height={710}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || loading || haulierId <= 0} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      {loading ? (
        <div className="data-table-empty">Loading…</div>
      ) : (
        <div className="cl-form">
          {/* One white panel — the Order Details page's left panel turned on its side: the load's
              header along the top and the orders it carries filling the rest, parted by a rule. The
              section titles and label/value rows are the page's own, with the controls where the
              values go. The right half of the header is kept for what comes next. */}
          <div className="ld-panel">
            <div className="ld-head">
              <section className={styles.section}>
                <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
                  <span>Load Details</span>
                  <span className={cx(styles.statusTag, isPending && styles.statusDispatched)}>
                    {isNew ? "New" : isPending ? "Pending" : "Open"}
                  </span>
                </h3>
                <label className={cx(styles.field, "ld-row")}>
                  <span className={styles.fieldLabel}>Haulier</span>
                  <select
                    className="ld-input"
                    value={haulierId}
                    autoFocus
                    onChange={(e) => setHaulierId(Number(e.target.value))}
                  >
                    <option value={0}>— Select —</option>
                    {hauliers.map((h) => (
                      <option key={h.id} value={h.id}>
                        {h.name}
                      </option>
                    ))}
                  </select>
                </label>
                <label className={cx(styles.field, "ld-row")}>
                  <span className={styles.fieldLabel}>Week Commencing</span>
                  <input
                    className="ld-input"
                    type="date"
                    value={collectionDate}
                    onChange={(e) => setCollectionDate(e.target.value)}
                  />
                </label>
                <label className={cx(styles.field, "ld-row")}>
                  <span className={styles.fieldLabel}>Trailer No</span>
                  <input className="ld-input" value={trailerNo} onChange={(e) => setTrailerNo(e.target.value)} />
                </label>
                {isPending && (
                  <label className={cx(styles.field, "ld-row")}>
                    <span className={styles.fieldLabel}>Weight In (kg)</span>
                    <input
                      className="ld-input"
                      inputMode="numeric"
                      value={weightIn}
                      onChange={(e) => setWeightIn(e.target.value.replace(/[^0-9]/g, ""))}
                    />
                  </label>
                )}
                <label className={cx(styles.field, "ld-row", "ld-row-top")}>
                  <span className={styles.fieldLabel}>Comments</span>
                  <textarea
                    className="ld-input ld-comments-box"
                    value={comments}
                    onChange={(e) => setComments(e.target.value)}
                  />
                </label>
              </section>
              {/* The right half: where the load's paperwork goes. Held to the left half's height and
                  scrolling within it, so a long list never pushes the orders down. */}
              <div className="ld-emails-cell">
                <section className={cx(styles.section, "ld-emails")}>
                  <h3 className={styles.sectionTitle}>Emails</h3>
                  <EmailsGrid parties={emailParties} emptyText="Choose a haulier" />
                </section>
              </div>
            </div>
            <section className={cx(styles.section, "ld-grid")}>
              <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
                <span>Orders</span>
                <span className="ld-note">{seq.size} selected — delivery order 1 is dropped first</span>
              </h3>
              <DataTable
                columns={columns}
                rows={visibleOrders}
                rowKey={(r) => r.id}
                onSelect={(r) => toggle(r.id)}
                emptyText="No unassigned open orders"
              />
            </section>
          </div>
        </div>
      )}
    </Modal>
  );
}
