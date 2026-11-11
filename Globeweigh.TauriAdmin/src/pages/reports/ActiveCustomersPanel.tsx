import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import ToolbarDate from "../../components/ToolbarDate";
import { messageDialog } from "../../components/confirmDialog";
import SageRefreshOverlay from "../../components/SageRefreshOverlay";
import { ActiveCustomer, ActiveCustomersReport, getActiveCustomersReport } from "../../api/reports";
import { fmtDate, fmtDateTime } from "../../format";
import { saveBlob } from "../sage-link/saveBlob";
import { useSageSalesRefresh } from "./useSageSalesRefresh";

// Reports > Other > Active Customers: every Sage customer with at least one sales invoice since a
// date (1 Jan 2025 unless changed), from the Sage sales mirror, since Evergreen's own orders do
// not go back that far. Services/ActiveCustomersReport.cs has the definitions.

export const DEFAULT_SINCE = "2025-01-01";

const pad = (n: number) => String(n).padStart(2, "0");
const ymd = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

// Sage's address lines with the postcode on the end, as one line.
const fullAddress = (c: ActiveCustomer) => [c.address, c.postcode].filter(Boolean).join(", ");

export default function ActiveCustomersPanel() {
  const [since, setSince] = useState(DEFAULT_SINCE);
  const [data, setData] = useState<ActiveCustomersReport | null>(null);
  const [loading, setLoading] = useState(false);
  const [search, setSearch] = useState("");
  const [exporting, setExporting] = useState(false);
  const { waiting, waitStart, refresh, cancel } = useSageSalesRefresh(() => load());

  async function load(f = since) {
    if (!f) return;
    setLoading(true);
    try {
      setData(await getActiveCustomersReport(f, ymd(new Date())));
    } catch (err) {
      void messageDialog("Failed to build the report.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [since]);

  const rows = useMemo(() => {
    const all = data?.customers ?? [];
    const q = search.trim().toLowerCase();
    if (!q) return all;
    return all.filter((c) =>
      [c.accountRef, c.name, c.contact, fullAddress(c)].some((v) => v?.toLowerCase().includes(q)),
    );
  }, [data, search]);

  const sync = data?.sync ?? null;
  const never = !!sync && !sync.lastSuccessUtc;

  async function exportXlsx() {
    if (!data || exporting) return;
    setExporting(true);
    try {
      const blob = await buildWorkbook(data, rows);
      await saveBlob(blob, `Active Customers since ${data.from.slice(0, 10)}.xlsx`, "xlsx");
    } catch (err) {
      void messageDialog("Could not build the spreadsheet.\n" + (err as Error).message);
    } finally {
      setExporting(false);
    }
  }

  // The client's column list (2026-11-10): A/C, Company Name, Contact Name, full postal address
  // with the postcode, first and last invoice, Orders, Invoices — nothing else.
  const columns: Column<ActiveCustomer>[] = [
    { key: "accountRef", header: "A/C", fit: true, footer: (r) => `${r.length}` },
    { key: "name", header: "Company Name", width: 260, footer: (r) => `customer${r.length === 1 ? "" : "s"}` },
    { key: "contact", header: "Contact Name", width: 170, render: (c) => c.contact ?? "" },
    { key: "address", header: "Postal Address", render: fullAddress, sortValue: fullAddress },
    { key: "firstInvoice", header: "First invoice", fit: true, render: (c) => fmtDate(c.firstInvoice), sortValue: (c) => c.firstInvoice },
    { key: "lastInvoice", header: "Last invoice", fit: true, render: (c) => fmtDate(c.lastInvoice), sortValue: (c) => c.lastInvoice },
    { key: "orders", header: "Orders", width: 70, align: "right", footer: (r) => r.reduce((s, c) => s + c.orders, 0).toLocaleString() },
    { key: "invoices", header: "Invoices", width: 76, align: "right", footer: (r) => r.reduce((s, c) => s + c.invoices, 0).toLocaleString() },
  ];

  return (
    <div className="po-open-layout">
      <div className="po-main">
        <div className="toolbar">
          <ToolbarDate label="Since" value={since} onChange={setSince} />
          {since !== DEFAULT_SINCE && (
            <button className="toolbar-btn" onClick={() => setSince(DEFAULT_SINCE)}>
              Back to {fmtDate(DEFAULT_SINCE)}
            </button>
          )}
          <input
            className="form-input toolbar-search"
            placeholder="Search customers…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <span className="toolbar-note" style={{ marginLeft: 10 }}>
            {loading ? "Loading…" : waiting ? "Refresh requested — the Sage sidecar is reading Sage (a few minutes)…" : syncNote(data)}
          </span>
          <div className="toolbar-right">
            <button className="toolbar-btn" onClick={() => void exportXlsx()} disabled={!data || exporting}>
              {exporting ? "Building…" : "Export to Excel"}
            </button>
            <button className="btn-primary" onClick={() => void refresh()} disabled={waiting}>
              {waiting ? "Refreshing…" : "Refresh from Sage"}
            </button>
          </div>
        </div>

        {never && !waiting && (
          <div className="rp-banner">
            <strong>No sales figures from Sage yet.</strong> Press <em>Refresh from Sage</em> to ask the Sage sidecar on the
            server to read them in.
            {sync?.lastError && <div className="rp-banner-error">Last attempt failed: {sync.lastError}</div>}
          </div>
        )}
        {!never && !waiting && sync?.lastError && (
          <div className="rp-banner rp-banner-warn">
            Last refresh failed ({fmtDateTime(sync.lastRunUtc)}): {sync.lastError}. Showing the previous figures.
          </div>
        )}
        {data && sync?.windowFrom && new Date(since) < new Date(sync.windowFrom) && (
          <div className="rp-banner rp-banner-warn">
            The Sage figures here start on {fmtDate(sync.windowFrom)}, so sales before that date are not counted.
          </div>
        )}

        {data && (
          <div className="rp-figures">
            <div className="rp-figure">
              <span className="rp-figure-k">Customers who bought</span>
              <span className="rp-figure-v">
                {rows.length.toLocaleString()}
                {data.sageCustomers > 0 && <small>of {data.sageCustomers.toLocaleString()} Sage accounts</small>}
              </span>
            </div>
            <div className="rp-figure">
              <span className="rp-figure-k">Period</span>
              <span className="rp-figure-v">
                {fmtDate(data.from)} – {fmtDate(data.to)}
              </span>
            </div>
          </div>
        )}

        <DataTable
          sortable
          resizable
          columns={columns}
          rows={rows}
          rowKey={(c) => c.accountRef}
          emptyText={loading ? "Loading…" : never ? "Nothing synced from Sage yet" : search ? "No customer matches the search" : "No sales in this period"}
        />
      </div>

      {waiting && (
        <SageRefreshOverlay what="sales figures" typically="usually around five minutes" startedAt={waitStart} onCancel={cancel} />
      )}
    </div>
  );
}

function syncNote(data: ActiveCustomersReport | null): string {
  const s = data?.sync;
  return s?.lastSuccessUtc ? `Sage synced ${fmtDateTime(s.lastSuccessUtc)}` : "";
}

// One sheet of the customers as shown (search applied), plus a line saying what it is.
async function buildWorkbook(report: ActiveCustomersReport, rows: ActiveCustomer[]): Promise<Blob> {
  const ExcelJS = (await import("exceljs")).default;
  const wb = new ExcelJS.Workbook();
  wb.creator = "Globeweigh (Sage mirror)";
  wb.created = new Date();
  const ws = wb.addWorksheet("Active Customers", { views: [{ state: "frozen", ySplit: 3 }] });
  const FONT = { name: "Arial", size: 10 };
  const BOLD = { name: "Arial", size: 10, bold: true };
  ws.getCell("A1").value = `Customers with at least one sales invoice from ${fmtDate(report.from)} to ${fmtDate(report.to)}`;
  ws.getCell("A1").font = { name: "Arial", size: 14, bold: true };
  ws.getCell("A2").value = report.sync.lastSuccessUtc ? `From Sage, as of ${fmtDateTime(report.sync.lastSuccessUtc)}.` : "From Sage.";
  ws.getCell("A2").font = { name: "Arial", size: 9, color: { argb: "FF64748B" } };

  // Same columns as the page.
  const head = ["A/C", "Company Name", "Contact Name", "Postal Address", "First invoice", "Last invoice", "Orders", "Invoices"];
  const widths = [10, 36, 24, 70, 13, 13, 9, 9];
  ws.getRow(3).values = head;
  ws.getRow(3).font = BOLD;
  widths.forEach((w, i) => (ws.getColumn(i + 1).width = w));
  const d = (s: string) => new Date(s.slice(0, 10) + "T00:00:00");

  rows.forEach((c, i) => {
    const r = ws.getRow(4 + i);
    r.values = [c.accountRef, c.name, c.contact ?? "", fullAddress(c), d(c.firstInvoice), d(c.lastInvoice), c.orders, c.invoices];
    r.font = FONT;
  });
  const last = 3 + rows.length;
  const total = ws.getRow(last + 1);
  total.getCell(2).value = `${rows.length} customers`;
  for (const col of [7, 8]) {
    const letter = ws.getColumn(col).letter;
    total.getCell(col).value = {
      formula: `SUM(${letter}4:${letter}${last})`,
      result: rows.reduce((s, c) => s + (col === 7 ? c.orders : c.invoices), 0),
    };
  }
  total.font = BOLD;
  for (const col of [5, 6]) ws.getColumn(col).numFmt = "dd/mm/yyyy";
  ws.autoFilter = { from: { row: 3, column: 1 }, to: { row: 3, column: head.length } };

  const buf = await wb.xlsx.writeBuffer();
  return new Blob([buf], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
}
