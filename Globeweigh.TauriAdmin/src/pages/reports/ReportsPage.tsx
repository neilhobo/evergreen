import { KeyboardEvent, useEffect, useState } from "react";
import { AdminUserDto } from "../../types";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import { messageDialog } from "../../components/confirmDialog";
import { getSageReports, SageReport } from "../../api/reports";
import MemberSalesReportPanel from "./MemberSalesReportPanel";
import ReportEditorModal from "./ReportEditorModal";
import ActiveCustomersPanel from "./ActiveCustomersPanel";
import "./Reports.css";

// The Reports menu: the report groups down the left with their reports under them, the chosen
// report on the right. Sage Financial Reports are saved reports (api/reports.ts) — each a name
// and the Sage accounts it adds up — built on the Sage sales mirror the sidecar keeps fresh, so
// the page is instant and still works when Sage is down (it just says how old the figures are).
// New ones are made here; the Allied Merchant Buying Group was the first.

// What the editor is open for: a new report, or an existing one.
type Editing = { mode: "new" } | { mode: "edit"; report: SageReport } | null;

// The rail's groups. Sage Financial Reports are the saved, editable ones; Other holds fixed
// reports built into the app (keys prefixed "other:" so they never meet a saved report's key).
type GroupKey = "sage" | "other";
const CLOSED_KEY = "reports.closedGroups";
const ACTIVE_CUSTOMERS = "other:active-customers";
const OTHER_REPORTS = [
  { key: ACTIVE_CUSTOMERS, name: "Active Customers", title: "Customers with at least one sales invoice in Sage since a date" },
];

export default function ReportsPage(_props: { user: AdminUserDto; initialTab?: "summary" | "monthly" | "topValue" | "topQty" | "products" }) {
  const [reports, setReports] = useState<SageReport[] | null>(null);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [editing, setEditing] = useState<Editing>(null);
  // Which groups in the rail are folded away (remembered per PC).
  const [closed, setClosed] = useState<Record<GroupKey, boolean>>(() => {
    try {
      return { sage: false, other: false, ...JSON.parse(localStorage.getItem(CLOSED_KEY) ?? "{}") };
    } catch {
      return { sage: false, other: false };
    }
  });
  // Bumped after the selected report's members change, so the panel rebuilds it.
  const [reloadToken, setReloadToken] = useState(0);

  async function loadReports(select?: string | null) {
    try {
      const list = await getSageReports();
      setReports(list);
      setSelectedKey((cur) => {
        const want = select !== undefined ? select : cur;
        if (want && OTHER_REPORTS.some((o) => o.key === want)) return want;
        return want && list.some((r) => r.key === want) ? want : (list[0]?.key ?? OTHER_REPORTS[0].key);
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
  const selectedOther = OTHER_REPORTS.find((o) => o.key === selectedKey) ?? null;

  function toggleGroup(g: GroupKey) {
    setClosed((cur) => {
      const next = { ...cur, [g]: !cur[g] };
      try {
        localStorage.setItem(CLOSED_KEY, JSON.stringify(next));
      } catch {
        // remembered for this session only
      }
      return next;
    });
  }

  const pick = (key: string) => ({
    className: key === selectedKey ? "active" : undefined,
    onClick: () => setSelectedKey(key),
    tabIndex: 0,
    onKeyDown: (e: KeyboardEvent) => {
      if (e.key === "Enter" || e.key === " ") setSelectedKey(key);
    },
  });

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title="Reports" meta={selected?.name ?? selectedOther?.name ?? ""} />
        <div className={cc.body}>
          <aside className={`${cc.listCol} ${cc.listColTinted} rp-rail`} style={{ flexBasis: 220 }}>
            <div className="side-list-title rail">Reports</div>
            <div className="rp-rail-groups">
              <section className={closed.sage ? "rp-group" : "rp-group is-open"}>
                <button className="rp-group-head" onClick={() => toggleGroup("sage")} aria-expanded={!closed.sage}>
                  <span className="rp-group-chev" aria-hidden />
                  Sage Financial Reports
                  <span className="rp-group-count">{reports?.length ?? "…"}</span>
                </button>
                {!closed.sage && (
                  <>
                    <ul className="side-list">
                      {reports === null && <li className="rp-nav-note">Loading…</li>}
                      {reports?.length === 0 && <li className="rp-nav-note">No reports yet</li>}
                      {reports?.map((r) => (
                        <li
                          key={r.key}
                          {...pick(r.key)}
                          title={`${r.memberCount} member${r.memberCount === 1 ? "" : "s"}, ${r.accountCount} Sage account${r.accountCount === 1 ? "" : "s"}`}
                        >
                          {r.name}
                        </li>
                      ))}
                    </ul>
                    <button className="rp-nav-new" onClick={() => setEditing({ mode: "new" })} disabled={reports === null}>
                      + New report
                    </button>
                  </>
                )}
              </section>

              <section className={closed.other ? "rp-group" : "rp-group is-open"}>
                <button className="rp-group-head" onClick={() => toggleGroup("other")} aria-expanded={!closed.other}>
                  <span className="rp-group-chev" aria-hidden />
                  Other
                  <span className="rp-group-count">{OTHER_REPORTS.length}</span>
                </button>
                {!closed.other && (
                  <ul className="side-list">
                    {OTHER_REPORTS.map((o) => (
                      <li key={o.key} {...pick(o.key)} title={o.title}>
                        {o.name}
                      </li>
                    ))}
                  </ul>
                )}
              </section>
            </div>
          </aside>

          {selectedOther?.key === ACTIVE_CUSTOMERS ? (
            <ActiveCustomersPanel />
          ) : selected ? (
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
