import { useState } from "react";
import { STYLE } from "../../api/features";
import { useFeatures } from "../../features";
import DataTable, { Column } from "../../components/DataTable";
import shared from "./StagingPanel.module.css";
import styles from "./ScaleRelayStylesPanel.module.css";
import "../dispatch/Dispatch.css";

// Super Admin > Open Orders Styles: how Orders / Dispatch > Open Orders marks the orders whose
// load is already on the Pending list (weighed in, not yet out). One flag in the server-held
// store (STYLE.openOrdersPendingHatched), so it is one answer for every PC; no row = Grouped.
// Each option draws the real grid (DataTable + the Dispatch.css classes) with sample orders.
type Mark = "grouped" | "hatched";
const OPTIONS: { key: Mark; title: string; text: string }[] = [
  {
    key: "grouped",
    title: "Grouped",
    text:
      "The orders on a Pending load move into their own band at the top of Open Orders, under an " +
      "“On Pending loads” header; everything else follows under “Other open orders”. " +
      "Sorting a column sorts inside each band.",
  },
  {
    key: "hatched",
    title: "Hatched",
    text:
      "The orders stay where they are, with a faint teal hatch and a teal edge. An “On Pending” " +
      "button beside the search box filters the grid down to just those orders.",
  },
];

interface Sample {
  id: number;
  orderNo: number;
  customer: string;
  delivery: string;
  onPending: boolean;
}
const SAMPLE: Sample[] = [
  { id: 1, orderNo: 10449, customer: "Ballymena Farm Supplies", delivery: "06/11/26", onPending: false },
  { id: 2, orderNo: 10452, customer: "Kilrea Agri Stores", delivery: "ASAP", onPending: true },
  { id: 3, orderNo: 10455, customer: "Coleraine Country Feeds", delivery: "07/11/26", onPending: false },
  { id: 4, orderNo: 10457, customer: "Moneymore Livestock Ltd", delivery: "06/11/26", onPending: true },
];
const COLUMNS: Column<Sample>[] = [
  { key: "orderNo", header: "Order", fit: true },
  { key: "customer", header: "Customer" },
  {
    key: "delivery",
    header: "Delivery",
    fit: true,
    render: (r) => (r.delivery === "ASAP" ? <span className="dsp-asap">ASAP</span> : r.delivery),
  },
];

function Preview({ mark }: { mark: Mark }) {
  const on = SAMPLE.filter((r) => r.onPending);
  const rows = mark === "grouped" ? [...on, ...SAMPLE.filter((r) => !r.onPending)] : SAMPLE;
  return (
    <div className={styles.preview} aria-hidden="true">
      <div className={styles.previewHead}>Open Orders</div>
      <DataTable
        columns={COLUMNS}
        rows={rows}
        rowKey={(r) => r.id}
        resizable={false}
        groupKey={mark === "grouped" ? (r) => (r.onPending ? "pending" : "open") : undefined}
        groupLabel={(r) =>
          r.onPending ? (
            <span className="dsp-band dsp-band-pending">On Pending loads · {on.length}</span>
          ) : (
            <span className="dsp-band dsp-band-rest">Other open orders · {SAMPLE.length - on.length}</span>
          )
        }
        rowClass={(r) => (mark === "hatched" && r.onPending ? "row-on-pending" : undefined)}
      />
    </div>
  );
}

export default function OpenOrdersStylesPanel() {
  const { isOn, set, loaded } = useFeatures();
  const chosen: Mark = isOn(STYLE.openOrdersPendingHatched) ? "hatched" : "grouped";
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function choose(key: Mark) {
    if (busy || key === chosen) return;
    setBusy(true);
    setError(null);
    try {
      await set(STYLE.openOrdersPendingHatched, key === "hatched");
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="po-main">
      <div className={shared.wrap}>
        <h2 className={shared.title}>Open Orders Styles</h2>
        <p className={shared.note}>
          How Orders / Dispatch &gt; Open Orders shows the orders whose load is already on the
          Pending list. The choice is held on the server, so it applies on every PC as soon as the
          page is next opened.
        </p>

        {error && <p className={shared.error}>{error}</p>}

        <div className={styles.options}>
          {OPTIONS.map((o) => {
            const on = o.key === chosen;
            return (
              <section key={o.key} className={on ? `${styles.option} ${styles.optionOn}` : styles.option}>
                <div className={styles.head}>
                  <h3 className={shared.itemTitle}>{o.title}</h3>
                  {on ? (
                    <span className={styles.inUse}>In use</span>
                  ) : (
                    <button
                      type="button"
                      className={styles.useBtn}
                      disabled={!loaded || busy}
                      onClick={() => void choose(o.key)}
                    >
                      Use this style
                    </button>
                  )}
                </div>
                <p className={shared.itemText}>{o.text}</p>
                <Preview mark={o.key} />
              </section>
            );
          })}
        </div>
      </div>
    </div>
  );
}
