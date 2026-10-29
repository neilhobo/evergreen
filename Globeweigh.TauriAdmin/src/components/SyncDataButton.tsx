import { useEffect, useRef, useState } from "react";
import Modal from "./Modal";
import {
  PreviewResult,
  ProdConn,
  SyncResult,
  getSyncTables,
  previewSync,
  syncFromProd,
  testProdConnection,
} from "../api/devSync";

type Phase = "confirm" | "running" | "done" | "error";

interface Props {
  sidebarOpen: boolean;
}

const CONN_KEY = "devSyncProdConn";
const TABLES_KEY = "devSyncTables"; // saved table subset; absent = all tables

// Saved table selection ("schema.table" names), or null to mean "all tables".
function loadSelectedTables(): string[] | null {
  try {
    const raw = localStorage.getItem(TABLES_KEY);
    if (raw) return JSON.parse(raw);
  } catch {
    // ignore corrupt storage
  }
  return null;
}

// Sensible starting point: the prod server's LAN IP + instance, and the prod DB name.
// Credentials are left blank for the developer to fill in.
const DEFAULT_CONN: ProdConn = {
  server: "192.168.250.5\\NTPRO",
  database: "GbEvergreen",
  user: "",
  password: "",
};

function loadConn(): ProdConn {
  try {
    const raw = localStorage.getItem(CONN_KEY);
    if (raw) return { ...DEFAULT_CONN, ...JSON.parse(raw) };
  } catch {
    // ignore corrupt storage
  }
  return { ...DEFAULT_CONN };
}

