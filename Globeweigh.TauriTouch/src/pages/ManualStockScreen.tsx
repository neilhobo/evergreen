import { useCallback, useEffect, useRef, useState } from "react";
import TopBar from "../components/touch/TopBar";
import ProductionLabelPreview from "../components/touch/ProductionLabelPreview";
import { keypad } from "../components/touch/Keypad";
import { keyboard } from "../components/touch/Keyboard";
import { confirmDialog, messageDialog, touchChoiceDialog } from "../components/confirmDialog";
import {
  FinishedProduct,
  ManualPallet,
  PalletBatchRow,
  addBatchPallet,
  createPalletBatch,
  deleteBatchPallet,
  deletePalletBatch,
  getBatchPallets,
  getFinishedProducts,
  getManualStockConfig,
  getPalletBatches,
  logLabelReprint,
  markPalletsPrinted,
  setManualStockConfig,
} from "../api/touch";
import {
  connectWeighscale,
  disconnectWeighscale,
  getSavedWeighscaleConfig,
  inTauriShell,
  onWeighscaleData,
  onWeighscaleStatus,
  parseWeight,
} from "../api/serial";
import { getSavedPrinter, PRINTING_ENABLED, printImage } from "../api/printer";
import { renderProductionLabelCanvas } from "../api/productionLabelCanvas";

interface Props {
  userName: string;
  // This PC's Device row name — recorded against a label reprint (see logLabelReprint).
  deviceName: string | null;
  onHome: () => void;
}

