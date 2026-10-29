import { MouseEvent, useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  downloadSageComparisonCsv,
  getSageComparison,
  getSageComparisonDetail,
  ignoreSageAccounts,
  unignoreSageAccounts,
  SageAddressSide,
  SageComparisonIssue,
  SageComparisonResult,
  SageCustomerSide,
  SageSideBySide,
  SageSyncState,
} from "../../api/sage";
import { fmtDate, fmtDateTime, fmtQty2 } from "../../format";
import cc from "../../styles/ContentCard.module.css";
import { saveBlob } from "./saveBlob";
import CopyMissingModal from "./CopyMissingModal";

const ALL = "All issues";
// SageComparison.MissingInEvergreen: the one category with an action, "Copy to Evergreen".
const MISSING = "Missing in Evergreen";
// SageComparison.Ignored: Missing accounts the office will never copy. Not issues, so kept out
// of "All issues"; their own category is where they are found and un-ignored.
const IGNORED = "Ignored";

type IssueRow = SageComparisonIssue & { id: number };

// The Comparison tab: Sage is the source of truth, and this lists everything on the Evergreen
// side that disagrees with it — one row per issue, filtered by category, with the account as
// both systems hold it beside the grid. The one action is "Copy to Evergreen" on Missing in
// Evergreen, which creates those customers after a popup shows exactly what will be made.
export default function ComparisonPanel({ onSync }: { onSync: (sync: SageSyncState) => void }) {
  const [data, setData] = useState<SageComparisonResult | null>(null);
  const [rows, setRows] = useState<IssueRow[]>([]);
  const [loading, setLoading] = useState(false);
  // Also list a telephone / contact / email Sage has that Evergreen left blank. Off by
  // default: there are thousands, and they drown the real conflicts.
  const [includeBlanks, setIncludeBlanks] = useState(false);
  // Only accounts that have orders in Evergreen — the ones a stale record would actually bite.
  const [withOrders, setWithOrders] = useState(false);
  const [category, setCategory] = useState<string>(ALL);
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<IssueRow | null>(null);
  const [detail, setDetail] = useState<SageSideBySide | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [exporting, setExporting] = useState(false);
  // The accounts "Copy to Evergreen" should copy (null = closed; empty = the whole list).
  const [copying, setCopying] = useState<string[] | null>(null);
  // Ctrl/Shift+click picks several rows (Missing in Evergreen: copy just those). The anchor is
  // the last plain- or ctrl-clicked row, where a shift range starts.
  const [checked, setChecked] = useState<Set<number>>(new Set());
  const [anchorId, setAnchorId] = useState<number | null>(null);

  async function load(blanks = includeBlanks) {
    setLoading(true);
    try {
      const d = await getSageComparison(blanks);
      setData(d);
      setRows(d.issues.map((i, n) => ({ ...i, id: n })));
      onSync(d.sync);
    } catch (err) {
      void messageDialog("Failed to build the comparison.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function toggleBlanks(on: boolean) {
    setIncludeBlanks(on);
    void load(on);
  }

  const term = search.trim().toLowerCase();
  const filtered = useMemo(
    () =>
      rows.filter(
        (i) =>
          (category === ALL ? i.category !== IGNORED : i.category === category) &&
          (!withOrders || i.orders > 0) &&
          (!term ||
            [i.accountRef, i.name, i.detail, i.evergreen, i.sage].some((v) => v?.toLowerCase().includes(term))),
      ),
    [rows, category, withOrders, term],
  );

  // Counts for the category list: the with-orders filter applies, the search does not.
  const counts = useMemo(() => {
    const m = new Map<string, number>();
    for (const i of rows) {
      if (withOrders && i.orders <= 0) continue;
      m.set(i.category, (m.get(i.category) ?? 0) + 1);
    }
    return m;
  }, [rows, withOrders]);
  const categories = data ? Object.keys(data.summary.byCategory) : [];
  const total = [...counts.entries()].reduce((a, [c, n]) => (c === IGNORED ? a : a + n), 0);

  function handleSelect(row: IssueRow, e?: MouseEvent, view?: IssueRow[]) {
    const shown = view ?? filtered;
    if (e?.shiftKey && anchorId != null) {
      const a = shown.findIndex((r) => r.id === anchorId);
      const b = shown.findIndex((r) => r.id === row.id);
      if (a !== -1 && b !== -1) {
        const [lo, hi] = a < b ? [a, b] : [b, a];
        setChecked(new Set(shown.slice(lo, hi + 1).map((r) => r.id)));
      }
    } else if (e?.ctrlKey || e?.metaKey) {
      setChecked((prev) => {
        // The first ctrl-click keeps the row already highlighted, as Explorer does.
        const next = new Set(prev.size === 0 && selected ? [selected.id] : prev);
        if (next.has(row.id)) next.delete(row.id);
        else next.add(row.id);
        return next;
      });
      setAnchorId(row.id);
    } else {
      setChecked(new Set());
      setAnchorId(row.id);
    }
    void selectRow(row);
  }

  // A new list, category or filter starts the picking again.
  useEffect(() => {
    setChecked(new Set());
    setAnchorId(null);
  }, [rows, category, withOrders, term]);

  // What Ignore / Stop ignoring act on: the picked rows of that category, else the
  // highlighted row if it is one.
  const actOn = (cat: string) => {
    const picked = filtered.filter((r) => checked.has(r.id) && r.category === cat).map((r) => r.accountRef);
    if (picked.length > 0) return picked;
    return selected && selected.category === cat ? [selected.accountRef] : [];
  };
  // Copy selected and Ignore both act on the same Missing rows: one highlighted, or several picked.
  const toIgnore = actOn(MISSING);
  const toUnignore = actOn(IGNORED);
  const [ignoring, setIgnoring] = useState(false);

  async function setIgnored(refs: string[], ignore: boolean) {
    const what = refs.length === 1 ? refs[0] : `${refs.length} accounts`;
    const ask = ignore
      ? `Ignore ${what}?\nThey leave Missing in Evergreen and are never copied. Find them under Ignored to undo.`
      : `Stop ignoring ${what}?\nThey go back to Missing in Evergreen.`;
    if (!(await confirmDialog(ask))) return;
    setIgnoring(true);
    try {
      await (ignore ? ignoreSageAccounts(refs) : unignoreSageAccounts(refs));
      setSelected(null);
      await load();
    } catch (err) {
      void messageDialog((ignore ? "Ignore" : "Stop ignoring") + " failed.\n" + (err as Error).message);
    } finally {
      setIgnoring(false);
    }
  }

  async function selectRow(row: IssueRow) {
    setSelected(row);
    if (detail?.accountRef === row.accountRef) return;
    setDetailLoading(true);
    try {
      setDetail(await getSageComparisonDetail(row.accountRef));
    } catch (err) {
      setDetail(null);
      void messageDialog("Failed to load the account.\n" + (err as Error).message);
    } finally {
      setDetailLoading(false);
    }
  }

  async function exportCsv() {
    setExporting(true);
    try {
      const blob = await downloadSageComparisonCsv({
        includeBlanks,
        withOrders,
        category: category === ALL ? undefined : category,
      });
      await saveBlob(blob, `Sage comparison ${stamp()}.csv`, "csv");
    } catch (err) {
      void messageDialog("Export failed.\n" + (err as Error).message);
    } finally {
      setExporting(false);
    }
  }

  const columns: Column<IssueRow>[] = [
    ...(category === ALL
      ? ([{ key: "category", header: "Category", fit: true }] as Column<IssueRow>[])
      : []),
    { key: "accountRef", header: "A/C", fit: true, render: (r) => <Highlight text={r.accountRef} term={term} /> },
    { key: "name", header: "Name", render: (r) => <Highlight text={r.name} term={term} /> },
    { key: "detail", header: "Detail", render: (r) => <Highlight text={r.detail} term={term} /> },
    {
      key: "evergreen",
      header: "Evergreen",
      render: (r) => <Highlight text={r.evergreen} term={term} />,
    },
    { key: "sage", header: "Sage", render: (r) => <Highlight text={r.sage} term={term} /> },
    { key: "sageAddressType", header: "Type", fit: true, render: (r) => r.sageAddressType ?? "" },
    { key: "orders", header: "Orders", width: 58, align: "right", render: (r) => r.orders || "" },
    { key: "lastOrder", header: "Last order", width: 86, render: (r) => fmtDate(r.lastOrder) },
  ];

  const sync = data?.sync ?? null;
  const noMirror = !!sync && !sync.lastSuccessUtc;
  const summary = data?.summary;

  return (
    <>
      <aside className={cc.listCol} style={{ flexBasis: 200 }}>
        <div className={cc.colHeader}>
          <h2 className={cc.colTitle}>Category</h2>
        </div>
        <div className={cc.navList}>
          <CategoryItem label={ALL} count={total} active={category === ALL} onClick={() => setCategory(ALL)} />
          {categories
            .filter((c) => (counts.get(c) ?? 0) > 0)
            .map((c) => (
              <CategoryItem
                key={c}
                label={c}
                count={counts.get(c) ?? 0}
                active={category === c}
                onClick={() => setCategory(c)}
              />
            ))}
        </div>
      </aside>

      <div className="po-open-layout">
        <div className="po-main">
          <div className="toolbar">
            <input
              className="form-input toolbar-search"
              placeholder="Search issues…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <span className="toolbar-note">
              {loading
                ? "Comparing…"
                : summary
                  ? `${summary.customersWithIssues.toLocaleString()} of ${summary.matched.toLocaleString()} matched accounts differ` +
                    ` (${summary.customersWithOrdersWithIssues} with orders) · Sage ${summary.sageCustomers.toLocaleString()}` +
                    ` / Evergreen ${summary.evergreenCustomers.toLocaleString()} customers` +
                    (sync?.lastSuccessUtc ? ` · Sage side as of ${fmtDateTime(sync.lastSuccessUtc)}` : "")
                  : ""}
            </span>
            <div className="toolbar-right">
              <label className={cc.toolbarCheck}>
                <input type="checkbox" checked={withOrders} onChange={(e) => setWithOrders(e.target.checked)} />
                With orders only
              </label>
              <label className={cc.toolbarCheck} title="Also list a telephone, contact or email Sage has and Evergreen left blank">
                <input type="checkbox" checked={includeBlanks} onChange={(e) => toggleBlanks(e.target.checked)} />
                Include blanks
              </label>
              {category === MISSING && (counts.get(MISSING) ?? 0) > 0 && (
                <button
                  className="toolbar-btn"
                  disabled={loading || ignoring || toIgnore.length === 0}
                  title="Never copy these accounts: they move to the Ignored category"
                  onClick={() => void setIgnored(toIgnore, true)}
                >
                  {toIgnore.length > 1 ? `Ignore ${toIgnore.length} selected` : "Ignore"}
                </button>
              )}
              {category === IGNORED && (
                <button
                  className="toolbar-btn"
                  disabled={loading || ignoring || toUnignore.length === 0}
                  title="Put these accounts back on Missing in Evergreen"
                  onClick={() => void setIgnored(toUnignore, false)}
                >
                  {toUnignore.length > 1 ? `Stop ignoring ${toUnignore.length} selected` : "Stop ignoring"}
                </button>
              )}
              {category === MISSING && (counts.get(MISSING) ?? 0) > 0 && (
                <>
                  <button
                    className="btn-blue"
                    disabled={loading || toIgnore.length === 0}
                    title="Create the selected account(s) as Evergreen customers (Ctrl/Shift+click to pick several)"
                    onClick={() => setCopying(toIgnore)}
                  >
                    {toIgnore.length > 1 ? `Copy ${toIgnore.length} selected` : "Copy selected"}
                  </button>
                  <button
                    className="btn-blue"
                    disabled={loading}
                    title="Create every account in this list as an Evergreen customer"
                    onClick={() => setCopying([])}
                  >
                    Copy all to Evergreen
                  </button>
                </>
              )}
              <button className="btn-primary" onClick={() => void exportCsv()} disabled={exporting || loading || rows.length === 0}>
                {exporting ? "Exporting…" : "Export CSV"}
              </button>
              <button className="toolbar-btn" onClick={() => void load()} disabled={loading}>
                Reload
              </button>
            </div>
          </div>

          {noMirror && (
            <div className="sl-banner">
              <strong>Nothing to compare against yet.</strong> The Sage customers have not been synced — use{" "}
              <em>Refresh from Sage</em> on the Customers tab first.
            </div>
          )}
          {!noMirror && !loading && data && rows.length === 0 && (
            <div className="sl-banner">
              <strong>Evergreen matches Sage.</strong> No differences found across {summary?.matched.toLocaleString()} accounts.
            </div>
          )}

          {!noMirror && (
            <DataTable
              sortable
              columns={columns}
              rows={filtered}
              rowKey={(r) => r.id}
              selectedKey={selected?.id ?? null}
              rowClass={(r) => (checked.has(r.id) ? "selected" : undefined)}
              onSelect={handleSelect}
              emptyText={loading ? "Comparing…" : "No issues match"}
            />
          )}
        </div>

        <div className="po-side" style={{ width: 340 }}>
          <h3 className="po-side-title">{detail ? `${detail.accountRef} — Evergreen vs Sage` : "Account"}</h3>
          {detailLoading ? (
            <div className="sl-empty">Loading…</div>
          ) : detail ? (
            <SideBySide d={detail} />
          ) : (
            <div className="sl-empty">Select an issue to see the account as both systems hold it</div>
          )}
        </div>
      </div>
      {copying && (
        <CopyMissingModal
          only={copying.length > 0 ? copying : undefined}
          onClose={(copied) => {
            setCopying(null);
            if (copied) void load();
          }}
        />
      )}
    </>
  );
}

function CategoryItem({ label, count, active, onClick }: { label: string; count: number; active: boolean; onClick: () => void }) {
  return (
    <div
      className={active ? `${cc.navItem} ${cc.navItemActive}` : cc.navItem}
      onClick={onClick}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") onClick();
      }}
    >
      <span className="sl-cat-label">{label}</span>
      <span className="sl-cat-count">{count.toLocaleString()}</span>
    </div>
  );
}

