import { useEffect, useMemo, useRef, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import SageRefreshOverlay from "../../components/SageRefreshOverlay";
import {
  getSageCustomerAddresses,
  getSageCustomers,
  getSageSync,
  requestSageCustomerRefresh,
  cancelSageCustomerRefresh,
  SageAddressRow,
  SageCustomerRow,
  SageSyncState,
} from "../../api/sage";
import { AdminUserDto } from "../../types";
import { fmtDateTime, fmtQty2 } from "../../format";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import PageTabs from "../../widgets/PageTabs";
import ComparisonPanel from "./ComparisonPanel";
import "./SageLink.css";

const TABS = [
  { key: "customers", label: "Customers" },
  { key: "comparison", label: "Comparison" },
] as const;
type Tab = (typeof TABS)[number]["key"];

// Sage Link — what Evergreen can see of the client's Sage 50 Accounts. Nothing on this page
// talks to Sage itself: Globeweigh.SageSidecar, on the machine with Sage installed, reads
// the ledger into a mirror on the API (SageCustomersController) on a timer, and "Refresh
// from Sage" asks it to do that now. The page is therefore instant, and still works when
// Sage is down — it just says how old the list is.
export default function SageLinkPage(_props: { user: AdminUserDto }) {
  const [tab, setTab] = useState<Tab>("customers");
  const [sync, setSync] = useState<SageSyncState | null>(null);

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader
          title="Sage Link"
          meta={syncMeta(sync)}
        />
        <PageTabs tabs={TABS} active={tab} onChange={setTab} />
        <div className={cc.body}>
          {tab === "customers" && <CustomersPanel onSync={setSync} />}
          {tab === "comparison" && <ComparisonPanel onSync={setSync} />}
        </div>
      </div>
    </div>
  );
}

// The card header's meta. Kept short — the tab strip sits right after it and clips anything
// longer; the full "synced when, by whom" line is in the toolbar.
function syncMeta(sync: SageSyncState | null): string {
  if (!sync) return "";
  if (!sync.lastSuccessUtc) return "not synced yet";
  return `${sync.rowCount.toLocaleString()} in Sage · ${ago(sync.lastSuccessUtc)}`;
}

