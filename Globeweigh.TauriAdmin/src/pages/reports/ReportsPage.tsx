import { useEffect, useState } from "react";
import { AdminUserDto } from "../../types";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import { messageDialog } from "../../components/confirmDialog";
import { getSageReports, SageReport } from "../../api/reports";
import MemberSalesReportPanel from "./MemberSalesReportPanel";
import ReportEditorModal from "./ReportEditorModal";
import "./Reports.css";

// The Reports menu: the report groups down the left with their reports under them, the chosen
// report on the right. Sage Financial Reports are saved reports (api/reports.ts) — each a name
// and the Sage accounts it adds up — built on the Sage sales mirror the sidecar keeps fresh, so
// the page is instant and still works when Sage is down (it just says how old the figures are).
// New ones are made here; the Allied Merchant Buying Group was the first.

// What the editor is open for: a new report, or an existing one.
type Editing = { mode: "new" } | { mode: "edit"; report: SageReport } | null;

export default function ReportsPage(_props: { user: AdminUserDto; initialTab?: "summary" | "monthly" | "topValue" | "topQty" | "products" }) {
  const [reports, setReports] = useState<SageReport[] | null>(null);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [editing, setEditing] = useState<Editing>(null);
  // Bumped after the selected report's members change, so the panel rebuilds it.
  const [reloadToken, setReloadToken] = useState(0);

  async function loadReports(select?: string | null) {
    try {
      const list = await getSageReports();
      setReports(list);
      setSelectedKey((cur) => {
        const want = select !== undefined ? select : cur;
        return want && list.some((r) => r.key === want) ? want : (list[0]?.key ?? null);
      });
    } catch (err) {
      setReports((cur) => cur ?? []);
      void messageDialog("Failed to load the reports.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    void loadReports();
  }, []);

  const selected = reports?.find((r) => r.key === selectedKey) ?? null;

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title="Reports" meta={selected?.name ?? ""} />
        <div className={cc.body}>
          <aside className={cc.listCol} style={{ flexBasis: 250 }}>
            <div className={cc.colHeader}>
              <h2 className={cc.colTitle}>Reports</h2>
            </div>
            <div className={cc.navList}>
              <div className="rp-nav-group">Sage Financial Reports</div>
              {reports === null && <div className="rp-nav-note">Loading…</div>}
              {reports?.length === 0 && <div className="rp-nav-note">No reports yet</div>}
              {reports?.map((r) => (
                <div
                  key={r.key}
                  className={`${r.key === selectedKey ? `${cc.navItem} ${cc.navItemActive}` : cc.navItem} rp-nav-child`}
                  onClick={() => setSelectedKey(r.key)}
                  role="button"
                  tabIndex={0}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") setSelectedKey(r.key);
                  }}
                  title={`${r.memberCount} member${r.memberCount === 1 ? "" : "s"}, ${r.accountCount} Sage account${r.accountCount === 1 ? "" : "s"}`}
                >
                  {r.name}
                </div>
              ))}
              <button className="rp-nav-new" onClick={() => setEditing({ mode: "new" })} disabled={reports === null}>
                + New report
              </button>
            </div>
          </aside>

          {selected ? (
            <MemberSalesReportPanel
              key={selected.key}
              reportKey={selected.key}
              title={selected.name}
              initialTab={_props.initialTab}
              reloadToken={reloadToken}
              onEdit={() => setEditing({ mode: "edit", report: selected })}
            />
          ) : (
            reports !== null && (
              <div className="rp-start">
                <strong>No Sage financial reports yet.</strong>
                <span>A report adds up what a set of Sage customer accounts bought — pick the accounts and give it a name.</span>
                <button className="btn-primary" onClick={() => setEditing({ mode: "new" })}>
                  New report
                </button>
              </div>
            )
          )}
        </div>
      </div>

      {editing && (
        <ReportEditorModal
          report={editing.mode === "edit" ? editing.report : null}
          onClose={(result) => {
            setEditing(null);
            if (!result) return;
            if (result.kind === "deleted") void loadReports(null);
            else {
              void loadReports(result.report.key);
              setReloadToken((t) => t + 1);
            }
          }}
        />
      )}
    </div>
  );
}
