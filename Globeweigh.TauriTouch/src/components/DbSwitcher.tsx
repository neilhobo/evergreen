import { useState } from "react";
import Modal from "./Modal";
import { DbTarget, getDbTarget, setDbTarget, DB_TARGET_URLS } from "../config";

// Dev-only control (rendered in the bottom-right dev bar beside the settings gear) for
// flipping the running dev build between the local API/DB and the live PROD API/DB.
// Picking a target writes the apiBaseUrl override and reloads so every request
// re-resolves against the chosen host.
export default function DbSwitcher() {
  const [open, setOpen] = useState(false);
  const current = getDbTarget();

  function choose(target: DbTarget) {
    if (target === current) {
      setOpen(false);
      return;
    }
    setDbTarget(target);
    window.location.reload();
  }

  return (
    <>
      <button
        className={`sidebar-db-switch ${current}`}
        onClick={() => setOpen(true)}
        title={`Database: ${current === "local" ? "Local" : "PROD"} — click to switch`}
      >
        <span className={`db-switch-dot ${current}`} />
        <span>DB: {current === "local" ? "Local" : "PROD"}</span>
      </button>

      {open && (
        <Modal title="Switch Database" width={360} onClose={() => setOpen(false)}>
          <div className="db-switch-options">
            <p className="db-switch-hint">
              Choose which API / database this dev build talks to. The app will reload.
            </p>
            {(["local", "prod"] as DbTarget[]).map((target) => (
              <button
                key={target}
                className={`db-switch-option${current === target ? " active" : ""}`}
                onClick={() => choose(target)}
              >
                <span className={`db-switch-dot ${target}`} />
                <span className="db-switch-text">
                  <strong>{target === "local" ? "Local" : "PROD"}</strong>
                  <small>{DB_TARGET_URLS[target]}</small>
                </span>
                {current === target && <span className="db-switch-current">current</span>}
              </button>
            ))}
          </div>
        </Modal>
      )}
    </>
  );
}