// Dev-only control (rendered above the DB switcher) that clones the whole PROD database into
// the LOCAL database (wipe + replace). The prod connection is entered here and the actual
// work happens on the local API.
export default function SyncDataButton({ sidebarOpen }: Props) {
  const [open, setOpen] = useState(false);
  const [phase, setPhase] = useState<Phase>("confirm");
  const [result, setResult] = useState<SyncResult | null>(null);
  const [error, setError] = useState("");

  const [conn, setConn] = useState<ProdConn>(loadConn);
  const [testing, setTesting] = useState(false);
  const [test, setTest] = useState<{ ok: boolean; message: string } | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [preview, setPreview] = useState<PreviewResult | null>(null);
  const [previewSecs, setPreviewSecs] = useState(0);
  const [syncSecs, setSyncSecs] = useState(0);
  const [selectedTables, setSelectedTables] = useState<string[] | null>(loadSelectedTables);
  const [chooseOpen, setChooseOpen] = useState(false);
  // Aborts the in-flight preview/sync so Cancel can stop a run instead of leaving the dialog
  // spinning; dropping the connection also cancels the work on the API side.
  const abortRef = useRef<AbortController | null>(null);

  function isAbort(e: unknown) {
    return e instanceof DOMException ? e.name === "AbortError" : /abort/i.test(String(e));
  }

  function cancelRun() {
    abortRef.current?.abort();
    abortRef.current = null;
  }

  function setField(field: keyof ProdConn, value: string) {
    setConn((c) => {
      const next = { ...c, [field]: value };
      try {
        localStorage.setItem(CONN_KEY, JSON.stringify(next));
      } catch {
        // ignore storage failures
      }
      return next;
    });
    setTest(null); // any edit invalidates the last test result
    setPreview(null); // …and the last preview
  }

  function openModal() {
    setPhase("confirm");
    setResult(null);
    setError("");
    setTest(null);
    setPreview(null);
    setOpen(true);
  }

  function close() {
    cancelRun(); // closing mid-run abandons it rather than trapping the user in the dialog
    setOpen(false);
  }

  // Auto-run a connection test when the dialog opens (if a server + database are present).
  useEffect(() => {
    if (open && conn.server.trim() && conn.database.trim()) void testConnection();
    // Run once per open; deliberately not re-running on field edits.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // Tick an elapsed-seconds counter while a preview is generating.
  useEffect(() => {
    if (!previewing) return;
    setPreviewSecs(0);
    const id = setInterval(() => setPreviewSecs((s) => s + 1), 1000);
    return () => clearInterval(id);
  }, [previewing]);

  // Tick an elapsed-seconds counter while a sync is running.
  useEffect(() => {
    if (phase !== "running") return;
    setSyncSecs(0);
    const id = setInterval(() => setSyncSecs((s) => s + 1), 1000);
    return () => clearInterval(id);
  }, [phase]);

  const canConnect = conn.server.trim() !== "" && conn.database.trim() !== "";

  async function testConnection() {
    setTesting(true);
    setTest(null);
    try {
      const r = await testProdConnection(conn);
      setTest({ ok: r.success, message: r.message });
    } catch (e) {
      setTest({ ok: false, message: String(e) });
    } finally {
      setTesting(false);
    }
  }

  async function generatePreview() {
    setPreviewing(true);
    setPreview(null);
    const ac = new AbortController();
    abortRef.current = ac;
    try {
      setPreview(await previewSync(conn, selectedTables, ac.signal));
    } catch (e) {
      setPreview(
        isAbort(e)
          ? { success: false, durationMs: 0, tables: [], message: "Preview cancelled." }
          : { success: false, durationMs: 0, tables: [], message: String(e) },
      );
    } finally {
      if (abortRef.current === ac) abortRef.current = null;
      setPreviewing(false);
    }
  }

  // A successful preview already identified exactly which tables changed — syncing just
  // those skips re-diffing every table on the server, so it finishes in seconds. Rows the
  // preview could not diff ("… — preview error: …") aren't real table names and are dropped.
  const previewedTables =
    preview?.success && preview.tables.length > 0
      ? preview.tables.map((t) => t.table).filter((t) => !t.includes(" — preview error:"))
      : null;

  async function run() {
    setPhase("running");
    setError("");
    // Preview proved local already matches prod — nothing to send to the server.
    if (preview?.success && preview.tables.length === 0) {
      setResult({ success: true, durationMs: 0, tables: [], message: preview.message ?? "Already up to date." });
      setPhase("done");
      return;
    }
    const ac = new AbortController();
    abortRef.current = ac;
    try {
      // Quick path: only the previewed tables. No preview (or only error rows) → full sync.
      const r = await syncFromProd(
        conn,
        previewedTables && previewedTables.length > 0 ? previewedTables : selectedTables,
        ac.signal,
      );
      setResult(r);
      setPhase(r.success ? "done" : "error");
    } catch (e) {
      // Cancelled: drop straight back to the form so another run can be started.
      if (isAbort(e)) {
        setPhase("confirm");
        return;
      }
      setError(String(e));
      setPhase("error");
    } finally {
      if (abortRef.current === ac) abortRef.current = null;
    }
  }

  const failures = result?.tables.filter((t) => t.error) ?? [];

  return (
    <>
      <button
        className="sidebar-sync-data"
        onClick={openModal}
        title="Sync data from PROD into the local database"
      >
        <span className="sync-data-icon" aria-hidden="true">
          ⟳
        </span>
        {sidebarOpen && <span>Sync Data</span>}
      </button>

      {open && (
        <Modal title="Sync Data" width={680} height={440} onClose={close}>
          <div className="sync-data-modal">
            {phase === "confirm" && (
              <>
                <div className="sync-data-panel">
                  <div className="sync-split">
                    <div className="sync-split-left">
                      <div className="form-inline">
                        <div className="form-col">
                          <div className="form-field form-field-inline">
                            <label className="form-label" htmlFor="sync-server">
                              Server
                            </label>
                            <input
                              id="sync-server"
                              className="form-input"
                              value={conn.server}
                              placeholder="host\\instance"
                              onChange={(e) => setField("server", e.target.value)}
                            />
                          </div>
                          <div className="form-field form-field-inline">
                            <label className="form-label" htmlFor="sync-database">
                              Database
                            </label>
                            <input
                              id="sync-database"
                              className="form-input"
                              value={conn.database}
                              onChange={(e) => setField("database", e.target.value)}
                            />
                          </div>
                          <div className="form-field form-field-inline">
                            <label className="form-label" htmlFor="sync-user">
                              User
                            </label>
                            <input
                              id="sync-user"
                              className="form-input"
                              value={conn.user}
                              placeholder="(blank = Windows auth)"
                              autoComplete="off"
                              onChange={(e) => setField("user", e.target.value)}
                            />
                          </div>
                          <div className="form-field form-field-inline">
                            <label className="form-label" htmlFor="sync-password">
                              Password
                            </label>
                            <input
                              id="sync-password"
                              className="form-input"
                              type="password"
                              value={conn.password}
                              autoComplete="off"
                              onChange={(e) => setField("password", e.target.value)}
                            />
                          </div>
                        </div>
                      </div>

                      <div className="sync-data-test-row">
                        {testing ? (
                          <span className="sync-data-summary">Testing connection…</span>
                        ) : (
                          test && (
                            <span className={test.ok ? "sync-data-ok" : "sync-data-warn"}>
                              {test.message}
                            </span>
                          )
                        )}
                      </div>

                      <div className="sync-data-preview-row">
                        <button
                          className="btn-primary"
                          onClick={generatePreview}
                          disabled={!test?.ok || previewing}
                        >
                          {previewing ? "Generating…" : "Generate Preview"}
                        </button>
                        <button className="toolbar-btn" onClick={() => setChooseOpen(true)}>
                          Choose tables…
                        </button>
                        <span className="sync-data-summary">
                          {selectedTables
                            ? `${selectedTables.length} table(s) selected`
                            : "All tables"}
                        </span>
                        {!previewing && preview?.success && preview.tables.length > 0 && (
                          <span className="sync-data-summary">{preview.message}</span>
                        )}
                      </div>
                    </div>
                    <div className="sync-split-right">
                      {previewing ? (
                        <div className="sync-preview-loading">
                          <div className="sync-spinner" aria-hidden="true" />
                          <p className="sync-preview-loading-title">Generating preview…</p>
                          <div className="sync-progress-bar" aria-hidden="true">
                            <div className="sync-progress-fill" />
                          </div>
                          <p className="sync-preview-sub">
                            Comparing prod and local tables — {previewSecs}s
                          </p>
                          <button className="toolbar-btn" onClick={cancelRun}>
                            Cancel
                          </button>
                        </div>
                      ) : preview ? (
                        !preview.success ? (
                          <p className="sync-data-warn">{preview.message}</p>
                        ) : preview.tables.length === 0 ? (
                          <p className="sync-data-ok">{preview.message}</p>
                        ) : (
                          <table className="sync-preview-table">
                              <thead>
                                <tr>
                                  <th>Table</th>
                                  <th>New</th>
                                  <th>Upd</th>
                                  <th>Del</th>
                                </tr>
                              </thead>
                              <tbody>
                                {preview.tables.map((t) => (
                                  <tr key={t.table}>
                                    <td>{t.table}</td>
                                    <td>{t.inserts.toLocaleString()}</td>
                                    <td>{t.updates.toLocaleString()}</td>
                                    <td>{t.deletes.toLocaleString()}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                        )
                      ) : (
                        <p className="sync-preview-placeholder">
                          Click “Generate Preview” to list the changes that a sync would make.
                        </p>
                      )}
                    </div>
                  </div>
                </div>
                <div className="sync-data-actions">
                  <button className="toolbar-btn" onClick={close}>
                    Cancel
                  </button>
                  <button className="btn-primary" onClick={run} disabled={!canConnect}>
                    {previewedTables && previewedTables.length > 0
                      ? `Sync ${previewedTables.length} changed table(s)`
                      : "Sync PROD → Local"}
                  </button>
                </div>
              </>
            )}

            {phase === "running" && (
              <div className="sync-data-panel">
                <div className="sync-preview-loading">
                  <div className="sync-spinner" aria-hidden="true" />
                  <p className="sync-preview-loading-title">Syncing changes from PROD…</p>
                  <div className="sync-progress-bar" aria-hidden="true">
                    <div className="sync-progress-fill" />
                  </div>
                  <p className="sync-preview-sub">
                    Applying changes to local data — please wait — {syncSecs}s
                  </p>
                  <button className="toolbar-btn" onClick={cancelRun}>
                    Cancel
                  </button>
                </div>
              </div>
            )}

            {(phase === "done" || phase === "error") && (
              <>
                <div className="sync-data-panel">
                {error ? (
                  <p className="sync-data-warn">Sync failed: {error}</p>
                ) : (
                  <>
                    <p className={result?.success ? "sync-data-ok" : "sync-data-warn"}>
                      {result?.message}{" "}
                      {result && `(${(result.durationMs / 1000).toFixed(1)}s)`}
                    </p>
                    {failures.length > 0 && (
                      <div className="sync-data-failures">
                        <strong>Tables with errors:</strong>
                        <ul>
                          {failures.map((t) => (
                            <li key={t.table}>
                              {t.table}: {t.error}
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}
                    {result && (
                      <p className="sync-data-summary">
                        {result.tables.length} table(s),{" "}
                        {result.tables.reduce((n, t) => n + t.rowsCopied, 0).toLocaleString()} rows
                        copied.
                      </p>
                    )}
                  </>
                )}
                </div>
                <div className="sync-data-actions">
                  <button className="btn-primary" onClick={() => setOpen(false)}>
                    Close
                  </button>
                </div>
              </>
            )}
          </div>
        </Modal>
      )}

      {chooseOpen && (
        <ChooseTablesModal
          initial={selectedTables}
          onClose={() => setChooseOpen(false)}
          onSave={(sel) => {
            setSelectedTables(sel);
            try {
              if (sel) localStorage.setItem(TABLES_KEY, JSON.stringify(sel));
              else localStorage.removeItem(TABLES_KEY);
            } catch {
              // ignore storage failures
            }
            setPreview(null); // selection changed → invalidate any preview
            setChooseOpen(false);
          }}
        />
      )}
    </>
  );
}

// Picker for which tables a sync touches. Lists all local tables with checkboxes; "all selected"
// is saved as null so newly-added tables are included by default next time.
function ChooseTablesModal({
  initial,
  onSave,
  onClose,
}: {
  initial: string[] | null;
  onSave: (selection: string[] | null) => void;
  onClose: () => void;
}) {
  const [tables, setTables] = useState<string[] | null>(null);
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;
    getSyncTables()
      .then((t) => {
        if (!active) return;
        setTables(t);
        setChecked(new Set(initial ?? t)); // null → all checked
      })
      .catch((e) => active && setError(String(e)));
    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function toggle(t: string) {
    setChecked((prev) => {
      const next = new Set(prev);
      if (next.has(t)) next.delete(t);
      else next.add(t);
      return next;
    });
  }

  function save() {
    if (!tables) return;
    // Everything checked → save null ("all"), so future new tables are included too.
    onSave(checked.size === tables.length ? null : tables.filter((t) => checked.has(t)));
  }

  return (
    <Modal title="Choose tables to sync" width={420} height={540} onClose={onClose}>
      <div className="choose-tables">
        {error ? (
          <p className="sync-data-warn">{error}</p>
        ) : !tables ? (
          <p className="sync-data-summary">Loading tables…</p>
        ) : (
          <>
            <div className="choose-tables-actions">
              <button className="toolbar-btn" onClick={() => setChecked(new Set(tables))}>
                Select all
              </button>
              <button className="toolbar-btn" onClick={() => setChecked(new Set())}>
                Clear
              </button>
              <span className="sync-data-summary">
                {checked.size} of {tables.length}
              </span>
            </div>
            <ul className="choose-tables-list">
              {tables.map((t) => (
                <li key={t}>
                  <label>
                    <input type="checkbox" checked={checked.has(t)} onChange={() => toggle(t)} />
                    <span>{t}</span>
                  </label>
                </li>
              ))}
            </ul>
          </>
        )}
        <div className="sync-data-actions">
          <button className="toolbar-btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-primary" onClick={save} disabled={!tables}>
            Save
          </button>
        </div>
      </div>
    </Modal>
  );
}
