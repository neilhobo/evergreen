import { useEffect, useMemo, useRef, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import ToolbarDate from "../../components/ToolbarDate";
import { messageDialog } from "../../components/confirmDialog";
import SageRefreshOverlay from "../../components/SageRefreshOverlay";
import {
  getMemberSalesReport,
  requestSageSalesRefresh,
  cancelSageSalesRefresh,
  MemberSalesMember,
  MemberSalesMonth,
  MemberSalesProduct,
  MemberSalesReport,
} from "../../api/reports";
import { getSageSync } from "../../api/sage";
import { fmtDate, fmtDateTime } from "../../format";
import cc from "../../styles/ContentCard.module.css";
import { buildMemberSalesWorkbook } from "./exportMemberSales";
import { saveBlob } from "../sage-link/saveBlob";

const TABS = [
  { key: "summary", label: "Summary" },
  { key: "monthly", label: "Monthly" },
  { key: "topValue", label: "Top 20 by Value" },
  { key: "topQty", label: "Top 20 by Quantity" },
  { key: "products", label: "All Products" },
] as const;
type Tab = (typeof TABS)[number]["key"];

interface Props {
  reportKey: string;
  title: string;
  // Which tab to open on; Summary unless told otherwise.
  initialTab?: Tab;
  // Changes when the report's members were edited: rebuild it.
  reloadToken?: number;
  // Open the report editor (the page owns it, since a rename changes its list too).
  onEdit: () => void;
}

const pad = (n: number) => String(n).padStart(2, "0");
const ymd = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

// £1,234.56, negatives in brackets and red; blankZero leaves a nil cell empty so a grid of
// member columns reads as a matrix rather than a wall of £0.00.
function gbp(n: number, blankZero = false) {
  if (blankZero && !n) return "";
  const s = Math.abs(n).toLocaleString("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  return n < 0 ? <span className="rp-neg">(£{s})</span> : `£${s}`;
}
function qty(n: number, blankZero = false) {
  if (blankZero && !n) return "";
  const s = Math.abs(n).toLocaleString("en-GB", { maximumFractionDigits: 2 });
  return n < 0 ? <span className="rp-neg">({s})</span> : s;
}
const money = (n: number) => `£${n.toLocaleString("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

// A member-sales report: what each named group of Sage accounts bought from Evergreen in a
// date range — turnover by member, by month, and the product lines by value and by quantity
// split per member — from the Sage sales mirror (Services/MemberSalesReport.cs has the
// definitions: turnover and months from the audit trail, products from the invoice lines).
export default function MemberSalesReportPanel({ reportKey, title, initialTab = "summary", reloadToken = 0, onEdit }: Props) {
  const today = new Date();
  const [from, setFrom] = useState(ymd(new Date(today.getFullYear(), 0, 1)));
  const [to, setTo] = useState(ymd(today));
  const [data, setData] = useState<MemberSalesReport | null>(null);
  const [loading, setLoading] = useState(false);
  const [tab, setTab] = useState<Tab>(initialTab);
  const [selected, setSelected] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);
  // A refresh has been asked for and the sidecar has not answered yet.
  const [waiting, setWaiting] = useState(false);
  // When the refresh was asked for (the cover's clock).
  const [waitStart, setWaitStart] = useState(0);
  const pollRef = useRef<number | null>(null);

  async function load(f = from, t = to) {
    if (!f || !t) return;
    setLoading(true);
    try {
      const r = await getMemberSalesReport(reportKey, f, t);
      setData(r);
      setSelected((cur) => (cur && r.members.some((m) => m.name === cur) ? cur : (r.members[0]?.name ?? null)));
    } catch (err) {
      void messageDialog("Failed to build the report.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  function stopPolling() {
    if (pollRef.current != null) {
      window.clearInterval(pollRef.current);
      pollRef.current = null;
    }
  }

  useEffect(() => {
    void load();
    return stopPolling;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reportKey, from, to, reloadToken]);

  function preset(which: "thisYear" | "lastYear" | "last12") {
    const y = today.getFullYear();
    if (which === "thisYear") {
      setFrom(ymd(new Date(y, 0, 1)));
      setTo(ymd(today));
    } else if (which === "lastYear") {
      setFrom(ymd(new Date(y - 1, 0, 1)));
      setTo(ymd(new Date(y - 1, 11, 31)));
    } else {
      const start = new Date(today);
      start.setFullYear(y - 1);
      start.setDate(start.getDate() + 1);
      setFrom(ymd(start));
      setTo(ymd(today));
    }
  }

  // Ask the sidecar for a fresh read of the sales mirror, then watch the sync row until it
  // moves. A full read is a few minutes (the audit trail and the invoice file, newest first).
  async function refreshFromSage() {
    if (waiting) return;
    let before: string | null;
    try {
      const s = await requestSageSalesRefresh();
      before = s.lastRunUtc;
    } catch (err) {
      void messageDialog("Could not request a refresh.\n" + (err as Error).message);
      return;
    }
    const startedAt = Date.now();
    setWaitStart(startedAt);
    setWaiting(true);
    stopPolling();
    pollRef.current = window.setInterval(async () => {
      try {
        const s = (await getSageSync()).find((x) => x.entity === "Sales");
        if (!s) return;
        if (s.lastRunUtc !== before) {
          stopPolling();
          // The cover stays up through the reload, so the new figures are there when it lifts.
          await load();
          setWaiting(false);
          if (s.lastError) void messageDialog("The Sage sidecar could not refresh the sales figures.\n" + s.lastError);
        } else if (Date.now() - startedAt > 20 * 60_000) {
          stopPolling();
          setWaiting(false);
          void messageDialog(
            "No answer from the Sage sidecar after 20 minutes.\nIs the GlobeweighSageSidecar service running on the server?",
          );
        }
      } catch {
        // keep polling
      }
    }, 5000);
  }

  // Cancel from the cover: the API drops a request the sidecar has not picked up and stops a
  // read in progress within seconds. The figures on the page stay as they were.
  async function cancelRefresh() {
    try {
      await cancelSageSalesRefresh();
      stopPolling();
      setWaiting(false);
    } catch (err) {
      void messageDialog("Could not cancel the refresh.\n" + (err as Error).message);
    }
  }

  async function exportXlsx() {
    if (!data || exporting) return;
    setExporting(true);
    try {
      const blob = await buildMemberSalesWorkbook(data, title);
      await saveBlob(blob, `${title} - ${from} to ${to}.xlsx`, "xlsx");
    } catch (err) {
      void messageDialog("Could not build the spreadsheet.\n" + (err as Error).message);
    } finally {
      setExporting(false);
    }
  }

  const members = data?.members ?? [];
  const months = data?.months ?? [];
  const products = data?.products ?? [];
  const topByQty = useMemo(
    () => [...products].sort((a, b) => b.qty - a.qty || a.stockCode.localeCompare(b.stockCode)).slice(0, 20),
    [products],
  );
  const sync = data?.sync ?? null;
  const never = !!sync && !sync.lastSuccessUtc;
  const selectedMember = members.find((m) => m.name === selected) ?? null;
  const unsplitTotal = (data?.unsplit ?? []).reduce((s, u) => s + u.net, 0);

  // ---- columns ----
  const summaryColumns: Column<MemberSalesMember>[] = [
    { key: "name", header: "Member", footer: () => "All members" },
    {
      key: "accounts",
      header: "Sage account(s)",
      width: 150,
      render: (m) =>
        m.accounts.length === 1 ? (
          m.accounts[0].accountRef
        ) : (
          <span title={m.accounts.map((a) => `${a.accountRef} ${a.name}`).join("\n")}>{m.accounts.length} accounts</span>
        ),
      sortValue: (m) => m.accounts.length,
    },
    { key: "invoices", header: "Invoices", width: 80, align: "right", render: (m) => qty(m.invoices, true), footer: () => qty(data?.totals.invoices ?? 0) },
    { key: "credits", header: "Credit notes", width: 90, align: "right", render: (m) => qty(m.credits, true), footer: () => qty(data?.totals.credits ?? 0) },
    { key: "turnover", header: "Purchase turnover", width: 150, align: "right", render: (m) => gbp(m.turnover), footer: () => gbp(data?.totals.turnover ?? 0) },
    {
      key: "share",
      header: "Share",
      width: 70,
      align: "right",
      render: (m) => (m.share ? `${(m.share * 100).toFixed(1)}%` : ""),
      footer: () => (data?.totals.turnover ? "100%" : ""),
    },
  ];

  const monthlyColumns: Column<MemberSalesMember>[] = [
    { key: "name", header: "Member", footer: () => "Total" },
    ...months.map<Column<MemberSalesMember>>((mo) => ({
      key: mo.month,
      header: mo.label,
      width: 100,
      align: "right",
      render: (m) => gbp(mo.byMember[m.name] ?? 0, true),
      sortValue: (m) => mo.byMember[m.name] ?? 0,
      footer: () => gbp(mo.total, true),
    })),
    { key: "turnover", header: "Total", width: 130, align: "right", render: (m) => gbp(m.turnover), footer: () => gbp(data?.totals.turnover ?? 0) },
  ];

  function productColumns(list: MemberSalesProduct[], measure: "value" | "qty"): Column<MemberSalesProduct>[] {
    const by = (p: MemberSalesProduct, name: string) => {
      const b = p.byMember[name];
      return b ? (measure === "value" ? b.value : b.qty) : 0;
    };
    return [
      { key: "rank", header: "#", fit: true, align: "right", render: (p) => list.indexOf(p) + 1, noSort: true },
      { key: "stockCode", header: "Stock code", fit: true, footer: () => "Total" },
      { key: "description", header: "Description", footer: () => `${list.length} product lines` },
      { key: "value", header: "Value", width: 110, align: "right", render: (p) => gbp(p.value), footer: (rows) => gbp(rows.reduce((s, p) => s + p.value, 0)) },
      { key: "qty", header: "Quantity", width: 90, align: "right", render: (p) => qty(p.qty), footer: (rows) => qty(rows.reduce((s, p) => s + p.qty, 0)) },
      ...members.map<Column<MemberSalesProduct>>((m) => ({
        key: `m:${m.name}`,
        header: m.name,
        width: 108,
        align: "right",
        render: (p) => (measure === "value" ? gbp(by(p, m.name), true) : qty(by(p, m.name), true)),
        sortValue: (p) => by(p, m.name),
        footer: (rows) => {
          const v = rows.reduce((s, p) => s + by(p, m.name), 0);
          return measure === "value" ? gbp(v, true) : qty(v, true);
        },
      })),
    ];
  }

  const emptyText = loading ? "Loading…" : never ? "Nothing synced from Sage yet" : "Nothing in this period";

  return (
    <div className="po-open-layout">
      <div className="po-main">
        <div className="toolbar">
          <ToolbarDate label="From" value={from} onChange={setFrom} />
          <ToolbarDate label="To" value={to} onChange={setTo} />
          <span className="toolbar-gap" />
          <button className="toolbar-btn" onClick={() => preset("thisYear")}>
            This year
          </button>
          <button className="toolbar-btn" onClick={() => preset("lastYear")}>
            Last year
          </button>
          <button className="toolbar-btn" onClick={() => preset("last12")}>
            Last 12 months
          </button>
          <span className="toolbar-note" style={{ marginLeft: 10 }} title={syncTitle(data)}>
            {loading ? "Loading…" : waiting ? "Refresh requested — the Sage sidecar is reading Sage (a few minutes)…" : syncNote(data)}
          </span>
          <div className="toolbar-right">
            <button className="toolbar-btn" onClick={onEdit} title="Rename the report, or change its members and their Sage accounts">
              Edit report…
            </button>
            <button className="toolbar-btn" onClick={() => void exportXlsx()} disabled={!data || exporting}>
              {exporting ? "Building…" : "Export to Excel"}
            </button>
            <button className="btn-primary" onClick={() => void refreshFromSage()} disabled={waiting}>
              {waiting ? "Refreshing…" : "Refresh from Sage"}
            </button>
          </div>
        </div>

        {never && !waiting && (
          <div className="rp-banner">
            <strong>No sales figures from Sage yet.</strong> The Sage sidecar on the server reads the audit trail and
            invoice lines in and keeps them fresh — press <em>Refresh from Sage</em> to ask it now.
            {sync?.lastError && <div className="rp-banner-error">Last attempt failed: {sync.lastError}</div>}
          </div>
        )}
        {!never && !waiting && sync?.lastError && (
          <div className="rp-banner rp-banner-warn">
            Last refresh failed ({fmtDateTime(sync.lastRunUtc)}): {sync.lastError}. Showing the previous figures.
          </div>
        )}
        {data && sync?.windowFrom && new Date(from) < new Date(sync.windowFrom) && (
          <div className="rp-banner rp-banner-warn">
            The Sage mirror starts on {fmtDate(sync.windowFrom)}; nothing before that date is included.
          </div>
        )}
        {data && data.unsplit.length > 0 && (tab === "topValue" || tab === "topQty" || tab === "products") && (
          <div className="rp-banner rp-banner-warn">
            {data.unsplit.length === 1 ? "One invoice" : `${data.unsplit.length} invoices`} worth {money(unsplitTotal)}{" "}
            {data.unsplit.length === 1 ? "is" : "are"} in the turnover but {data.unsplit.length === 1 ? "was" : "were"} deleted
            from Sage's invoice list after posting, so {data.unsplit.length === 1 ? "its" : "their"} product lines cannot be
            read and {data.unsplit.length === 1 ? "it is" : "they are"} not in this split:
            <ul>
              {data.unsplit.map((u) => (
                <li key={`${u.accountRef}-${u.invRef}`}>
                  {fmtDate(u.date)} · {u.member} ({u.accountRef}) · {u.type} {u.invRef} · {money(u.net)}
                  {u.details ? ` · "${u.details}"` : ""}
                </li>
              ))}
            </ul>
          </div>
        )}

        {data && (
          <div className="rp-figures">
            <div className="rp-figure">
              <span className="rp-figure-k">Purchase turnover</span>
              <span className="rp-figure-v">
                {money(data.totals.turnover)}
                <small>net of VAT</small>
              </span>
            </div>
            <div className="rp-figure">
              <span className="rp-figure-k">Invoices / credit notes</span>
              <span className="rp-figure-v">
                {data.totals.invoices.toLocaleString()} <small>/ {data.totals.credits.toLocaleString()}</small>
              </span>
            </div>
            <div className="rp-figure">
              <span className="rp-figure-k">Members</span>
              <span className="rp-figure-v">
                {members.length}
                <small>{members.reduce((s, m) => s + m.accounts.length, 0)} Sage accounts</small>
              </span>
            </div>
            <div className="rp-figure">
              <span className="rp-figure-k">Period</span>
              <span className="rp-figure-v">
                {fmtDate(data.from)} – {fmtDate(data.to)}
              </span>
            </div>
            <div className="rp-figure">
              <span className="rp-figure-k">Product lines</span>
              <span className="rp-figure-v">{products.length}</span>
            </div>
          </div>
        )}

        <div className={`${cc.tabs} rp-tabs`}>
          {TABS.map((t) => (
            <button key={t.key} className={t.key === tab ? `${cc.tab} ${cc.tabActive}` : cc.tab} onClick={() => setTab(t.key)}>
              {t.label}
            </button>
          ))}
        </div>

        {tab === "summary" && (
          <DataTable
            sortable
            columns={summaryColumns}
            rows={members}
            rowKey={(m) => m.name}
            selectedKey={selected}
            onSelect={(m) => setSelected(m.name)}
            emptyText={emptyText}
          />
        )}
        {tab === "monthly" && (
          <DataTable sortable columns={monthlyColumns} rows={members} rowKey={(m) => m.name} emptyText={emptyText} />
        )}
        {tab === "topValue" && (
          <DataTable
            columns={productColumns(products.slice(0, 20), "value")}
            rows={products.slice(0, 20)}
            rowKey={(p) => p.stockCode}
            emptyText={emptyText}
          />
        )}
        {tab === "topQty" && (
          <DataTable columns={productColumns(topByQty, "qty")} rows={topByQty} rowKey={(p) => p.stockCode} emptyText={emptyText} />
        )}
        {tab === "products" && (
          <DataTable sortable columns={productColumns(products, "value")} rows={products} rowKey={(p) => p.stockCode} emptyText={emptyText} />
        )}
      </div>

      {tab === "summary" && (
        <div className="po-side" style={{ width: 330 }}>
          <h3 className="po-side-title">{selectedMember ? `${selectedMember.name} — Sage accounts` : "Sage accounts"}</h3>
          {selectedMember ? (
            <AccountList member={selectedMember} months={months} />
          ) : (
            <div className="rp-empty">Select a member to see its Sage accounts</div>
          )}
        </div>
      )}

      {waiting && (
        <SageRefreshOverlay
          what="sales figures"
          typically="usually around five minutes"
          startedAt={waitStart}
          onCancel={cancelRefresh}
        />
      )}
    </div>
  );
}

// Kept short: with the side panel open the toolbar has little room beside the date controls.
// The full "how many, by which machine" line is the tooltip.
function syncNote(data: MemberSalesReport | null): string {
  const s = data?.sync;
  if (!s?.lastSuccessUtc) return "";
  const since = s.windowFrom ? ` · from ${fmtDate(s.windowFrom)}` : "";
  return `Sage synced ${fmtDateTime(s.lastSuccessUtc)}${since}`;
}

function syncTitle(data: MemberSalesReport | null): string | undefined {
  const s = data?.sync;
  if (!s?.lastSuccessUtc) return undefined;
  return `${s.rowCount.toLocaleString()} invoices and credit notes, ${s.detailCount.toLocaleString()} item lines, read from Sage ${fmtDateTime(s.lastSuccessUtc)}${s.machine ? ` by ${s.machine}` : ""}`;
}

// The selected member's Sage accounts with what each bought — the branch split for a
// consolidated member such as a co-op with several stores.
function AccountList({ member, months: _months }: { member: MemberSalesMember; months: MemberSalesMonth[] }) {
  return (
    <div className="rp-acc-list">
      {member.accounts.map((a) => (
        <div key={a.accountRef} className="rp-acc">
          <span className="rp-acc-ref">{a.accountRef}</span>
          <span className="rp-acc-sum">{gbp(a.turnover)}</span>
          <span className="rp-acc-name" title={a.name}>
            {a.name}
          </span>
          <span className="rp-acc-meta">
            {a.invoices || a.credits
              ? `${a.invoices} invoice${a.invoices === 1 ? "" : "s"}${a.credits ? `, ${a.credits} credit note${a.credits === 1 ? "" : "s"}` : ""}`
              : "No transactions in the period"}
          </span>
        </div>
      ))}
      {member.accounts.length > 1 && (
        <div className="rp-acc-total">
          <span>{member.name} total</span>
          <span>{gbp(member.turnover)}</span>
        </div>
      )}
    </div>
  );
}
