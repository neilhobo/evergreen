import { useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import { LoginEventRow, getLoginHistory } from "../../api/loginHistory";
import "./LoginHistoryPanel.css";

// Super Admin > Login History: who signed in to TauriAdmin or the mobile app, when, from which
// app and from where (off-site through Cloudflare, or on the site network), the failed attempts
// with why, and each Switch user. Newest first; the API keeps it to the super-admin.

const KIND_LABEL: Record<string, string> = {
  SignIn: "Signed in",
  Failed: "Failed",
  SwitchUser: "Switch user",
  SwitchBack: "Returned",
};

const DAY_CHOICES = [1, 7, 30, 90, 365];

function when(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString("en-GB", {
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });
}

// "TauriAdmin" in the desktop shell vs a browser, from the user agent (the header says only the app).
function appLabel(r: LoginEventRow): string {
  const app = r.client ?? "Unknown";
  if (app !== "TauriAdmin" || !r.userAgent) return app;
  return /Edg\/|Chrome\//.test(r.userAgent) && !/Tauri|WebView2/i.test(r.userAgent) ? "TauriAdmin (browser)" : app;
}

export default function LoginHistoryPanel() {
  const [days, setDays] = useState(30);
  const [rows, setRows] = useState<LoginEventRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [failedOnly, setFailedOnly] = useState(false);
  const [offSiteOnly, setOffSiteOnly] = useState(false);

  const load = (d: number) => {
    setError(null);
    getLoginHistory(d)
      .then(setRows)
      .catch((err) => setError((err as Error).message));
  };

  useEffect(() => load(days), [days]);

  const shown = useMemo(() => {
    const term = search.trim().toLowerCase();
    return (rows ?? []).filter(
      (r) =>
        (!failedOnly || r.kind === "Failed") &&
        (!offSiteOnly || r.via === "Tunnel") &&
        (!term ||
          [r.fullName, r.username, r.byName, r.ipAddress, r.client, r.detail].some((v) =>
            v?.toLowerCase().includes(term),
          )),
    );
  }, [rows, search, failedOnly, offSiteOnly]);

  const failedCount = rows?.filter((r) => r.kind === "Failed").length ?? 0;

  const columns: Column<LoginEventRow>[] = [
    { key: "date", header: "When", width: 150, render: (r) => when(r.date), sortValue: (r) => r.date },
    {
      key: "who",
      header: "User",
      width: 200,
      render: (r) => (
        <span title={r.username ?? undefined}>
          {r.fullName || r.username || "—"}
          {!r.userId && r.username && <span className="lh-muted"> (no such user)</span>}
        </span>
      ),
      sortValue: (r) => r.fullName ?? r.username ?? "",
    },
    {
      key: "kind",
      header: "Result",
      width: 100,
      render: (r) => <span className={`lh-kind lh-kind-${r.kind}`}>{KIND_LABEL[r.kind] ?? r.kind}</span>,
    },
    { key: "detail", header: "Detail", render: (r) => r.detail ?? "" },
    { key: "client", header: "App", width: 150, render: appLabel, sortValue: appLabel },
    {
      key: "via",
      header: "From",
      width: 90,
      render: (r) => (r.via === "Tunnel" ? "Off-site" : r.via === "Site" ? "On site" : ""),
    },
    { key: "ipAddress", header: "Address", width: 140, render: (r) => r.ipAddress ?? "" },
  ];

  return (
    <div className="po-main lh-panel">
      <div className="toolbar">
        <select
          className="form-input"
          style={{ width: 130 }}
          value={days}
          onChange={(e) => setDays(Number(e.target.value))}
          aria-label="Period"
        >
          {DAY_CHOICES.map((d) => (
            <option key={d} value={d}>
              {d === 1 ? "Today" : `Last ${d} days`}
            </option>
          ))}
        </select>
        <input
          className="form-input toolbar-search"
          placeholder="Search user, address, app…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <label className="toolbar-label lh-check">
          <input type="checkbox" checked={failedOnly} onChange={(e) => setFailedOnly(e.target.checked)} /> Failed only
          {failedCount > 0 && ` (${failedCount})`}
        </label>
        <label className="toolbar-label lh-check">
          <input type="checkbox" checked={offSiteOnly} onChange={(e) => setOffSiteOnly(e.target.checked)} /> Off-site
          only
        </label>
        <div className="toolbar-right">
          <button className="toolbar-btn" onClick={() => load(days)}>
            Refresh
          </button>
        </div>
      </div>
      {error && <p className="lh-error">{error}</p>}
      <DataTable
        columns={columns}
        rows={shown}
        rowKey={(r) => r.id}
        rowClass={(r) => (r.kind === "Failed" ? "lh-failed" : undefined)}
        emptyText={rows === null ? "Loading…" : "No sign-ins in this period"}
        sortable
      />
    </div>
  );
}
