import { useEffect, useRef, useState } from "react";
import TopBar from "../components/touch/TopBar";
import BigButton from "../components/touch/BigButton";
import WeightGauge from "../components/touch/WeightGauge";
import { keyboard } from "../components/touch/Keyboard";
import { keypad, KeypadPanel } from "../components/touch/Keypad";
import { IconBarrier, IconBox, IconTruck } from "../components/touch/TouchIcons";
import { messageDialog } from "../components/confirmDialog";
import {
  BogOption,
  CollectionLoad,
  DeviceConfig,
  getCollection,
  getGateBarrierConfig,
  getHauliers,
  getLiveWeight,
  getProducts,
  getProductSuppliers,
  getRelayState,
  getSupplierBogs,
  HaulierOption,
  ProductOption,
  RelayState,
  setBarrier,
  SupplierOption,
  weighCollection,
  weighInDelivery,
} from "../api/touch";
import { requireBarrierPin } from "../components/touch/barrierPin";
import SuperAdminGear from "../components/touch/SuperAdminGear";
import DbSwitcher from "../components/DbSwitcher";

type Step =
  | "menu"
  | "delivery-haulier"
  | "delivery-select"
  | "delivery-weigh"
  | "collection-ref"
  | "collection-weigh";

interface Props {
  device: DeviceConfig;
  onOpenSettings: () => void;
}

// Barrier auto-close, lifted from the WPF WeighbridgeInViewModel: once opened, hold the
// barrier until the live weight has stayed under 150 kg for 2 seconds (the vehicle is off
// the bridge), giving up after an hour and closing anyway.
const BARRIER_CLEAR_KG = 150;
const BARRIER_STABLE_MS = 2000;
const BARRIER_TIMEOUT_MS = 60 * 60 * 1000;
const BARRIER_POLL_MS = 500; // same cadence as WeightGauge — one shared weight proxy
const RELAY_POLL_MS = 2000;

// The post-weigh barrier flow drives the real relay on its own, with nobody tapping
// anything, so dev builds leave it alone — the rule the printer and the alarm siren already
// follow. The Open Barrier button stays live in dev: that one is a deliberate tap.
const POST_WEIGH_BARRIER_ENABLED = !import.meta.env.DEV;

const delay = (ms: number) => new Promise((r) => window.setTimeout(r, ms));

// Waits for the bridge to read clear. Returns true if it did, false on timeout or cancel.
// A failed weight read is not "clear" — a blip must never close the barrier on a vehicle.
async function waitUntilBridgeClear(cancelled: () => boolean): Promise<boolean> {
  const started = Date.now();
  let clearSince: number | null = null;
  while (Date.now() - started < BARRIER_TIMEOUT_MS) {
    if (cancelled()) return false;
    let w: number | null = null;
    try {
      const r = await getLiveWeight();
      const n = typeof r.weight === "number" ? r.weight : parseFloat(String(r.weight ?? ""));
      if (Number.isFinite(n)) w = n;
    } catch {
      // Service blip — keep waiting.
    }
    if (w !== null && w < BARRIER_CLEAR_KG) {
      if (clearSince === null) clearSince = Date.now();
      if (Date.now() - clearSince >= BARRIER_STABLE_MS) return true;
    } else {
      clearSince = null;
    }
    await delay(BARRIER_POLL_MS);
  }
  return false;
}