// Manual Stock: create pallets by hand (vs the 3rd-party Codico feed). A batch = N pallets of
// one finished product against an order number (dbo.PalletBatch). Each pallet is then put on
// the weighscale and captured via the Add/Weigh popup (live weight from this PC's weighscale
// config) — the capture inserts a CodicoPallet row (IsManual=1, UniqueCode "M{batch}-{seq}").
// Layout: batch list left, selected batch's pallets right.
export default function ManualStockScreen({ userName, deviceName, onHome }: Props) {
  const [view, setView] = useState<"list" | "new">("list");
  const [batches, setBatches] = useState<PalletBatchRow[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [pallets, setPallets] = useState<ManualPallet[]>([]);
  const [selectedPalletId, setSelectedPalletId] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [weighing, setWeighing] = useState(false);
  // Print status overlay: shown while a label is being sent (simulated in dev mode).
  const [printStatus, setPrintStatus] = useState<{
    phase: "printing" | "done" | "error";
    text: string;
  } | null>(null);
  // Dev builds print nothing, so Reprint Label shows the rendered label instead (PNG data URL).
  const [devLabelPreview, setDevLabelPreview] = useState<{ code: string; src: string } | null>(null);

  // New Batch state
  const [products, setProducts] = useState<FinishedProduct[]>([]);
  const [productSearch, setProductSearch] = useState("");
  const [productId, setProductId] = useState<number | null>(null);
  const [orderNumber, setOrderNumber] = useState("");
  const [palletCount, setPalletCount] = useState<number | null>(null);
  // On = weigh each pallet on the scale after the batch is created (the original flow).
  // Off = every pallet is created on save at palletWeight, seeded from the product's
  // Average Weight and editable here for this batch only.
  const [requireIndividual, setRequireIndividual] = useState(true);
  const [palletWeight, setPalletWeight] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);

  const refresh = useCallback(async (selectId?: number) => {
    setLoading(true);
    try {
      const rows = await getPalletBatches();
      setBatches(rows);
      setError(null);
      setSelectedId((cur) => {
        const want = selectId ?? cur;
        return rows.some((b) => b.id === want) ? want! : (rows[0]?.id ?? null);
      });
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Load the selected batch's pallets whenever the selection changes.
  useEffect(() => {
    setSelectedPalletId(null);
    if (selectedId === null) {
      setPallets([]);
      return;
    }
    let cancelled = false;
    getBatchPallets(selectedId)
      .then((rows) => {
        if (!cancelled) setPallets(rows);
      })
      .catch(() => {
        if (!cancelled) setPallets([]);
      });
    return () => {
      cancelled = true;
    };
  }, [selectedId]);

  const selected = batches.find((b) => b.id === selectedId) ?? null;

  async function openNew() {
    setProductId(null);
    setProductSearch("");
    setOrderNumber("");
    setPalletCount(null);
    setRequireIndividual(true);
    setPalletWeight(null);
    setView("new");
    if (products.length === 0) {
      try {
        setProducts(await getFinishedProducts());
      } catch (err) {
        void messageDialog("Could not load products.\n" + (err as Error).message);
        setView("list");
      }
    }
  }

  async function enterOrderNumber() {
    const v = await keyboard({ title: "Order Number", initial: orderNumber });
    if (v !== null) setOrderNumber(v.trim());
  }

  async function enterPalletCount() {
    const v = await keypad({ title: "Number of Pallets", initial: palletCount?.toString() ?? "", max: 100 });
    if (v === null) return;
    const n = parseInt(v, 10);
    if (Number.isFinite(n) && n > 100) {
      void messageDialog("A batch can have at most 100 pallets.");
      return;
    }
    setPalletCount(Number.isFinite(n) && n > 0 ? n : null);
  }

  async function enterPalletWeight() {
    const v = await keypad({
      title: "Pallet Weight (kg)",
      decimal: true,
      initial: palletWeight !== null ? String(palletWeight) : "",
    });
    if (v === null) return;
    const n = parseFloat(v);
    if (!Number.isFinite(n) || n <= 0) {
      void messageDialog("Weight must be a number greater than zero.");
      return;
    }
    setPalletWeight(n);
  }

  function pickProduct(p: FinishedProduct) {
    setProductId(p.id);
    // Seed the batch weight from the product's Average Weight (blank if it has none).
    setPalletWeight(p.averageWeight && p.averageWeight > 0 ? p.averageWeight : null);
  }

  async function create() {
    if (!productId || !palletCount) return;
    if (!requireIndividual && !(palletWeight && palletWeight > 0)) return;

    // Average-weight batches create every pallet on save, so ask about labels first —
    // Cancel backs out before anything is written.
    let print = false;
    if (!requireIndividual) {
      const answer = await touchChoiceDialog({
        icon: "🖨",
        title: `Print ${palletCount} label${palletCount === 1 ? "" : "s"}?`,
        message: `${palletCount} pallet${palletCount === 1 ? "" : "s"} will be created at ${palletWeight!.toLocaleString(undefined, { maximumFractionDigits: 2 })} kg each.`,
        choices: [
          { value: "cancel", text: "Cancel" },
          { value: "continue", text: "Continue without printing" },
          { value: "print", text: "Yes, print", tone: "primary" },
        ],
      });
      if (answer === null || answer === "cancel") return;
      print = answer === "print";
    }

    setSaving(true);
    try {
      const created = await createPalletBatch({
        orderNumber: orderNumber || null,
        productId,
        palletCount,
        createdBy: userName,
        requireIndividualWeight: requireIndividual,
        palletWeight: requireIndividual ? null : palletWeight,
      });
      setView("list");
      await refresh(created.id);
      if (print) {
        const rows = await getBatchPallets(created.id).catch(() => [] as ManualPallet[]);
        await printLabels(rows.map((p, i) => ({ pallet: p, seq: i + 1 })), created);
      }
    } catch (err) {
      void messageDialog("Failed to create the batch.\n" + (err as Error).message);
    } finally {
      setSaving(false);
    }
  }

  async function reloadPallets(batchId: number | null) {
    if (batchId === null) return;
    setPallets(await getBatchPallets(batchId).catch(() => []));
  }

  async function onPalletAdded(created: ManualPallet) {
    setWeighing(false);
    let rows: ManualPallet[] = [];
    if (selectedId !== null) {
      rows = await getBatchPallets(selectedId).catch(() => []);
      setPallets(rows);
    }
    await refresh();
    // Every captured pallet prints its label automatically (same flow as Reprint).
    if (selected) {
      const seq = rows.findIndex((p) => p.id === created.id) + 1;
      await printLabels([{ pallet: created, seq: seq > 0 ? seq : rows.length }], selected);
    }
  }

  // Prints pallet labels to this PC's configured printer through the normal Windows driver
  // path (dev included), one after another, and stamps each one that went out as printed
  // (the tick on the pallet list). Used straight after weighing, for a new average-weight
  // batch, for Print Unprinted, and by Reprint Label — only the last is an audit event,
  // hence isReprint.
  async function printLabels(
    items: { pallet: ManualPallet; seq: number }[],
    batch: PalletBatchRow,
    isReprint = false,
  ) {
    if (items.length === 0) return;
    // Dev builds never spool, so they don't need a printer — Reprint Label still shows its preview.
    const printerName = getSavedPrinter() ?? (PRINTING_ENABLED ? null : "(dev — no printer)");
    if (!printerName) {
      void messageDialog("No printer is set up on this PC.\nSelect one via the Printer badge on the Home screen first.");
      return;
    }
    const printedIds: number[] = [];
    try {
      for (const [i, { pallet }] of items.entries()) {
        setPrintStatus({
          phase: "printing",
          text:
            items.length === 1
              ? `Sending label ${pallet.uniqueCode} to ${printerName}…`
              : `Sending label ${i + 1} of ${items.length} (${pallet.uniqueCode}) to ${printerName}…`,
        });
        // Same template as a Production reprint: big description + DataMatrix + order no.
        const label = await renderProductionLabelCanvas({
          product: batch.product ?? "",
          orderNumber: batch.orderNumber,
          netWeightKg: pallet.weight,
          size: batch.size,
          upp: batch.upp,
          gtin: batch.gtin ?? "",
          palletCode: pallet.uniqueCode,
        });
        await printImage(printerName, label);
        printedIds.push(pallet.id);
        if (isReprint)
          logLabelReprint({
            palletId: pallet.id,
            uniqueCode: pallet.uniqueCode,
            source: "ManualStock",
            deviceName,
            printerName,
            userName: userName || null,
          });
        if (isReprint && !PRINTING_ENABLED)
          setDevLabelPreview({ code: pallet.uniqueCode, src: label.toDataURL("image/png") });
      }
      if (isReprint && !PRINTING_ENABLED) {
        setPrintStatus(null); // the preview replaces the "nothing was printed" toast
        return;
      }
      setPrintStatus({
        phase: "done",
        text: !PRINTING_ENABLED
          ? `Dev build — nothing was printed`
          : items.length === 1
            ? `Label ${items[0].pallet.uniqueCode} sent to ${printerName}`
            : `${items.length} labels sent to ${printerName}`,
      });
      setTimeout(() => setPrintStatus((cur) => (cur?.phase === "done" ? null : cur)), 2000);
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      setPrintStatus({
        phase: "error",
        text: items.length === 1 ? msg : `${printedIds.length} of ${items.length} labels sent.\n${msg}`,
      });
    } finally {
      // Dev builds simulate the print but still tick, so the flow can be tested end to end.
      if (printedIds.length > 0) {
        await markPalletsPrinted(printedIds).catch(() => undefined);
        await reloadPallets(batch.id);
      }
    }
  }

  async function onPrintUnprinted() {
    if (!selected) return;
    const items = pallets
      .map((p, i) => ({ pallet: p, seq: i + 1 }))
      .filter(({ pallet }) => !pallet.printed);
    if (items.length === 0) return;
    const ok = await touchChoiceDialog({
      icon: "🖨",
      title: `Print ${items.length} label${items.length === 1 ? "" : "s"}?`,
      choices: [
        { value: "cancel", text: "Cancel" },
        { value: "print", text: "Yes, print", tone: "primary" },
      ],
    });
    if (ok === "print") await printLabels(items, selected);
  }

  async function onDeletePallet() {
    const pallet = pallets.find((p) => p.id === selectedPalletId);
    if (!pallet) return;
    if (!(await confirmDialog(`Delete pallet ${pallet.uniqueCode} (${pallet.weight.toLocaleString(undefined, { maximumFractionDigits: 2 })} kg)?`)))
      return;
    try {
      await deleteBatchPallet(pallet.id);
      setSelectedPalletId(null);
      if (selectedId !== null) setPallets(await getBatchPallets(selectedId).catch(() => []));
      await refresh();
    } catch (err) {
      void messageDialog("Failed to delete the pallet.\n" + (err as Error).message);
    }
  }

  async function onReprintLabel() {
    const pallet = pallets.find((p) => p.id === selectedPalletId);
    if (!pallet || !selected) return;
    await printLabels([{ pallet, seq: pallets.findIndex((p) => p.id === pallet.id) + 1 }], selected, true);
  }

  async function onDeleteBatch() {
    if (!selected) return;
    const label = `batch #${selected.id}${selected.orderNumber ? ` (${selected.orderNumber})` : ""}`;
    const warning =
      selected.palletsWeighed > 0
        ? `Delete ${label}?\n\nWARNING: its ${selected.palletsWeighed} weighed pallet${selected.palletsWeighed === 1 ? "" : "s"} will be deleted too.`
        : `Delete ${label}?`;
    if (!(await confirmDialog(warning))) return;
    try {
      await deletePalletBatch(selected.id);
      setSelectedId(null);
      await refresh();
    } catch (err) {
      void messageDialog("Failed to delete the batch.\n" + (err as Error).message);
    }
  }

  const selectedProduct = products.find((p) => p.id === productId) ?? null;

  // Live product filter over description + GTIN (the selected product always stays visible).
  const search = productSearch.trim().toLowerCase();
  const filteredProducts = search
    ? products.filter(
        (p) =>
          p.id === productId ||
          (p.description ?? "").toLowerCase().includes(search) ||
          (p.gtin ?? "").toLowerCase().includes(search),
      )
    : products;

  const fmtDate = (iso: string | null) =>
    iso
      ? new Date(iso).toLocaleString(undefined, {
          day: "2-digit",
          month: "short",
          hour: "2-digit",
          minute: "2-digit",
        })
      : "";

  // ---- New Batch ----
  if (view === "new") {
    return (
      <div className="tt-app">
        <TopBar title="Manual Stock — New Batch" onBack={() => setView("list")} hideBrand />
        <div className="tt-body" style={{ display: "flex", gap: 16 }}>
          {/* Finished product picker */}
          <div className="tt-panel" style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
            <div className="tt-col-title" style={{ marginBottom: 10 }}>Product (finished)</div>
            <div style={{ display: "flex", gap: 8, marginBottom: 10 }}>
              <input
                className="tt-input"
                style={{ minHeight: 44, fontSize: 16 }}
                placeholder="Search product or GTIN…"
                value={productSearch}
                onChange={(e) => setProductSearch(e.target.value)}
              />
              {/* On-screen keyboard for touch screens without a physical one. */}
              <button
                className="tt-btn tt-ghost"
                style={{ minHeight: 44, padding: "0 14px", flex: "none" }}
                onClick={async () => {
                  const v = await keyboard({ title: "Search Product", initial: productSearch });
                  if (v !== null) setProductSearch(v);
                }}
              >
                ⌨
              </button>
              {productSearch && (
                <button
                  className="tt-btn tt-ghost"
                  style={{ minHeight: 44, padding: "0 14px", flex: "none" }}
                  onClick={() => setProductSearch("")}
                >
                  ✕
                </button>
              )}
            </div>
            <div className="tt-list" style={{ flex: 1 }}>
              {products.length === 0 && <div className="tt-note">Loading products…</div>}
              {products.length > 0 && filteredProducts.length === 0 && (
                <div className="tt-note">No products match “{productSearch}”.</div>
              )}
              {filteredProducts.map((p) => (
                <div
                  key={p.id}
                  className={`tt-list-item${productId === p.id ? " tt-selected" : ""}`}
                  style={{ padding: "7px 14px", display: "flex", alignItems: "baseline", gap: 10 }}
                  onClick={() => pickProduct(p)}
                >
                  <span style={{ fontWeight: 600, fontSize: 15 }}>{p.description}</span>
                  <span style={{ fontSize: 12, color: "var(--tt-text-dim)", whiteSpace: "nowrap" }}>
                    {[p.gtin, p.size != null ? `Size ${p.size}` : null, p.upp ? `UPP ${p.upp}` : null]
                      .filter(Boolean)
                      .join("  ·  ")}
                  </span>
                </div>
              ))}
            </div>
          </div>

          {/* Batch details */}
          <div className="tt-panel" style={{ width: 400, flex: "none", display: "flex", flexDirection: "column", gap: 12 }}>
            <div className="tt-col-title">Batch Details</div>

            <div className="tt-list-item" onClick={() => void enterOrderNumber()}>
              <div style={{ fontSize: 13, color: "var(--tt-text-dim)" }}>Order Number</div>
              <div style={{ fontWeight: 600, minHeight: 24 }}>{orderNumber || "Tap to enter"}</div>
            </div>

            <div className="tt-list-item" onClick={() => void enterPalletCount()}>
              <div style={{ fontSize: 13, color: "var(--tt-text-dim)" }}>Number of Pallets</div>
              <div style={{ fontWeight: 600, minHeight: 24 }}>{palletCount ?? "Tap to enter"}</div>
            </div>

            <div className="tt-list-item" style={{ cursor: "default" }}>
              <div style={{ fontSize: 13, color: "var(--tt-text-dim)" }}>Product</div>
              <div style={{ fontWeight: 600, minHeight: 24 }}>
                {selectedProduct?.description ?? "Select from the list"}
              </div>
            </div>

            {/* Whole row is the tap target — the switch alone is small for a gloved finger. */}
            <div
              className="tt-list-item"
              style={{ display: "flex", alignItems: "center", gap: 12 }}
              onClick={() => setRequireIndividual((v) => !v)}
            >
              <div style={{ flex: 1 }}>
                <div style={{ fontWeight: 600 }}>Require Individual Weight</div>
                <div style={{ fontSize: 12, color: "var(--tt-text-dim)" }}>
                  {requireIndividual
                    ? "Weigh each pallet on the scale"
                    : "All pallets created at one weight"}
                </div>
              </div>
              <div className={`tt-toggle${requireIndividual ? " tt-on" : ""}`}>
                <div className="tt-knob" />
              </div>
            </div>

            {!requireIndividual && (
              <div className="tt-list-item" onClick={() => void enterPalletWeight()}>
                <div style={{ fontSize: 13, color: "var(--tt-text-dim)" }}>
                  Pallet Weight (kg)
                  {palletWeight !== null && selectedProduct?.averageWeight === palletWeight
                    ? " · product average"
                    : ""}
                </div>
                <div style={{ fontWeight: 600, minHeight: 24, fontVariantNumeric: "tabular-nums" }}>
                  {palletWeight !== null
                    ? `${palletWeight.toLocaleString(undefined, { maximumFractionDigits: 2 })} kg  ✎`
                    : !productId
                      ? "Select a product first"
                      : "No average weight — tap to enter"}
                </div>
              </div>
            )}

            <div style={{ flex: 1 }} />
            <button
              className="tt-btn tt-primary"
              style={{ minHeight: 64, fontSize: 20 }}
              disabled={
                saving || !productId || !palletCount || (!requireIndividual && !(palletWeight && palletWeight > 0))
              }
              onClick={() => void create()}
            >
              {saving ? "Creating…" : "Create Batch"}
            </button>
            <button className="tt-btn tt-ghost" onClick={() => setView("list")}>Cancel</button>
          </div>
        </div>
      </div>
    );
  }

  // ---- Batch list + pallets ----
  const complete = selected !== null && pallets.length >= selected.palletCount;
  const unprinted = pallets.filter((p) => !p.printed).length;

  return (
    <div className="tt-app">
      <TopBar
        title="Manual Stock"
        onBack={onHome}
        hideBrand
        right={
          <button className="tt-btn tt-primary" style={{ minHeight: 48 }} onClick={() => void openNew()}>
            ＋ New Batch
          </button>
        }
      />
      <div className="tt-body" style={{ display: "flex", gap: 12 }}>
        {/* Batches */}
        <div className="tt-panel" style={{ width: 380, flex: "none", display: "flex", flexDirection: "column", minWidth: 0 }}>
          <div className="tt-col-title" style={{ marginBottom: 10 }}>Batches</div>
          {error && <div className="tt-error" style={{ marginBottom: 10 }}>{error}</div>}
          <div className="tt-list" style={{ flex: 1 }}>
            {loading && batches.length === 0 && <div className="tt-note">Loading…</div>}
            {!loading && batches.length === 0 && !error && (
              <div className="tt-note">No batches yet — tap New Batch.</div>
            )}
            {batches.map((b) => {
              const done = b.palletsWeighed >= b.palletCount;
              return (
                <div
                  key={b.id}
                  className={`tt-list-item${selectedId === b.id ? " tt-selected" : ""}`}
                  style={{ padding: "9px 14px" }}
                  onClick={() => setSelectedId(b.id)}
                >
                  <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                    <span style={{ fontWeight: 600, fontSize: 15, flex: 1 }}>
                      #{b.id}{b.orderNumber ? ` · ${b.orderNumber}` : ""}
                    </span>
                    <span
                      style={{
                        fontSize: 13,
                        fontVariantNumeric: "tabular-nums",
                        color: done ? "var(--tt-green)" : "var(--tt-amber)",
                        fontWeight: 600,
                      }}
                    >
                      {b.palletsWeighed}/{b.palletCount}
                    </span>
                  </div>
                  <div style={{ fontSize: 12, color: "var(--tt-text-dim)" }}>
                    {b.product} · {fmtDate(b.dateCreated)}
                  </div>
                </div>
              );
            })}
          </div>
          {selected !== null && (
            // Discreet destructive action — ghost red (the Exit-button idiom), not a solid block.
            <button
              className="tt-btn tt-ghost tt-exit"
              style={{ marginTop: 10, minHeight: 40, fontSize: 13, padding: "0 14px", alignSelf: "flex-end" }}
              onClick={() => void onDeleteBatch()}
            >
              🗑 Delete Batch #{selected.id}
            </button>
          )}
        </div>

        {/* Selected batch's pallets */}
        <div className="tt-panel" style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
          {selected === null ? (
            <div className="tt-center" style={{ height: "100%" }}>
              <div className="tt-note" style={{ fontSize: 16 }}>Select a batch to see its pallets.</div>
            </div>
          ) : (
            <>
              <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 10 }}>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontWeight: 600, fontSize: 17 }}>
                    Batch #{selected.id}{selected.orderNumber ? ` — ${selected.orderNumber}` : ""}
                  </div>
                  <div style={{ fontSize: 13, color: "var(--tt-text-dim)" }}>
                    {selected.product} · {pallets.length} of {selected.palletCount} pallets
                    {selected.requireIndividualWeight
                      ? " weighed"
                      : ` at the average weight (${(selected.palletWeight ?? 0).toLocaleString(undefined, { maximumFractionDigits: 2 })} kg)`}
                  </div>
                </div>
                {unprinted > 0 && (
                  <button
                    className="tt-btn"
                    style={{ minHeight: 54, fontSize: 17 }}
                    onClick={() => void onPrintUnprinted()}
                  >
                    🖨 Print Unprinted ({unprinted})
                  </button>
                )}
                <button
                  className="tt-btn tt-primary"
                  style={{ minHeight: 54, fontSize: 17 }}
                  disabled={complete}
                  onClick={() => setWeighing(true)}
                >
                  ⚖ Add / Weigh Pallet
                </button>
              </div>
              {complete && (
                <div className="tt-note" style={{ marginBottom: 8, color: "var(--tt-green)" }}>
                  ✓ Batch complete — all {selected.palletCount} pallets {selected.requireIndividualWeight ? "weighed" : "created"}.
                </div>
              )}
              <div className="tt-grid-wrap">
                <table className="tt-grid">
                  <thead>
                    <tr>
                      <th>#</th>
                      <th>Pallet Code</th>
                      <th className="tt-center-col">Weight (kg)</th>
                      <th className="tt-col-grow">Created</th>
                      <th className="tt-center-col">Printed</th>
                    </tr>
                  </thead>
                  <tbody>
                    {pallets.map((p, i) => (
                      <tr
                        key={p.id}
                        // Tap to select for deletion; tap again to deselect.
                        onClick={() => setSelectedPalletId((cur) => (cur === p.id ? null : p.id))}
                        style={{
                          cursor: "pointer",
                          background:
                            selectedPalletId === p.id ? "rgba(var(--tt-green-rgb), 0.22)" : undefined,
                        }}
                      >
                        <td>{i + 1}</td>
                        <td>{p.uniqueCode}</td>
                        <td className="tt-center-col" style={{ fontVariantNumeric: "tabular-nums" }}>
                          {p.weight.toLocaleString(undefined, { maximumFractionDigits: 2 })}
                        </td>
                        <td className="tt-col-grow">{fmtDate(p.dateCreated)}</td>
                        <td
                          className="tt-center-col"
                          style={{ fontWeight: 700, color: p.printed ? "var(--tt-green)" : "var(--tt-text-dim)" }}
                        >
                          {p.printed ? "✓" : "—"}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {pallets.length === 0 && (
                  <div className="tt-note" style={{ padding: 18 }}>
                    No pallets weighed yet — tap Add / Weigh Pallet.
                  </div>
                )}
              </div>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", paddingTop: 10, minHeight: 58 }}>
                {/* Only visible once a pallet row is tapped (batch delete works the same way). */}
                {selectedPalletId !== null ? (
                  <div style={{ display: "flex", gap: 10 }}>
                    <button
                      className="tt-btn tt-ghost tt-exit"
                      style={{ minHeight: 40, fontSize: 13, padding: "0 14px" }}
                      onClick={() => void onDeletePallet()}
                    >
                      🗑 Delete Pallet
                    </button>
                    <button
                      className="tt-btn tt-ghost"
                      style={{ minHeight: 40, fontSize: 13, padding: "0 14px" }}
                      onClick={() => void onReprintLabel()}
                    >
                      🖨 Reprint Label
                    </button>
                  </div>
                ) : (
                  <span />
                )}
                <div style={{ display: "flex", gap: 20 }}>
                  <div className="tt-note">Pallets = {pallets.length}</div>
                  <div className="tt-note">
                    Total weight = {pallets.reduce((s, p) => s + p.weight, 0).toLocaleString(undefined, { maximumFractionDigits: 2 })} kg
                  </div>
                </div>
              </div>
            </>
          )}
        </div>
      </div>

      {weighing && selected && (
        <WeighPalletDialog
          batch={selected}
          nextSeq={pallets.length + 1}
          onClose={(created) => (created ? void onPalletAdded(created) : setWeighing(false))}
        />
      )}

      {/* Dev-only: what Reprint Label would have sent to the printer, at print proportions. */}
      {devLabelPreview && (
        <div className="tt-pad-overlay" style={{ zIndex: 80 }} onClick={() => setDevLabelPreview(null)}>
          <div
            className="tt-panel"
            style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 14, padding: 24 }}
            onClick={(e) => e.stopPropagation()}
          >
            <div className="tt-col-title">Dev build — label {devLabelPreview.code} (not printed)</div>
            <img
              src={devLabelPreview.src}
              alt={`Label ${devLabelPreview.code}`}
              style={{ width: "min(900px, 88vw)", maxHeight: "72vh", objectFit: "contain", background: "#fff", border: "1px solid var(--tt-line)" }}
            />
            <button className="tt-btn tt-primary" style={{ minHeight: 54, minWidth: 220 }} onClick={() => setDevLabelPreview(null)}>
              Close
            </button>
          </div>
        </div>
      )}

      {/* Print status: busy while sending (dev mode simulates), auto-dismissing on success. */}
      {printStatus && (
        <div className="tt-pad-overlay" style={{ zIndex: 80 }}>
          <div className="tt-pad" style={{ width: "min(520px, 92vw)", textAlign: "center" }}>
            <div style={{ fontSize: 62, marginBottom: 8 }}>
              {printStatus.phase === "printing" ? "🖨" : printStatus.phase === "done" ? "✅" : "⚠️"}
            </div>
            <div style={{ fontSize: 20, fontWeight: 600, marginBottom: 6 }}>
              {printStatus.phase === "printing"
                ? "Printing…"
                : printStatus.phase === "done"
                  ? "Label sent"
                  : "Print failed"}
            </div>
            <div className="tt-note" style={{ fontSize: 14 }}>{printStatus.text}</div>
            {printStatus.phase === "error" && (
              <button
                className="tt-btn"
                style={{ marginTop: 16, minHeight: 48 }}
                onClick={() => setPrintStatus(null)}
              >
                OK
              </button>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

// Popup: live weight from this PC's weighscale config; Capture inserts the pallet. The
// connection lives only while the popup is open.
function WeighPalletDialog({
  batch,
  nextSeq,
  onClose,
}: {
  batch: PalletBatchRow;
  nextSeq: number;
  // The created pallet on capture (so the parent can auto-print its label), null on cancel.
  onClose: (created: ManualPallet | null) => void;
}) {
  const config = getSavedWeighscaleConfig();
  const [connected, setConnected] = useState(false);
  const [connError, setConnError] = useState<string | null>(null);
  const [weight, setWeight] = useState<number | null>(null);
  // Fallback for a dead/absent scale: switch to keying the weight in by hand.
  // No configured scale on this PC forces manual entry from the start.
  const [entryMode, setEntryMode] = useState<"live" | "manual">(() =>
    config !== null && config.parse !== undefined ? "live" : "manual",
  );
  const [manualWeight, setManualWeight] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);
  // Standard bulk density (kg/m³) from dbo.Setting — peat is sold by volume, so the popup
  // shows the m³ the captured weight represents.
  const [bulkDensity, setBulkDensity] = useState<number | null>(null);
  const tailRef = useRef("");

  useEffect(() => {
    getManualStockConfig()
      .then((c) => setBulkDensity(c.bulkDensity > 0 ? c.bulkDensity : null))
      .catch(() => setBulkDensity(null));
  }, []);

  const decimals = config?.parse?.decimals ?? 0;
  const configured = config !== null && config.parse !== undefined;

  async function connect() {
    setConnError(null);
    try {
      await connectWeighscale(config!);
      // The Rust side emits its "connected" status DURING the connect command — belt and
      // braces here in case that event fired before our listener was attached.
      setConnected(true);
    } catch (err) {
      setConnError(String(err));
    }
  }

  useEffect(() => {
    if (!configured || !inTauriShell()) return;
    let cancelled = false;
    let offData: (() => void) | null = null;
    let offStatus: (() => void) | null = null;
    (async () => {
      // Listeners must be attached BEFORE connecting — the connected status fires during
      // the connect call, and a late listener misses it (popup stuck on "Connecting…").
      const d = await onWeighscaleData((text) => {
        const parts = (tailRef.current + text).split(/[\r\n]+/);
        tailRef.current = parts.pop() ?? "";
        const frames = parts.map((p) => p.replace(/[\x00-\x1f\x7f]/g, "")).filter((p) => p.trim());
        const last = frames[frames.length - 1];
        if (last && config?.parse) {
          const w = parseWeight(last, config.parse);
          if (w !== null) setWeight(w);
        }
      });
      const s = await onWeighscaleStatus((st) => {
        setConnected(st.connected);
        if (st.error) setConnError(st.error);
      });
      if (cancelled) {
        d();
        s();
        return;
      }
      offData = d;
      offStatus = s;
      await connect();
    })();
    return () => {
      cancelled = true;
      offData?.();
      offStatus?.();
      void disconnectWeighscale();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function capture() {
    if (activeWeight === null || activeWeight <= 0) return;
    setSaving(true);
    try {
      const created = await addBatchPallet(batch.id, activeWeight);
      onClose(created);
    } catch (err) {
      void messageDialog("Failed to save the pallet.\n" + (err as Error).message);
      setSaving(false);
    }
  }

  async function enterManualWeight() {
    const v = await keypad({
      title: "Pallet Weight (kg)",
      decimal: true,
      initial: manualWeight !== null ? String(manualWeight) : "",
    });
    if (v === null) return;
    const n = parseFloat(v);
    if (!Number.isFinite(n) || n <= 0) {
      void messageDialog("Weight must be a number greater than zero.");
      return;
    }
    setManualWeight(n);
  }

  // Edit the standard bulk density in place (persists to dbo.Setting for everyone).
  async function editDensity() {
    const v = await keypad({
      title: "Bulk Density (kg/m³)",
      decimal: true,
      initial: bulkDensity !== null ? String(bulkDensity) : "",
    });
    if (v === null) return;
    const n = parseFloat(v);
    if (!Number.isFinite(n) || n <= 0) {
      void messageDialog("Bulk density must be a number greater than zero.");
      return;
    }
    try {
      const saved = await setManualStockConfig(n);
      setBulkDensity(saved.bulkDensity);
    } catch (err) {
      void messageDialog("Failed to save bulk density.\n" + (err as Error).message);
    }
  }

  const live = connected && weight !== null;
  // Whichever entry mode is active drives the volume, the Save button and the label preview.
  const activeWeight = entryMode === "live" ? weight : manualWeight;
  const activeDecimals = entryMode === "live" ? decimals : 2;
  const canCapture = entryMode === "live" ? live : manualWeight !== null && manualWeight > 0;
  const volume = activeWeight !== null && bulkDensity !== null ? activeWeight / bulkDensity : null;
  // The code this pallet will get on capture (matches the server's M{batch}-{seq}-{ddMMyyyy} series).
  const now = new Date();
  const palletCode = `M${batch.id}-${String(nextSeq).padStart(3, "0")}-${String(now.getDate()).padStart(2, "0")}${String(now.getMonth() + 1).padStart(2, "0")}${now.getFullYear()}`;

  return (
    <div className="tt-pad-overlay">
      <div className="tt-panel" style={{ width: "97vw", height: "95vh", display: "flex", gap: 20 }}>
        {/* Left: the weigh flow */}
        <div style={{ flex: 1, minWidth: 0, display: "flex", flexDirection: "column", gap: 16, alignItems: "center", justifyContent: "center" }}>
        <div style={{ textAlign: "center" }}>
          <div style={{ fontWeight: 600, fontSize: 19 }}>
            Weigh Pallet {nextSeq} of {batch.palletCount}
          </div>
          <div style={{ fontSize: 14, color: "var(--tt-text-dim)", marginTop: 2 }}>
            Batch #{batch.id}{batch.orderNumber ? ` — ${batch.orderNumber}` : ""} · {batch.product}
          </div>
        </div>

        {/* Live scale vs keyed-in weight (fallback when the scale is down). */}
        <div style={{ display: "flex", gap: 10 }}>
          <button
            className={`tt-btn${entryMode === "live" ? " tt-primary" : " tt-ghost"}`}
            style={{ minHeight: 46, fontSize: 15 }}
            disabled={!configured}
            title={configured ? undefined : "No weighscale is set up on this PC"}
            onClick={() => setEntryMode("live")}
          >
            ⚖ Live Scale
          </button>
          <button
            className={`tt-btn${entryMode === "manual" ? " tt-primary" : " tt-ghost"}`}
            style={{ minHeight: 46, fontSize: 15 }}
            onClick={() => setEntryMode("manual")}
          >
            ✎ Manual Entry
          </button>
        </div>
        {!configured && (
          <div className="tt-note" style={{ textAlign: "center" }}>
            No weighscale is set up on this PC — manual entry only.
          </div>
        )}

        {entryMode === "live" ? (
          <>
            <div className={`tt-gauge${live ? "" : " tt-stale"}`}>
              <div className="tt-gauge-val">
                {weight === null ? "----" : weight.toFixed(decimals)}
                <span className="tt-gauge-unit"> kg</span>
              </div>
              <div className={`tt-gauge-status${live ? "" : " tt-bad"}`}>
                {live ? "Live weight" : connError ?? "Connecting to the scale…"}
              </div>
            </div>
            {!live && connError && (
              <button className="tt-btn" style={{ minHeight: 44 }} onClick={() => void connect()}>
                ⟳ Retry connection
              </button>
            )}
          </>
        ) : (
          <>
            <div className={`tt-gauge${manualWeight !== null ? "" : " tt-stale"}`}>
              <div className="tt-gauge-val">
                {manualWeight === null ? "----" : manualWeight.toFixed(2)}
                <span className="tt-gauge-unit"> kg</span>
              </div>
              <div className="tt-gauge-status">Manually entered weight</div>
            </div>
            <button className="tt-btn" style={{ minHeight: 44 }} onClick={() => void enterManualWeight()}>
              ✎ Enter Weight
            </button>
          </>
        )}

        <>
            {/* The volume calculation, spelled out with live numbers so operators see HOW
                the m³ comes from the weight (peat sells by volume). Density is editable
                in place and persists to dbo.Setting. */}
            <div style={{ textAlign: "center", width: "100%" }}>
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  gap: 10,
                  fontSize: 21,
                  fontVariantNumeric: "tabular-nums",
                  flexWrap: "wrap",
                }}
              >
                <span>{activeWeight === null ? "—" : activeWeight.toFixed(activeDecimals)} kg</span>
                <span style={{ color: "var(--tt-text-dim)" }}>÷</span>
                <span style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
                  {bulkDensity === null
                    ? "—"
                    : bulkDensity.toLocaleString(undefined, { maximumFractionDigits: 2 })}{" "}
                  kg/m³
                  <button
                    className="tt-btn tt-ghost"
                    style={{ minHeight: 38, padding: "0 11px", fontSize: 16 }}
                    title="Edit bulk density"
                    onClick={() => void editDensity()}
                  >
                    ✎
                  </button>
                </span>
                <span style={{ color: "var(--tt-text-dim)" }}>=</span>
                <span style={{ fontWeight: 700, fontSize: 27, color: "var(--tt-green)" }}>
                  {volume === null ? "—" : volume.toFixed(2)} m³
                </span>
              </div>
              <div style={{ fontSize: 12, color: "var(--tt-text-dim)", marginTop: 4 }}>
                Weight (kg) ÷ Bulk Density (kg/m³) = Volume (m³)
              </div>
            </div>

            <button
              className="tt-btn tt-primary"
              style={{ minHeight: 64, fontSize: 20, width: "80%" }}
              disabled={!canCapture || saving}
              onClick={() => void capture()}
            >
              {saving ? "Saving…" : "Save Pallet"}
            </button>
        </>

          <button className="tt-btn tt-ghost" style={{ width: "80%" }} onClick={() => onClose(null)}>
            Cancel
          </button>
        </div>

        {/* Right: label preview at print proportions, filled live from the weigh in progress.
            Same template as a Production reprint (ProductionLabelPreview / renderProductionLabelCanvas). */}
        <div style={{ flex: "none", display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: 10 }}>
          <div className="tt-col-title">Label Preview — 15 × 10 cm</div>
          <ProductionLabelPreview
            d={{
              product: batch.product ?? "",
              orderNumber: batch.orderNumber,
              // Weight is not printed, only carried in the DataMatrix payload.
              netWeightKg: activeWeight ?? 0,
              size: batch.size,
              upp: batch.upp,
              gtin: batch.gtin ?? "",
              palletCode,
            }}
          />
        </div>
      </div>
    </div>
  );
}
