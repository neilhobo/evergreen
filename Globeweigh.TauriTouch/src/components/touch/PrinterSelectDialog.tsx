import { useEffect, useState } from "react";
import { getSavedPrinter, listPrinters, PrinterInfo, savePrinter } from "../../api/printer";

// Popup: pick this PC's label printer from the printers installed on it. The selection
// is per-PC (localStorage), like the weighscale connection config.
export default function PrinterSelectDialog({ onClose }: { onClose: () => void }) {
  const [printers, setPrinters] = useState<PrinterInfo[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(getSavedPrinter());

  useEffect(() => {
    listPrinters()
      .then(setPrinters)
      // Tauri invoke can reject with a plain string (e.g. ACL denials) — not an Error.
      .catch((e) => setError(e instanceof Error ? e.message : String(e)));
  }, []);

  function onSave() {
    savePrinter(selected);
    onClose();
  }

  return (
    <div className="tt-pad-overlay" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="tt-pad">
        <div className="tt-pad-header">Select Printer</div>

        {error ? (
          <div className="tt-error" style={{ textAlign: "center", margin: "18px 0" }}>{error}</div>
        ) : printers === null ? (
          <div className="tt-note" style={{ textAlign: "center", margin: "18px 0" }}>
            Finding printers…
          </div>
        ) : printers.length === 0 ? (
          <div className="tt-note" style={{ textAlign: "center", margin: "18px 0" }}>
            No printers are installed on this PC.
          </div>
        ) : (
          <div
            style={{
              display: "flex",
              flexDirection: "column",
              gap: 10,
              margin: "12px 0 4px",
              maxHeight: "50vh",
              overflowY: "auto",
            }}
          >
            {printers.map((p) => (
              <button
                key={p.name}
                className={`tt-btn${selected === p.name ? " tt-primary" : ""}`}
                style={{ minHeight: 52, justifyContent: "flex-start", textAlign: "left" }}
                onClick={() => setSelected((cur) => (cur === p.name ? null : p.name))}
              >
                {p.name}
                {p.isDefault && (
                  <span style={{ marginLeft: "auto", fontSize: 13, opacity: 0.75 }}>
                    Windows default
                  </span>
                )}
              </button>
            ))}
          </div>
        )}

        <div style={{ display: "flex", gap: 12, marginTop: 16 }}>
          <button className="tt-btn tt-ghost" style={{ flex: 1, minHeight: 52 }} onClick={onClose}>
            Cancel
          </button>
          <button
            className="tt-btn tt-primary"
            style={{ flex: 1, minHeight: 52 }}
            disabled={printers === null && !error}
            onClick={onSave}
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