// Weigh-In screen. Mirrors the WPF WeighbridgeInView (Delivery / Collection) + the shared
// weigh step. Live weight comes from the gate-barrier proxy; the weigh button is disabled
// until the scale reads live, exactly like the desktop CanSave guard. Kiosk: no login.
export default function WeighInScreen({ device, onOpenSettings }: Props) {
  const [step, setStep] = useState<Step>("menu");

  // "Show Hauliers on one screen" (Screen Settings): the haulier is picked on a screen of
  // its own, then Product / Supplier / Bog share the next one. Off = all four at once.
  const splitHaulier = device.haulierOwnScreen;

  // Live weight, kept in a ref so the weigh action reads the latest value at click time.
  const weightRef = useRef<number | null>(null);
  const [healthy, setHealthy] = useState(false);
  const onWeight = (w: number | null, ok: boolean) => {
    weightRef.current = w;
    setHealthy(ok);
  };

  // ---- Delivery state ----
  const [hauliers, setHauliers] = useState<HaulierOption[]>([]);
  const [products, setProducts] = useState<ProductOption[]>([]);
  const [suppliers, setSuppliers] = useState<SupplierOption[]>([]);
  const [bogs, setBogs] = useState<BogOption[]>([]);
  const [haulier, setHaulier] = useState<HaulierOption | null>(null);
  const [product, setProduct] = useState<ProductOption | null>(null);
  const [supplier, setSupplier] = useState<SupplierOption | null>(null);
  const [bog, setBog] = useState<BogOption | null>(null);
  const [vehicleReg, setVehicleReg] = useState("");
  const [docketChoice, setDocketChoice] = useState<boolean | null>(null);
  const [docketNumber, setDocketNumber] = useState("");

  // ---- Collection state ----
  const [load, setLoad] = useState<CollectionLoad | null>(null);
  const [trailerNo, setTrailerNo] = useState("");
  const [refError, setRefError] = useState<string | null>(null);

  // ---- Barrier state (relay 2, driven by GlobeweighService through the API proxy) ----
  const [relay, setRelay] = useState<RelayState>({ connected: false, gateOpen: false, barrierOpen: false });
  const [barrierBusy, setBarrierBusy] = useState(false);
  // True while we are holding the barrier open waiting for the vehicle to clear the bridge.
  const [holdingOpen, setHoldingOpen] = useState(false);
  const cancelHoldRef = useRef(false);
  const relayPollRef = useRef(false);
  // Read inside onBarrier: the pass-code prompt can sit open while the poll (or the Admin
  // page) moves the barrier, and the captured render value would be stale by then.
  const relayRef = useRef(relay);
  relayRef.current = relay;

  // GlobalDbSettings.GateEnabled in the WPF app: sites without a gate just show the overlay.
  const [gateEnabled, setGateEnabled] = useState(false);

  const [busy, setBusy] = useState(false);
  const [overlay, setOverlay] = useState<string | null>(null);
  const [countdown, setCountdown] = useState<number | null>(null);

  // Poll the relay so the button always shows the real barrier state — it can also be
  // worked from the Admin Gate/Barrier page. Skips a tick while one is still in flight,
  // the same guard the Admin page uses, because a disconnected relay makes calls slow.
  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      if (relayPollRef.current) return;
      relayPollRef.current = true;
      try {
        const s = await getRelayState();
        if (!cancelled) setRelay(s);
      } catch {
        if (!cancelled) setRelay((p) => ({ ...p, connected: false }));
      } finally {
        relayPollRef.current = false;
      }
    };
    void tick();
    const id = window.setInterval(tick, RELAY_POLL_MS);
    return () => {
      cancelled = true;
      window.clearInterval(id);
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    getGateBarrierConfig()
      .then((c) => { if (!cancelled) setGateEnabled(c.gateEnabled); })
      .catch(() => { if (!cancelled) setGateEnabled(false); });
    return () => { cancelled = true; };
  }, []);

  async function sendBarrier(open: boolean): Promise<boolean> {
    setBarrierBusy(true);
    try {
      setRelay(await setBarrier(open));
      return true;
    } catch (err) {
      void messageDialog(`Failed to ${open ? "open" : "close"} the barrier.\n` + (err as Error).message);
      return false;
    } finally {
      setBarrierBusy(false);
    }
  }

  // Mirrors WeighbridgeInViewModel.OnOpenBarrier: pass code first, then open and hold until
  // the vehicle is off the bridge before closing. Tapping again while it is open closes it
  // straight away (and abandons the hold).
  async function onBarrier() {
    if (barrierBusy) return;
    if (!(await requireBarrierPin())) return;

    if (relayRef.current.barrierOpen) {
      cancelHoldRef.current = true;
      await sendBarrier(false);
      return;
    }

    if (!(await sendBarrier(true))) return;
    cancelHoldRef.current = false;
    setHoldingOpen(true);
    try {
      await waitUntilBridgeClear(() => cancelHoldRef.current);
      if (!cancelHoldRef.current) await sendBarrier(false);
    } finally {
      setHoldingOpen(false);
    }
  }

  // Success countdown → back to menu.
  useEffect(() => {
    if (countdown === null) return;
    if (countdown <= 0) {
      resetAll();
      setOverlay(null);
      setCountdown(null);
      setStep("menu");
      return;
    }
    const t = window.setTimeout(() => setCountdown((c) => (c === null ? null : c - 1)), 1000);
    return () => window.clearTimeout(t);
  }, [countdown]);

  function resetAll() {
    setHaulier(null); setProduct(null); setSupplier(null); setBog(null);
    setSuppliers([]); setBogs([]);
    setVehicleReg(""); setDocketChoice(null); setDocketNumber("");
    setLoad(null); setTrailerNo(""); setRefError(null);
  }

  // Post-weigh flow, from the WPF RunPostWeighFlowAsync: show the overlay, and where the
  // site has a gate, open the barrier and hold it until the vehicle is off the bridge before
  // closing it and returning to the menu. Otherwise it is the plain 5-second countdown.
  // Deliberately not awaited by the weigh handlers — the overlay is already covering the
  // screen, and the wait can run for as long as the driver takes.
  async function succeed(message: string) {
    setOverlay(message);

    const runBarrier = POST_WEIGH_BARRIER_ENABLED && gateEnabled && relayRef.current.connected;
    if (!runBarrier) {
      setCountdown(5);
      return;
    }

    setHoldingOpen(true);
    try {
      if (!relayRef.current.barrierOpen) setRelay(await setBarrier(true));
      await waitUntilBridgeClear(() => false);
      setRelay(await setBarrier(false));
    } catch (err) {
      // A relay fault must never strand the driver behind a dialog on a kiosk screen —
      // the WPF version logged it and carried on, so do the same.
      console.error("post-weigh barrier flow failed", err);
    } finally {
      setHoldingOpen(false);
      resetAll();
      setOverlay(null);
      setCountdown(null);
      setStep("menu");
    }
  }

  // ---- Delivery: open the selection step ----
  async function startDelivery() {
    resetAll();
    setStep(splitHaulier ? "delivery-haulier" : "delivery-select");
    try {
      const [hs, ps] = await Promise.all([getHauliers(), getProducts()]);
      setHauliers(hs);
      setProducts(ps);
    } catch (err) {
      void messageDialog("Failed to load lists.\n" + (err as Error).message);
    }
  }

  async function pickProduct(p: ProductOption) {
    setProduct(p);
    setSupplier(null);
    setBog(null);
    setBogs([]);
    try {
      setSuppliers(await getProductSuppliers(p.id));
    } catch {
      setSuppliers([]);
    }
  }

  async function pickSupplier(s: SupplierOption) {
    setSupplier(s);
    setBog(null);
    try {
      setBogs(await getSupplierBogs(s.supplierId));
    } catch {
      setBogs([]);
    }
  }

  // CanNext mirrors the WPF DeliveryInViewModel: haulier + product required; supplier/bog
  // required only when their lists have entries.
  const canNext =
    !!haulier && !!product &&
    (suppliers.length === 0 || !!supplier) &&
    (bogs.length === 0 || !!bog);

  const canWeighDelivery =
    healthy && vehicleReg.trim().length > 0 &&
    docketChoice !== null &&
    (docketChoice === false || docketNumber.trim().length > 0);

  async function doDeliveryWeigh() {
    if (!haulier || weightRef.current === null) return;
    setBusy(true);
    try {
      await weighInDelivery({
        haulierId: haulier.id,
        haulierName: haulier.name,
        product: product?.description ?? null,
        supplier: supplier?.name ?? null,
        bogName: bog?.name ?? null,
        vehicleReg: vehicleReg.trim(),
        docketNumber: docketChoice ? docketNumber.trim() : null,
        weight: weightRef.current,
      });
      void succeed("Weigh successful");
    } catch (err) {
      void messageDialog("Weigh failed.\n" + (err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  // ---- Collection ----
  async function lookupRef(entered: string) {
    if (entered.trim().length === 0) return;
    setBusy(true);
    setRefError(null);
    try {
      const found = await getCollection(entered.trim());
      setLoad(found);
      setTrailerNo("");
      setStep("collection-weigh");
    } catch (err) {
      setRefError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const canWeighCollection = healthy && trailerNo.trim().length > 0 && !!load;

  async function doCollectionWeigh() {
    if (!load || weightRef.current === null) return;
    setBusy(true);
    try {
      await weighCollection(load.id, trailerNo.trim(), weightRef.current);
      void succeed("Weigh successful");
    } catch (err) {
      void messageDialog("Weigh failed.\n" + (err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const oneLineAddress = (a?: string | null) =>
    (a ?? "").split(/\r?\n/).map((l) => l.trim()).filter(Boolean).join(", ");

  async function editReg() {
    const v = await keyboard({ title: "Enter Vehicle Reg", initial: vehicleReg });
    if (v !== null) setVehicleReg(v);
  }
  async function editDocket() {
    const v = await keypad({ title: "Enter Docket Number", initial: docketNumber });
    if (v !== null) setDocketNumber(v);
  }
  async function editTrailer() {
    const v = await keyboard({ title: "Enter Trailer No", initial: trailerNo });
    if (v !== null) setTrailerNo(v);
  }

  // -------------------- render --------------------
  const gauge = <WeightGauge onChange={onWeight} />;

  let backTo: (() => void) | undefined;
  if (step === "delivery-haulier" || step === "collection-ref") backTo = () => { resetAll(); setStep("menu"); };
  if (step === "delivery-select") {
    backTo = splitHaulier
      ? () => setStep("delivery-haulier")
      : () => { resetAll(); setStep("menu"); };
  }
  if (step === "delivery-weigh") backTo = () => setStep("delivery-select");
  if (step === "collection-weigh") backTo = () => { setLoad(null); setStep("collection-ref"); };

  return (
    <div className="tt-app">
      <TopBar
        title="Weigh In"
        onBack={backTo}
        // The haulier is off-screen once it has its own step — keep it in sight.
        right={
          splitHaulier && haulier && step === "delivery-select" ? (
            <span className="tt-note" style={{ fontSize: 15 }}>
              Haulier: <strong style={{ color: "#fff" }}>{haulier.name}</strong>
            </span>
          ) : undefined
        }
      />
      <div className="tt-body">
        {step === "menu" && (
          <div className="tt-center">
            {gauge}
            <div className="tt-bigbtn-grid tt-wb-menu" style={{ maxWidth: 620 }}>
              <BigButton label="Delivery" icon={<IconBox />} sub="Incoming raw material" onClick={startDelivery} />
              <BigButton label="Collection" icon={<IconTruck />} sub="Pickup by reference" onClick={() => { resetAll(); setStep("collection-ref"); }} />
              {/* The third button on the WPF weigh-in screen, in the same tile style as the
                  pair above it — it spans the full width of the grid. */}
              <BigButton
                label={relay.barrierOpen ? "Close Barrier" : "Open Barrier"}
                icon={<IconBarrier />}
                sub={relay.connected ? "Let a vehicle off the bridge" : "Relay not connected"}
                disabled={!relay.connected || barrierBusy}
                onClick={() => void onBarrier()}
              />
            </div>

            {/* The lamp beside the WPF button: red closed, green open. */}
            <div className={`tt-barrier${relay.barrierOpen ? " tt-open" : ""}`}>
              <span className="tt-dot" />
              {!relay.connected ? "Relay not connected" : relay.barrierOpen ? "Barrier open" : "Barrier closed"}
            </div>
            {holdingOpen && (
              <div className="tt-note">Barrier held open — it closes once the vehicle is off the bridge.</div>
            )}
          </div>
        )}

        {step === "delivery-haulier" && (
          <div className="tt-select-step tt-haulier-full">
            {/* Two columns, filled down the first then across, sized so the list never
                scrolls. Tapping a haulier goes straight on; Back returns here. */}
            <SelectColumn title="Haulier" className="tt-col-2col"
                items={hauliers.map((h) => ({ id: h.id, label: h.name }))}
                selectedId={haulier?.id ?? null}
                onPick={(id) => {
                  const h = hauliers.find((x) => x.id === id) ?? null;
                  setHaulier(h);
                  if (h) setStep("delivery-select");
                }} />
          </div>
        )}

        {step === "delivery-select" && (
          <div className="tt-select-step">
            {/* Product / Supplier / Bog once the haulier has its own screen — supplier names
                are the long ones, so that column gets the width Product gives up. */}
            <div className={`tt-cols${splitHaulier ? " tt-cols-psb" : ""}`}>
              {!splitHaulier && (
                <SelectColumn title="Haulier" items={hauliers.map((h) => ({ id: h.id, label: h.name }))}
                  selectedId={haulier?.id ?? null} onPick={(id) => setHaulier(hauliers.find((h) => h.id === id) ?? null)} />
              )}
              <SelectColumn title="Product" items={products.map((p) => ({ id: p.id, label: p.description }))}
                selectedId={product?.id ?? null} onPick={(id) => { const p = products.find((x) => x.id === id); if (p) void pickProduct(p); }} />
              <SelectColumn title="Supplier" className={splitHaulier ? "tt-col-2col tt-col-boxed" : undefined}
                items={suppliers.map((s) => ({ id: s.id, label: s.name }))}
                selectedId={supplier?.id ?? null} emptyNote={product ? "None" : "Select a product"}
                onPick={(id) => { const s = suppliers.find((x) => x.id === id); if (s) void pickSupplier(s); }} />
              <SelectColumn title="Bog" items={bogs.map((b) => ({ id: b.id, label: b.name }))}
                selectedId={bog?.id ?? null} emptyNote={supplier ? "None" : "Select a supplier"}
                onPick={(id) => setBog(bogs.find((x) => x.id === id) ?? null)} />
            </div>
            <div className="tt-btn-row" style={{ marginTop: 22 }}>
              <button className="tt-btn tt-primary" disabled={!canNext} onClick={() => setStep("delivery-weigh")}>Next →</button>
            </div>
          </div>
        )}

        {step === "delivery-weigh" && (
          <div style={{ display: "grid", gridTemplateColumns: "1fr 460px", gap: 26 }}>
            <div className="tt-panel">
              <h2 className="tt-h2">Delivery</h2>
              <ValueField label="Haulier" value={haulier?.name} readOnly />
              <ValueField label="Product" value={product?.description} readOnly />
              {supplier && <ValueField label="Supplier" value={supplier.name} readOnly />}
              {bog && <ValueField label="Bog" value={bog.name} readOnly />}
              <ValueField label="Vehicle Reg" value={vehicleReg} placeholder="Required — tap to enter"
                onClick={editReg} required />
              {/* The one thing the driver has to be asked. Until it is answered the block
                  shouts; answering it settles it back into the panel. */}
              <div className={`tt-ask${docketChoice === null ? " tt-ask-unanswered" : ""}`}>
                <div className="tt-ask-q">Do you have a Docket?</div>
                <div className="tt-ask-btns">
                  <button
                    className={`tt-btn tt-ask-btn${docketChoice === true ? " tt-primary" : ""}`}
                    onClick={() => setDocketChoice(true)}
                  >
                    YES
                  </button>
                  <button
                    className={`tt-btn tt-ask-btn${docketChoice === false ? " tt-primary" : ""}`}
                    onClick={() => { setDocketChoice(false); setDocketNumber(""); }}
                  >
                    NO
                  </button>
                </div>
              </div>
              {docketChoice === true && (
                <ValueField label="Docket No" value={docketNumber} placeholder="Required — tap to enter"
                  onClick={editDocket} required />
              )}
            </div>
            <div className="tt-panel tt-weigh-side" style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 22 }}>
              {gauge}
              <button className="tt-btn tt-primary" style={{ width: "100%" }} disabled={!canWeighDelivery || busy} onClick={doDeliveryWeigh}>
                {busy ? "Weighing…" : "Weigh"}
              </button>
              {!healthy && <div className="tt-error">Waiting for live weight…</div>}
              {healthy && !canWeighDelivery && (
                <div className="tt-error">
                  {vehicleReg.trim().length === 0
                    ? "Enter the vehicle reg to weigh."
                    : docketChoice === null
                      ? "Answer the docket question to weigh."
                      : "Enter the docket number to weigh."}
                </div>
              )}
            </div>
          </div>
        )}

        {step === "collection-ref" && (
          <div className="tt-center">
            {/* The keypad is the page — the driver types the reference the office gave them
                straight away, with no button to open it first. Back leaves. */}
            <KeypadPanel
              className="tt-pad-lg"
              title="Enter Reference Number"
              okLabel={busy ? "Checking…" : "Find Load"}
              disabled={busy}
              onSubmit={(v) => void lookupRef(v)}
            />
            {refError && <div className="tt-error">{refError}</div>}
          </div>
        )}

        {step === "collection-weigh" && load && (
          <div style={{ display: "grid", gridTemplateColumns: "1fr 460px", gap: 26 }}>
            <div className="tt-panel">
              <h2 className="tt-h2">Collection {load.idRef}</h2>
              <ValueField label="Haulier" value={load.haulierName} readOnly />
              <ValueField label="Trailer No" value={trailerNo} placeholder="Required — tap to enter"
                onClick={editTrailer} required />
              <div className="tt-col-title" style={{ marginTop: 16 }}>Orders on this load</div>
              <div className="tt-list tt-orders-list">
                {load.orders.length === 0 && <div className="tt-note">No orders listed.</div>}
                {load.orders.map((o, i) => (
                  <div className="tt-list-item tt-drop-item" key={o.id} style={{ cursor: "default" }}>
                    {/* Delivery order (1 = dropped first); the API returns them in this order. */}
                    <div className="tt-drop-no">{o.sortOrder ?? i + 1}</div>
                    <div>
                      <div>{(o.orderNo ? `#${o.orderNo} ` : "") + (o.customerName ?? "Order " + o.id)}</div>
                      {o.addressFull && <div className="tt-list-sub">{oneLineAddress(o.addressFull)}</div>}
                    </div>
                  </div>
                ))}
              </div>
            </div>
            <div className="tt-panel tt-weigh-side" style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 22 }}>
              {gauge}
              <button className="tt-btn tt-primary" style={{ width: "100%" }} disabled={!canWeighCollection || busy} onClick={doCollectionWeigh}>
                {busy ? "Weighing…" : "Weigh"}
              </button>
              {!healthy && <div className="tt-error">Waiting for live weight…</div>}
              {healthy && trailerNo.trim().length === 0 && (
                <div className="tt-error">Enter the trailer number to weigh.</div>
              )}
            </div>
          </div>
        )}
      </div>

      {/* Build number + settings gear on the Weigh-In menu only. The steps below it need
          their bottom-right corner for Next, and the gear is one Back away. */}
      {step === "menu" && (
        <>
          <span className="tt-build-ver">{__BUILD_VERSION__}</span>
          <SuperAdminGear onOpen={onOpenSettings} />
          {/* Dev builds only: which API / database this run is pointed at, and a click to
              flip it. Prod builds show nothing. */}
          {import.meta.env.DEV && (
            <div className="tt-devbar">
              <DbSwitcher />
            </div>
          )}
        </>
      )}

      {overlay && (
        <div className="tt-overlay tt-success">
          <div className="tt-overlay-ico">✓</div>
          <div className="tt-overlay-title">{overlay}</div>
          <div className="tt-overlay-sub">
            {holdingOpen ? "Barrier open — drive off the weighbridge" : "Please exit the weighbridge when ready"}
          </div>
          {countdown !== null && <div className="tt-countdown">{countdown}</div>}
        </div>
      )}
    </div>
  );
}

// ---- small presentational helpers ----

function SelectColumn({
  title, items, selectedId, onPick, emptyNote, className,
}: {
  title: string;
  items: { id: number; label: string | null }[];
  selectedId: number | null;
  onPick: (id: number) => void;
  emptyNote?: string;
  className?: string;
}) {
  return (
    <div className={`tt-col${className ? " " + className : ""}`}>
      <div className="tt-col-title">{title}</div>
      <div className="tt-list">
        {items.length === 0 && <div className="tt-note">{emptyNote ?? "—"}</div>}
        {items.map((it) => (
          <div key={it.id} className={`tt-list-item${selectedId === it.id ? " tt-selected" : ""}`} onClick={() => onPick(it.id)}>
            {it.label}
          </div>
        ))}
      </div>
    </div>
  );
}

function ValueField({
  label, value, placeholder, onClick, readOnly, required,
}: {
  label: string;
  value?: string | null;
  placeholder?: string;
  onClick?: () => void;
  readOnly?: boolean;
  required?: boolean; // star the label and flag the box until it is filled in
}) {
  const has = !!value && value.length > 0;
  return (
    <div className="tt-field">
      <div className="tt-label">
        {label}
        {required && <span className="tt-req"> *</span>}
      </div>
      <div
        className={`tt-value${readOnly ? " tt-readonly" : ""}${has ? "" : " tt-placeholder"}${
          required && !has ? " tt-missing" : ""
        }`}
        onClick={readOnly ? undefined : onClick}
      >
        {has ? value : readOnly ? "—" : placeholder}
      </div>
    </div>
  );
}
