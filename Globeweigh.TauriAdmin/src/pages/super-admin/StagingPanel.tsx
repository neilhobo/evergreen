import { useState } from "react";
import { STAGED_FEATURES } from "../../api/features";
import { useFeatures } from "../../features";
import styles from "./StagingPanel.module.css";

// Super Admin > Staging: work that is finished and deployed but not switched on.
//
// The point of it is that a new feature can go out with everything else and still be dark on the
// client's screens until they ask for it — no branch kept alive, no second deploy. Switching one
// is immediate and applies to every PC, because the flag is on the server, not in the browser.
export default function StagingPanel() {
  const { isOn, set, loaded } = useFeatures();
  // Which switch is mid-flight, and what went wrong with the last one.
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function toggle(name: string, next: boolean) {
    if (busy) return;
    setBusy(name);
    setError(null);
    try {
      await set(name, next);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(null);
    }
  }

  // The On/Off word and the switch, for a feature or one of its parts.
  function switchFor(name: string, label: string) {
    const on = isOn(name);
    return (
      <>
        <span className={on ? styles.stateOn : styles.stateOff}>{on ? "On" : "Off"}</span>
        <button
          type="button"
          role="switch"
          aria-checked={on}
          aria-label={`${label} — ${on ? "on" : "off"}`}
          className={`${styles.switch} ${on ? styles.switchOn : ""}`}
          disabled={!loaded || busy !== null}
          onClick={() => void toggle(name, !on)}
        >
          <span className={styles.knob} />
        </button>
      </>
    );
  }

  return (
    <div className="po-main">
      <div className={styles.wrap}>
        <h2 className={styles.title}>Staging</h2>
        <p className={styles.note}>
          Features that are built and deployed but not live. Switching one on takes effect
          everywhere straight away — each screen picks it up when it is next opened, or on the
          next sign-in. Anything not listed here is already live.
        </p>

        {error && <p className={styles.error}>{error}</p>}

        <div className={styles.list}>
          {STAGED_FEATURES.filter((f) => !f.parent).map((f) => {
            const on = isOn(f.name);
            // Its parts (one switch per tab), listed inside its card.
            const parts = STAGED_FEATURES.filter((p) => p.parent === f.name);
            return (
              <section key={f.name} className={styles.item}>
                <div className={styles.itemHead}>
                  <h3 className={styles.itemTitle}>{f.title}</h3>
                  {switchFor(f.name, f.title)}
                </div>
                <p className={styles.itemText}>{f.description}</p>
                {/* What the client keeps seeing while it is off, so switching it back is a
                    known quantity rather than a guess. */}
                <p className={styles.itemOff}>
                  <span className={styles.itemOffLabel}>While off:</span> {f.whenOff}
                </p>
                {parts.length > 0 && (
                  <div className={styles.parts}>
                    <p className={styles.partsNote}>
                      Tabs — each shows only while {f.title} is on as well
                      {on ? "" : " (it is off, so none show yet)"}. The super-admin always sees
                      every tab, the switched-off ones in lavender.
                    </p>
                    {parts.map((p) => (
                      <div key={p.name} className={styles.part}>
                        <div className={styles.partText}>
                          <span className={styles.partTitle}>{p.title}</span>
                          <span className={styles.partDesc}>{p.description}</span>
                        </div>
                        {switchFor(p.name, `${f.title} ${p.title}`)}
                      </div>
                    ))}
                  </div>
                )}
              </section>
            );
          })}
        </div>

        {STAGED_FEATURES.length === 0 && (
          <p className={styles.note}>Nothing is staged — everything built is live.</p>
        )}
      </div>
    </div>
  );
}
