import { useEffect, useState } from "react";
import { OilTanksAccessRow, getOilTanksAccess, setOilTanksAccess } from "../../api/oilTanks";
import shared from "./StagingPanel.module.css";
import styles from "./OilTanksAccessPanel.module.css";

// Super Admin > Oil Tanks: which users see the Oil Tanks page. One switch per user, saved as it
// is flipped (GlobeweighUser.OilTanksAccess). The API refuses the readings to anyone switched
// off, so this is the access rule, not just the menu. The super-admin always sees the page.
export default function OilTanksAccessPanel() {
  const [rows, setRows] = useState<OilTanksAccessRow[] | null>(null);
  // Which user's switch is mid-flight, and what went wrong with the last call.
  const [busy, setBusy] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getOilTanksAccess()
      .then(setRows)
      .catch((err) => setError((err as Error).message));
  }, []);

  async function toggle(id: number, next: boolean) {
    if (busy !== null) return;
    setBusy(id);
    setError(null);
    try {
      setRows(await setOilTanksAccess(id, next));
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(null);
    }
  }

  const name = (r: OilTanksAccessRow) =>
    `${r.firstName ?? ""} ${r.surname ?? ""}`.trim() || r.username || `User ${r.id}`;
  const onCount = rows?.filter((r) => r.canView).length ?? 0;

  return (
    <div className="po-main">
      <div className={shared.wrap}>
        <h2 className={shared.title}>Oil Tanks</h2>
        <p className={shared.note}>
          Who sees the Oil Tanks page. Switch on each user who should have it; everyone else does
          not get it in the menu, and the tank readings are refused to them. Takes effect the
          next time that user signs in or reopens the app. You always see it.
        </p>

        {error && <p className={shared.error}>{error}</p>}

        {rows === null && !error && <p className={shared.note}>Loading users…</p>}

        {rows !== null && (
          <>
            <p className={styles.count}>
              {onCount === 0 ? "Nobody else sees it yet." : `${onCount} of ${rows.length} users see it.`}
            </p>
            <div className={styles.list}>
              {rows.map((r) => (
                <div key={r.id} className={styles.row}>
                  <div className={styles.who}>
                    <span className={styles.name}>{name(r)}</span>
                    {r.username && name(r) !== r.username && (
                      <span className={styles.username}>{r.username}</span>
                    )}
                    {r.disabled && <span className={styles.disabled}>Disabled</span>}
                  </div>
                  <span className={r.canView ? shared.stateOn : shared.stateOff}>
                    {r.canView ? "Sees it" : "Hidden"}
                  </span>
                  <button
                    type="button"
                    role="switch"
                    aria-checked={r.canView}
                    aria-label={`Oil Tanks for ${name(r)} — ${r.canView ? "on" : "off"}`}
                    className={`${shared.switch} ${r.canView ? shared.switchOn : ""}`}
                    disabled={busy !== null}
                    onClick={() => void toggle(r.id, !r.canView)}
                  >
                    <span className={shared.knob} />
                  </button>
                </div>
              ))}
              {rows.length === 0 && <p className={shared.note}>No admin-app users.</p>}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
