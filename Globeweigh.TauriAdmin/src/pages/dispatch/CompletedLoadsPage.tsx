import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import ToolbarDate from "../../components/ToolbarDate";
import EmailedMark from "../../components/EmailedMark";
import { messageDialog } from "../../components/confirmDialog";
import { WbCompleted, getDispatchCompleted } from "../../api/dispatch";
import { fmtDateTime, fmtQty, todayInput } from "../../format";

interface Props {
  // Opens the load detail panel. DispatchPage owns that,
  // because it flies in over the whole card; this tab only asks for it.
  onViewLoad: (id: number) => void;
}

// Completed Loads as a tab of its own: the same grid as the panel on the Open Orders tab, but
// over a date range rather than the latest 500, with a search — the shape Completed Orders has.
// The panel on the Open Orders tab stays as it is; this is where to come looking for a load.
export default function CompletedLoadsPage({ onViewLoad }: Props) {
  const [rows, setRows] = useState<WbCompleted[]>([]);
  const [search, setSearch] = useState("");
  const [from, setFrom] = useState(todayInput(-30));
  const [to, setTo] = useState(todayInput());
  const [loading, setLoading] = useState(false);

  async function refresh() {
    setLoading(true);
    try {
      setRows(await getDispatchCompleted(from, to));
    } catch (err) {
      void messageDialog("Failed to load completed loads.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return rows;
    return rows.filter((r) =>
      [String(r.id), r.haulierName, r.trailerNo].some((v) => v?.toLowerCase().includes(term)),
    );
  }, [rows, term]);

  // The same columns as the Completed Loads panel on the Open Orders tab.
  const columns: Column<WbCompleted>[] = [
    { key: "id", header: "Load Ref", fit: true, render: (r) => r.id },
    { key: "haulierName", header: "Haulier", render: (r) => <Highlight text={r.haulierName} term={term} /> },
    { key: "trailerNo", header: "Trailer No", fit: true, render: (r) => <Highlight text={r.trailerNo} term={term} /> },
    { key: "weightIn", header: "Weight In", fit: true, align: "right", render: (r) => fmtQty(r.weightIn) },
    { key: "dateWeightOut", header: "Outgoing Weigh Date", fit: true, render: (r) => fmtDateTime(r.dateWeightOut) },
    { key: "weightOut", header: "Weight Out", width: 90, align: "right", render: (r) => fmtQty(r.weightOut) },
    { key: "netWeight", header: "Net Weight", width: 90, align: "right", render: (r) => fmtQty(r.netWeight) },
    { key: "pickedQuantity", header: "Pallets", width: 70, align: "right", render: (r) => fmtQty(r.pickedQuantity) },
    {
      // The dispatch confirmation (sent from the Completed Loads panel on the Open Orders tab).
      key: "dispatchEmailSentAt",
      header: "Emailed",
      fit: true,
      align: "center",
      render: (r) => (
        <EmailedMark sentAt={r.dispatchEmailSentAt} sentTo={r.dispatchEmailSentTo} label="Dispatch confirmation emailed" />
      ),
    },
    {
      key: "view",
      header: "",
      width: 76,
      render: (r) => (
        <button
          className="link-btn"
          onClick={(e) => {
            e.stopPropagation();
            onViewLoad(r.id);
          }}
        >
          View Load
        </button>
      ),
    },
  ];

  return (
    <div className="po-main">
      <div className="toolbar">
        <input
          className="form-input toolbar-search"
          placeholder="Search loads…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <ToolbarDate label="From" value={from} onChange={setFrom} />
        <ToolbarDate label="To" value={to} onChange={setTo} />
        {loading && <span className="toolbar-note">Loading…</span>}
        <div className="toolbar-right">
          <button className="toolbar-btn" onClick={() => void refresh()} disabled={loading}>
            Refresh
          </button>
        </div>
      </div>
      <DataTable
        columns={columns}
        rows={filtered}
        rowKey={(r) => r.id}
        onDoubleClick={(r) => onViewLoad(r.id)}
        emptyText={loading ? "Loading…" : "No completed loads in this range"}
      />
    </div>
  );
}