// Both records, Evergreen above Sage (the panel is too narrow for two columns), with the
// customer name marked where it differs. The issue rows already say exactly what differs
// on each address, so the address lists are shown plain.
function SideBySide({ d }: { d: SageSideBySide }) {
  const nameDiffers =
    !!d.evergreen && !!d.sage && norm(d.evergreen.name) !== norm(d.sage.name);
  return (
    <div className="sl-sbs">
      <Side title="Evergreen" side={d.evergreen} missing="Not in Evergreen" nameDiffers={nameDiffers} />
      <Side title="Sage" side={d.sage} missing="Not in Sage" nameDiffers={nameDiffers} sage />
    </div>
  );
}

function Side({
  title,
  side,
  missing,
  nameDiffers,
  sage,
}: {
  title: string;
  side: SageCustomerSide | null;
  missing: string;
  nameDiffers: boolean;
  sage?: boolean;
}) {
  return (
    <section className="sl-side-section">
      <h4 className={`sl-side-h${sage ? " sage" : ""}`}>{title}</h4>
      {!side ? (
        <div className="sl-empty">{missing}</div>
      ) : (
        <>
          <div className="sl-kv">
            <b className={nameDiffers ? "sl-diff" : undefined}>{side.name || "—"}</b>
          </div>
          <div className="sl-kv sl-kv-muted">
            {side.inactive ? "Inactive" : "Active"}
            {side.onHold && <span className="sl-badge sl-badge-hold">On hold</span>}
            {side.taxCode != null && ` · T${side.taxCode}`}
            {side.balance != null && side.balance !== 0 && ` · balance ${fmtQty2(side.balance)}`}
            {!sage && ` · ${side.orders} order${side.orders === 1 ? "" : "s"}`}
            {!sage && side.lastOrder && `, last ${fmtDate(side.lastOrder)}`}
          </div>
          <div className="sl-addr-list sl-addr-list-compact">
            {side.addresses.length === 0 && <div className="sl-empty">No addresses</div>}
            {side.addresses.map((a) => (
              <AddressCard key={a.id ?? a.number ?? 0} a={a} />
            ))}
          </div>
        </>
      )}
    </section>
  );
}

