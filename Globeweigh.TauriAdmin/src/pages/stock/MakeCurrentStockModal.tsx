import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import { fmtDateTime } from "../../format";
import {
  MakeCurrentResult,
  StockLocationCounts,
  StockTake,
  getStockLocationCounts,
  makeCurrentStock,
} from "../../api/stock";

interface Props {
  take: StockTake; // an OPEN take with a location
  onClose: (applied: boolean) => void;
}

// "Make Current Stock" flow: a summary of exactly what applying the stock take will do,
// a confirm button, a progress spinner while the API applies it, then the outcome.
export default function MakeCurrentStockModal({ take, onClose }: Props) {
  const [phase, setPhase] = useState<"summary" | "applying" | "done">("summary");
  const [counts, setCounts] = useState<StockLocationCounts | null>(null);
  const [result, setResult] = useState<MakeCurrentResult | null>(null);

  useEffect(() => {
    getStockLocationCounts()
      .then(setCounts)
      .catch(() => setCounts(null));
  }, []);

  // Standing counts of the OTHER locations — they remain in effect alongside this take.
  const others = (counts?.locations ?? []).filter((l) => l.id !== take.locationId && l.palletCount > 0);
  const othersTotal = others.reduce((s, l) => s + l.palletCount, 0);
  const fmt = (n: number) => n.toLocaleString();

  async function apply() {
    setPhase("applying");
    try {
      setResult(await makeCurrentStock(take.id));
      setPhase("done");
    } catch (err) {
      void messageDialog("Failed to make current stock.\n" + (err as Error).message);
      setPhase("summary");
    }
  }

  const row = (label: string, value: string) => (
    <div style={{ display: "flex", justifyContent: "space-between", gap: 16, padding: "4px 0" }}>
      <span style={{ color: "var(--text-label)", fontSize: 12, fontWeight: 500 }}>{label}</span>
      <span style={{ fontSize: 12, fontWeight: 600 }}>{value}</span>
    </div>
  );

  return (
    <Modal
      title={`Make Current Stock — Stock Take #${take.id}`}
      width={520}
      minHeight={300}
      onClose={() => phase !== "applying" && onClose(phase === "done")}
      footer={
        phase === "summary" ? (
          <>
            <button className="toolbar-btn" onClick={() => onClose(false)}>
              Cancel
            </button>
            <button className="btn-lavender" onClick={() => void apply()}>
              Confirm — Make Current Stock
            </button>
          </>
        ) : phase === "done" ? (
          <button className="btn-blue" onClick={() => onClose(true)}>
            Close
          </button>
        ) : undefined
      }
    >
      {phase === "applying" ? (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            gap: 14,
            height: "100%",
            minHeight: 180,
          }}
        >
          <div className="sync-spinner" />
          <span style={{ fontSize: 13, color: "var(--text-secondary)" }}>
            Updating stock — marking pallets in and out…
          </span>
        </div>
      ) : phase === "done" && result ? (
        <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
          <p style={{ fontSize: 13, fontWeight: 600, margin: "0 0 6px" }}>Current stock updated.</p>
          {row("Now in stock", `${fmt(result.inStock)} pallets`)}
          {row("Marked out of stock", fmt(result.markedOut))}
          {row("Brought back into stock", fmt(result.broughtBack))}
          {result.created > 0 && row("Placeholder pallets created", fmt(result.created))}
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
          {row("Location", take.locationName ?? "—")}
          {row("Counted", `${fmt(take.productCount)} products, ${fmt(take.palletCount)} pallets`)}
          {row("Taken", fmtDateTime(take.dateCreated))}
          {others.length > 0 && (
            <>
              <div style={{ borderTop: "1px solid var(--border)", margin: "6px 0" }} />
              <span style={{ fontSize: 12, color: "var(--text-secondary)" }}>
                Standing counts at other locations (kept in stock):
              </span>
              {others.map((l) => row(l.name ?? "?", `${fmt(l.palletCount)} pallets`))}
            </>
          )}
          <div style={{ borderTop: "1px solid var(--border)", margin: "6px 0" }} />
          {row("Total staying in stock", `${fmt(take.palletCount + othersTotal)} pallets`)}
          <p style={{ fontSize: 12, color: "var(--text-secondary)", margin: "8px 0 0", lineHeight: 1.5 }}>
            For each counted product the <strong>newest</strong> pallets up to the counted quantity stay in
            stock — pallets previously marked out are brought back if the counts now cover them. Every
            other pallet on the system is marked <strong>out of stock</strong> (method: Stock Take). If a
            counted quantity exceeds the pallets recorded on the system, placeholder pallet records are
            created so the stock reconciles.
          </p>
        </div>
      )}
    </Modal>
  );
}