function ago(iso: string): string {
  const mins = Math.round((Date.now() - new Date(iso).getTime()) / 60_000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins} min ago`;
  const hours = Math.round(mins / 60);
  if (hours < 48) return `${hours} h ago`;
  return `${Math.round(hours / 24)} days ago`;
}

// The Customers tab: the mirrored Sage ledger with a search, the selected account's address
// book beside it, and the refresh control.
function CustomersPanel({ onSync }: { onSync: (sync: SageSyncState) => void }) {
  const [rows, setRows] = useState<SageCustomerRow[]>([]);
  const [sync, setSyncState] = useState<SageSyncState | null>(null);
  const [selected, setSelected] = useState<SageCustomerRow | null>(null);
  const [addresses, setAddresses] = useState<SageAddressRow[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(false);
  // A refresh has been asked for and the sidecar has not answered yet.
  const [waiting, setWaiting] = useState(false);
  // When the refresh was asked for (the cover's clock).
  const [waitStart, setWaitStart] = useState(0);
  const pollRef = useRef<number | null>(null);

  function setSync(s: SageSyncState) {
    setSyncState(s);
    onSync(s);
  }

  async function load() {
    setLoading(true);
    try {
      const data = await getSageCustomers();
      setRows(data.customers);
      setSync(data.sync);
      setSelected((cur) => (cur ? data.customers.find((r) => r.accountRef === cur.accountRef) ?? null : null));
    } catch (err) {
      void messageDialog("Failed to load Sage customers.\n" + (err as Error).message);
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
  }, []);

  // Ask for a fresh pull, then watch the sync row until it moves — the sidecar polls the
  // API every ~10 s and a full read of Sage takes about a minute. LastRunUtc changes on
  // success and on failure alike; LastError says which.
  async function refreshFromSage() {
    if (waiting) return;
    let before: string | null;
    try {
      const s = await requestSageCustomerRefresh();
      before = s.lastRunUtc;
      setSync(s);
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
        const s = (await getSageSync()).find((x) => x.entity === "Customers");
        if (!s) return;
        if (s.lastRunUtc !== before) {
          stopPolling();
          // The cover stays up through the reload, so the new list is there when it lifts.
          if (s.lastError) {
            setWaiting(false);
            setSync(s);
            void messageDialog("The Sage sidecar could not refresh the customers.\n" + s.lastError);
          } else {
            await load();
            setWaiting(false);
          }
        } else if (Date.now() - startedAt > 5 * 60_000) {
          stopPolling();
          setWaiting(false);
          setSync(s);
          void messageDialog(
            "No answer from the Sage sidecar after 5 minutes.\nIs the GlobeweighSageSidecar service running on the server?",
          );
        }
      } catch {
        // keep polling
      }
    }, 5000);
  }

  // Cancel from the cover: the API drops a request the sidecar has not picked up and stops a
  // read in progress within seconds. The list on the page stays as it was.
  async function cancelRefresh() {
    try {
      const s = await cancelSageCustomerRefresh();
      stopPolling();
      setSync(s);
      setWaiting(false);
    } catch (err) {
      void messageDialog("Could not cancel the refresh.\n" + (err as Error).message);
    }
  }

  async function selectRow(row: SageCustomerRow) {
    setSelected(row);
    try {
      setAddresses(await getSageCustomerAddresses(row.accountRef));
    } catch (err) {
      setAddresses([]);
      void messageDialog("Failed to load addresses.\n" + (err as Error).message);
    }
  }

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return rows;
    return rows.filter((r) =>
      [r.accountRef, r.name, r.address1, r.address3, r.address5, r.telephone, r.contact, r.email].some((v) =>
        v?.toLowerCase().includes(term),
      ),
    );
  }, [rows, term]);

  // A/C and Tax are best-fit, which also puts DataTable into its clamp mode: Name and
  // Address then share what is left with ellipses instead of pushing Balance, Status and
  // Addresses out past the side panel (which is what happened with two free columns).
  const columns: Column<SageCustomerRow>[] = [
    { key: "accountRef", header: "A/C", fit: true, render: (r) => <Highlight text={r.accountRef} term={term} /> },
    { key: "name", header: "Name", render: (r) => <Highlight text={r.name} term={term} /> },
    { key: "address1", header: "Address", render: (r) => <Highlight text={r.address1} term={term} /> },
    { key: "address3", header: "Town", width: 120, render: (r) => <Highlight text={r.address3} term={term} /> },
    { key: "address5", header: "Postcode", width: 84, render: (r) => <Highlight text={r.address5} term={term} /> },
    { key: "telephone", header: "Telephone", width: 112 },
    { key: "defTaxCode", header: "Tax", fit: true, align: "center", render: (r) => `T${r.defTaxCode}` },
    {
      key: "balance",
      header: "Balance",
      width: 90,
      align: "right",
      render: (r) => (r.balance ? fmtQty2(r.balance) : ""),
      sortValue: (r) => r.balance,
    },
    {
      key: "status",
      header: "Status",
      width: 78,
      align: "center",
      render: (r) =>
        r.onHold ? (
          <span className="sl-badge sl-badge-hold">On hold</span>
        ) : r.inactive ? (
          <span className="sl-badge sl-badge-inactive">Inactive</span>
        ) : (
          ""
        ),
      sortValue: (r) => (r.onHold ? 0 : r.inactive ? 1 : 2),
    },
    // How many entries the account has in Sage's address book (the book itself is in the
    // panel beside the grid); the tooltip says how many of them are delivery addresses.
    {
      key: "addressCount",
      header: "Addresses",
      width: 86,
      align: "right",
      render: (r) => <span title={`${r.deliveryAddressCount} delivery`}>{r.addressCount || ""}</span>,
      sortValue: (r) => r.addressCount,
    },
  ];

  const never = !!sync && !sync.lastSuccessUtc;

  return (
    <div className="po-open-layout">
      <div className="po-main">
        <div className="toolbar">
          <input
            className="form-input toolbar-search"
            placeholder="Search Sage customers…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <span className="toolbar-note">
            {loading ? "Loading…" : waiting ? "Refresh requested — waiting for the Sage sidecar…" : syncNote(sync)}
          </span>
          <div className="toolbar-right">
            <button className="btn-primary" onClick={() => void refreshFromSage()} disabled={waiting}>
              {waiting ? "Refreshing…" : "Refresh from Sage"}
            </button>
            <button className="toolbar-btn" onClick={() => void load()} disabled={loading}>
              Reload
            </button>
          </div>
        </div>

        {never && !waiting && (
          <div className="sl-banner">
            <strong>No customers from Sage yet.</strong> The Sage sidecar on the server reads them in and keeps this
            list fresh — press <em>Refresh from Sage</em> to ask it now.
            {sync?.lastError && <div className="sl-banner-error">Last attempt failed: {sync.lastError}</div>}
          </div>
        )}
        {!never && !waiting && sync?.lastError && (
          <div className="sl-banner sl-banner-warn">
            Last refresh failed ({fmtDateTime(sync.lastRunUtc)}): {sync.lastError}. Showing the previous list.
          </div>
        )}

        <DataTable
          sortable
          columns={columns}
          rows={filtered}
          rowKey={(r) => r.id}
          selectedKey={selected?.id ?? null}
          onSelect={(r) => void selectRow(r)}
          emptyText={loading ? "Loading…" : never ? "Nothing synced from Sage yet" : "No customers match"}
        />
      </div>

      {waiting && (
        <SageRefreshOverlay
          what="customers"
          typically="usually a minute or two"
          startedAt={waitStart}
          onCancel={cancelRefresh}
        />
      )}

      <div className="po-side" style={{ width: 330 }}>
        <h3 className="po-side-title">Addresses</h3>
        {selected ? (
          <AddressList rows={addresses} customer={selected} />
        ) : (
          <div className="sl-empty">Select a customer to see its Sage addresses</div>
        )}
      </div>
    </div>
  );
}

function syncNote(sync: SageSyncState | null): string {
  if (!sync?.lastSuccessUtc) return "";
  const by = sync.machine ? ` by ${sync.machine}` : "";
  return `${sync.detailCount.toLocaleString()} addresses · synced ${fmtDateTime(sync.lastSuccessUtc)}${by}`;
}

// The selected account's Sage address book: registered, delivery and contact entries.
function AddressList({ rows, customer }: { rows: SageAddressRow[]; customer: SageCustomerRow }) {
  if (rows.length === 0) return <div className="sl-empty">No addresses in Sage for {customer.accountRef}</div>;
  return (
    <div className="sl-addr-list">
      {rows.map((a) => {
        const kind = (a.type ?? "other").toLowerCase();
        const lines = [a.address1, a.address2, a.address3, a.address4, a.address5].filter(Boolean).join(", ");
        const meta = [a.contact, a.telephone, a.email].filter(Boolean).join(" · ");
        return (
          <div key={a.addressNumber} className="sl-addr">
            <div className="sl-addr-head">
              <span className={`sl-type sl-type-${kind}`}>{a.type ?? "Address"}</span>
              <span className="sl-addr-no">#{a.addressNumber}</span>
              {a.description && <span className="sl-addr-desc">{a.description}</span>}
            </div>
            {a.name && <div className="sl-addr-name">{a.name}</div>}
            {lines && <div className="sl-addr-lines">{lines}</div>}
            {meta && <div className="sl-addr-meta">{meta}</div>}
          </div>
        );
      })}
    </div>
  );
}
