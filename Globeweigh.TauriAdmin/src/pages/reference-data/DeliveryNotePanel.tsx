import { useState } from "react";
import { STYLE } from "../../api/features";
import { DeliveryNote, DeliveryNoteTemplate } from "../../api/dispatch";
import { useFeatures } from "../../features";
import Modal from "../../components/Modal";
import DeliveryNoteSheet from "../completed-orders/DeliveryNoteSheet";
import styles from "./DeliveryNotePanel.module.css";

// Admin > Delivery Note: which design every delivery note prints in — the
// Completed Orders preview, the auto-print when a load completes, and the PDF attached to the
// dispatch confirmation. Held as two server-side flags (STYLE.deliveryNoteLedger /
// deliveryNoteGreenBand, neither = the original), which the API reads and stamps on each note,
// so the choice is one answer for every PC and for the server's own printing.
//
// Each option is drawn by the real sheet with a sample note, so what is chosen is what prints.
const OPTIONS: { key: DeliveryNoteTemplate; title: string; text: string }[] = [
  {
    key: "classic",
    title: "Original",
    text: "The note as it has always printed: clover logo centred, boxed To and Delivery Instructions, green-banded item table.",
  },
  {
    key: "ledger",
    title: "Ledger",
    text: "Hairline rules, small-caps labels and monospaced figures, in step with the new admin screens. Green only in the logos.",
  },
  {
    key: "greenband",
    title: "Green Band",
    text: "A solid green brand header with the note number in a lime pill and the load details in one strip. Best on a colour printer.",
  },
];

// A plausible load, clearly a sample: five lines so every design fits it on one page.
const SAMPLE: DeliveryNote = {
  orderId: 0,
  noteNo: 4812,
  date: "2026-09-25T00:00:00",
  poNumber: "PO-88214",
  accountRef: "RIV001",
  customerName: "Sample Garden Centre",
  addressLines: ["14 Mill Lane", "Banbridge", "Co. Down", "BT32 4AB"],
  addressFull: null,
  telephone: "07700 900412",
  comments: "Deliver to rear yard. Tail-lift required. Please call 30 minutes before arrival.",
  haulierName: "Sample Transport",
  vehicleReg: "KX21 BZR",
  trailerNo: "T-117",
  items: [
    { description: "Multi Purpose Compost", size: 60, upp: 70, pallets: 10, totalBags: 700 },
    { description: "Ericaceous Compost", size: 50, upp: 80, pallets: 4, totalBags: 320 },
    { description: "Bark Mulch", size: 70, upp: 60, pallets: 3, totalBags: 180 },
    { description: "John Innes No. 2", size: 20, upp: 150, pallets: 2, totalBags: 300 },
    { description: "Seed & Cutting Compost", size: 40, upp: 90, pallets: 1, totalBags: 90 },
  ],
};

export function chosenNoteTemplate(isOn: (name: string) => boolean): DeliveryNoteTemplate {
  if (isOn(STYLE.deliveryNoteLedger)) return "ledger";
  if (isOn(STYLE.deliveryNoteGreenBand)) return "greenband";
  return "classic";
}

export default function DeliveryNotePanel() {
  const { isOn, set, loaded } = useFeatures();
  const chosen = chosenNoteTemplate(isOn);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [viewing, setViewing] = useState<DeliveryNoteTemplate | null>(null);

  async function choose(key: DeliveryNoteTemplate) {
    if (busy || key === chosen) return;
    setBusy(true);
    setError(null);
    try {
      // Switch the other one OFF first, so there is never a moment with both on.
      if (key !== "ledger" && isOn(STYLE.deliveryNoteLedger)) await set(STYLE.deliveryNoteLedger, false);
      if (key !== "greenband" && isOn(STYLE.deliveryNoteGreenBand))
        await set(STYLE.deliveryNoteGreenBand, false);
      if (key === "ledger") await set(STYLE.deliveryNoteLedger, true);
      if (key === "greenband") await set(STYLE.deliveryNoteGreenBand, true);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const viewingOpt = OPTIONS.find((o) => o.key === viewing);

  return (
    // Not on the legacy .po-main class: App.css is bundled after this module and would win.
    <section className={styles.wrap}>
      <h2 className={styles.title}>Delivery Note</h2>
      <p className={styles.note}>
        The design every delivery note prints in: the Completed Orders preview, the automatic print
        when a load is completed, and the PDF attached to the dispatch confirmation. The commercial
        invoice follows the same design. Applies straight
        away, on every PC. The sheets below are a sample load; click one to see it full size.
      </p>
      {error && <p className={styles.error}>{error}</p>}

      <div className={styles.options}>
        {OPTIONS.map((o) => {
          const on = o.key === chosen;
          return (
            <div key={o.key} className={on ? `${styles.option} ${styles.optionOn}` : styles.option}>
              <button
                type="button"
                className={styles.thumb}
                onClick={() => setViewing(o.key)}
                aria-label={`View the ${o.title} design full size`}
              >
                <div className={styles.thumbInner} aria-hidden="true">
                  <DeliveryNoteSheet note={SAMPLE} template={o.key} />
                </div>
              </button>
              <div className={styles.head}>
                <span className={styles.name}>{o.title}</span>
                {on ? (
                  <span className={styles.inUse}>In use</span>
                ) : (
                  <button
                    type="button"
                    className={styles.useBtn}
                    disabled={!loaded || busy}
                    onClick={() => void choose(o.key)}
                  >
                    Use this
                  </button>
                )}
              </div>
              <p className={styles.text}>{o.text}</p>
            </div>
          );
        })}
      </div>

      {viewingOpt && (
        <Modal
          title={`Delivery note: ${viewingOpt.title} (sample)`}
          width={866}
          height={1241}
          draggable
          onClose={() => setViewing(null)}
          footer={
            <>
              {viewingOpt.key === chosen ? (
                <span className={styles.inUse}>In use</span>
              ) : (
                <button
                  className="btn-blue"
                  disabled={!loaded || busy}
                  onClick={() => void choose(viewingOpt.key)}
                >
                  Use this design
                </button>
              )}
              <button className="toolbar-btn" onClick={() => setViewing(null)}>
                Close
              </button>
            </>
          }
        >
          <div className="dn-preview">
            <DeliveryNoteSheet note={SAMPLE} template={viewingOpt.key} />
          </div>
        </Modal>
      )}
    </section>
  );
}