function AddressCard({ a }: { a: SageAddressSide }) {
  const kind = (a.type ?? "").toLowerCase();
  const meta = [a.contact, a.telephone, a.email].filter(Boolean).join(" · ");
  return (
    <div className="sl-addr">
      <div className="sl-addr-head">
        {a.type && <span className={`sl-type sl-type-${kind}`}>{a.type}</span>}
        {a.number != null && <span className="sl-addr-no">#{a.number}</span>}
        {a.id != null && <span className="sl-addr-no">id {a.id}</span>}
        {a.description && <span className="sl-addr-desc">{a.description}</span>}
      </div>
      {a.name && <div className="sl-addr-name">{a.name}</div>}
      <div className="sl-addr-lines">{a.lines.join(", ")}</div>
      {meta && <div className="sl-addr-meta">{meta}</div>}
      {a.modified && <div className="sl-addr-meta">edited in Evergreen {fmtDateTime(a.modified)}</div>}
    </div>
  );
}

function norm(s: string | null): string {
  return (s ?? "").trim().toUpperCase().replace(/\s+/g, " ");
}

function stamp(): string {
  const n = new Date();
  return `${n.getFullYear()}-${String(n.getMonth() + 1).padStart(2, "0")}-${String(n.getDate()).padStart(2, "0")}`;
}
