import { useState } from "react";
import { STYLE } from "../../api/features";
import { useFeatures } from "../../features";
import LiveWeightBody, { LiveWeightStyle } from "../weighbridge/LiveWeightPanel";
import shared from "./StagingPanel.module.css";
import styles from "./ScaleRelayStylesPanel.module.css";
import "../weighbridge/Weighbridge.css";

// Super Admin > Scale/Relay Styles: which look the Live Weight column on Deliveries/Incoming
// uses. One flag in the same server-held store as Staging (STYLE.scaleRelayDarkDock), so the
// choice is one answer for every PC rather than something set on each of them. Each option
// shows the real component with sample values, so what is chosen is what is seen.
const OPTIONS: { key: LiveWeightStyle; title: string; text: string }[] = [
  {
    key: "current",
    title: "Current",
    text:
      "The look carried over from the WPF admin: a dark readout with green digits, filled Open " +
      "and Close buttons, and a round lamp for each relay.",
  },
  {
    key: "dock",
    title: "Dark dock",
    text:
      "The sidebar's navy brought into the column: glowing green digits with a live lamp for each bridge, " +
      "and a state chip beside a plain Open or Close button.",
  },
];

// Sample values for the previews: a lorry on Bridge 1, Bridge 2 not reporting, gate open.
const SAMPLE = {
  weight: "24860",
  weightStale: false,
  weight2: "0",
  weight2Stale: true,
  relay: { connected: true, gateOpen: true, barrierOpen: false },
  relayChecked: true,
  busy: false,
};

export default function ScaleRelayStylesPanel() {
  const { isOn, set, loaded } = useFeatures();
  const chosen: LiveWeightStyle = isOn(STYLE.scaleRelayDarkDock) ? "dock" : "current";
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function choose(key: LiveWeightStyle) {
    if (busy || key === chosen) return;
    setBusy(true);
    setError(null);
    try {
      await set(STYLE.scaleRelayDarkDock, key === "dock");
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="po-main">
      <div className={shared.wrap}>
        <h2 className={shared.title}>Scale/Relay Styles</h2>
        <p className={shared.note}>
          How the bridge readouts and the gate and barrier controls are drawn, on both
          Deliveries/Incoming and Gate/Barrier. The choice is held on the server, so it applies on
          every PC as soon as the page is next opened.
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
                <div className={styles.preview} aria-hidden="true">
                  <div className={styles.previewHead}>Live Weight</div>
                  <LiveWeightBody
                    {...SAMPLE}
                    style={o.key}
                    onToggle={() => undefined}
                    onRemote={() => undefined}
                  />
                </div>
              </section>
            );
          })}
        </div>
      </div>
    </div>
  );
}
