import { useEffect, useState } from "react";
import { getVersion } from "@tauri-apps/api/app";
import { AdminUserDto, UserRole, hasRole, isSuperAdmin as userIsSuperAdmin } from "../types";
import { rememberOpenPage, takeResumePage } from "../components/resumeAfterRefresh";
import { SessionContext } from "../session";
import { FeaturesProvider, useFeatures } from "../features";
import { FEATURE } from "../api/features";
import { getOilTanksAccessMe } from "../api/oilTanks";
import { isLocalDb } from "../config";
import ComingSoon from "../components/ComingSoon";
import UpdatePill from "../components/UpdatePill";
import UiRefreshPill from "../components/UiRefreshPill";
import DbSwitcher from "../components/DbSwitcher";
import IncomingCallOverlay from "../components/intercom/IncomingCallOverlay";
import SyncDataButton from "../components/SyncDataButton";
import WeighbridgeRecordsPage from "./weighbridge/WeighbridgeRecordsPage";
import DispatchPage from "./dispatch/DispatchPage";
import StockPage from "./stock/StockPage";
import CostingPage from "./costing/CostingPage";
import ProductionPage from "./production/ProductionPage";
import CustomersSuppliersPage from "./customers-suppliers/CustomersSuppliersPage";
import ProductsPage from "./products/ProductsPage";
import HauliersPage from "./hauliers/HauliersPage";
import ReferenceDataPage from "./reference-data/ReferenceDataPage";
import GateBarrierPage from "./gate-barrier/GateBarrierPage";
import IntercomPage from "./intercom/IntercomPage";
import DevicesPage from "./devices/DevicesPage";
import SystemDevicesPage from "./system-devices/SystemDevicesPage";
import SuperAdminPage from "./super-admin/SuperAdminPage";
import MyOrdersPage from "./sales/MyOrdersPage";
import SageLinkPage from "./sage-link/SageLinkPage";
import ReportsPage from "./reports/ReportsPage";
import OilTanksPage from "./oil-tanks/OilTanksPage";
import SwitchUserBanner from "../components/SwitchUserBanner";

interface Props {
  user: AdminUserDto;
  onSignOut: () => void;
  onUserUpdated: (user: AdminUserDto) => void;
  onSwitchUser: (user: AdminUserDto) => void;
}

interface NavItem {
  key: string;
  label: string;
  icon: string;
  dev?: boolean; // dev-build only, rendered in the lavender/dotted restricted style
  superAdmin?: boolean; // named super-admin only, same lavender/dotted style
  sage?: boolean; // the Sage Link item: sage green, so the accounts link reads apart from the day-to-day pages
}

// Same items and order as the WPF MainWindow.xaml / MainWindowViewModel nav.
const MAIN_NAV: NavItem[] = [
  { key: "weighbridge", label: "Deliveries/Incoming", icon: "weighbridge-96.png" },
  { key: "dispatch", label: "Orders / Dispatch", icon: "Dispatch-96.png" },
  { key: "stock", label: "Stock", icon: "Stock-96.png" },
  { key: "costing", label: "Costing", icon: "Money-96.png" },
  { key: "production", label: "Production", icon: "Production-96.png" },
  { key: "customers-suppliers", label: "Customers/Suppliers", icon: "Customers-96.png" },
  { key: "products", label: "Products", icon: "Product-96.png" },
  { key: "hauliers", label: "Hauliers", icon: "Intake-96.png" },
  // The Sage 50 integration (pages/sage-link). Gated with Admin below: an office tool.
  { key: "sage-link", label: "Sage Link", icon: "sage-96.svg", sage: true },
  // Reports (pages/reports) — built on the Sage sales mirror; gated with Admin, and staged.
  { key: "reports", label: "Reports", icon: "Reports-96.png" },
  { key: "admin", label: "Admin", icon: "admin-96.png" },
  { key: "gate-barrier", label: "Gate/Barrier", icon: "gate-96.png" },
  // Oil Tanks (pages/oil-tanks) — the site's Specgauge oil-tank sensors.
  { key: "oil-tanks", label: "Oil Tanks", icon: "oil-tank-96.svg" },
  // Weighbridge door intercoms - connection, ring log and cameras. Shown to everyone FOR NOW
  // (2026-10-20, user's call); it was super-admin only and should go back to `superAdmin: true`.
  // Calls themselves come through the overlay on every page either way.
  { key: "intercom", label: "Intercom", icon: "Van-96.png" },
];

// "Globeweigh Admin" section — Globeweigh-internal tools restricted to this single named
// user (not the GlobeweighAdmin role). Device Manager (mobile-app deployment) and Super
// Admin are shown to the super-admin on the deployed server too; Device Manager and the
// client-facing Devices overview are additionally shown in any local dev build so they can
// be worked on from this PC. The username itself lives in types.ts, shared with the
// super-admin-only grid columns.
// The Sales role sees this one page and nothing else: their own orders, sent to the office
// for approval (see pages/sales/MyOrdersPage.tsx).
const SALES_NAV: NavItem = { key: "my-orders", label: "My Orders", icon: "Purchase-Order-96.png" };

const DEVICES_NAV: NavItem = { key: "devices", label: "Device Manager", icon: "Computer-96.png" };
const ADMIN_NAV: NavItem[] = [
  // Client-facing device overview — still being built out, so dev-only for now.
  { key: "system-devices", label: "Devices", icon: "Computer-96.png", dev: true },
  { key: "super-admin", label: "Super Admin", icon: "admin-96.png", superAdmin: true },
];

// The nav this user actually gets. One source of truth: the sidebar renders it, and the
// after-refresh resume looks the saved page up in it, so a page hidden from this user (or
// a dev-only one on a deployed build) can never be restored into.
// Which staged items are switched on (Super Admin > Staging), and whether this user is one of
// those picked to see Oil Tanks (Super Admin > Oil Tanks) — both answers the server gives
// after sign-in, so both are handled the same way.
interface Staged {
  sageLinkOn: boolean;
  reportsOn: boolean;
  oilTanksOn: boolean;
}
const ALL_STAGED_ON: Staged = { sageLinkOn: true, reportsOn: true, oilTanksOn: true };

function navFor(user: AdminUserDto, staged: Staged): { main: NavItem[]; admin: NavItem[] } {
  if (hasRole(user, UserRole.Sales)) return { main: [SALES_NAV], admin: [] };
  // The Admin (data setup) item is gated by DataSetupVisible (OfficeAdmin/GlobeweighAdmin),
  // as in MainWindow.xaml. The "Globeweigh Admin" section is restricted to a single named
  // user, not the role. Costing is always shown (no gating in the WPF nav).
  const dataSetupVisible = hasRole(user, UserRole.GlobeweighAdmin, UserRole.OfficeAdmin);
  const superAdmin = userIsSuperAdmin(user);
  // The build/user gates the items carry: dev-only ones only in a local dev build,
  // super-admin-only ones only for the named user.
  const visible = (item: NavItem) =>
    (!item.dev || import.meta.env.DEV) && (!item.superAdmin || superAdmin);
  return {
    // My Orders is shown to every account while the sales flow is being tested (2026-10-05);
    // once it is signed off it belongs to the Sales role alone.
    main: [
      ...MAIN_NAV.filter(
        (item) =>
          visible(item) &&
          ((item.key !== "admin" && item.key !== "sage-link" && item.key !== "reports") || dataSetupVisible) &&
          // Staged (Super Admin > Staging) until the client is ready for them.
          (item.key !== "sage-link" || staged.sageLinkOn) &&
          (item.key !== "reports" || staged.reportsOn) &&
          // Per user, chosen in Super Admin > Oil Tanks.
          (item.key !== "oil-tanks" || staged.oilTanksOn),
      ),
      SALES_NAV,
    ],
    // Device Manager is the one either/or case: the super-admin (on the deployed server
    // too, not just dev), or any local dev build.
    admin: [
      ...(superAdmin || import.meta.env.DEV ? [DEVICES_NAV] : []),
      ...ADMIN_NAV.filter(visible),
    ] as NavItem[],
  };
}

// The page to reopen after an update refresh, or null for an ordinary start.
function resumeSelection(user: AdminUserDto, staged: Staged): NavItem | null {
  const key = takeResumePage();
  if (!key) return null;
  const { main, admin } = navFor(user, staged);
  return [...main, ...admin].find((item) => item.key === key) ?? null;
}

// The providers every screen reads from — the session, and which staged features are on — with
// the window itself as a child component, because the nav has to ask about staged features and
// a component cannot read a context it is the one providing.
export default function MainWindow({ user, onSignOut, onUserUpdated, onSwitchUser }: Props) {
  return (
    <SessionContext.Provider
      value={{ user, signOut: onSignOut, updateUser: onUserUpdated, switchUser: onSwitchUser }}
    >
      <FeaturesProvider>
        <MainWindowBody user={user} />
      </FeaturesProvider>
    </SessionContext.Provider>
  );
}

function MainWindowBody({ user }: { user: AdminUserDto }) {
  // Sage Link is staged: in the nav only once it is switched on in Super Admin > Staging. Off
  // until the flags arrive, so it appears a moment after sign-in rather than flashing away.
  const { isOn, loaded: flagsLoaded } = useFeatures();
  // Oil Tanks is per user: asked once, off until the answer comes (and off if it never does).
  const [oilTanksOn, setOilTanksOn] = useState(false);
  const [oilTanksLoaded, setOilTanksLoaded] = useState(false);
  useEffect(() => {
    getOilTanksAccessMe()
      .then((r) => setOilTanksOn(r.canView))
      .catch(() => setOilTanksOn(false))
      .finally(() => setOilTanksLoaded(true));
  }, []);
  const featuresLoaded = flagsLoaded && oilTanksLoaded;
  const staged: Staged = {
    sageLinkOn: isOn(FEATURE.sageLink),
    reportsOn: isOn(FEATURE.reports),
    oilTanksOn,
  };
  // Lazy initialiser: reads (and clears) the resume marker once, on the first render. The
  // resume looks the page up as if every staged item were on — the flags have not arrived
  // yet, and an update refresh on a staged page must come back to it; the effect below puts
  // it right if the flags then say otherwise.
  const [selected, setSelectedState] = useState<NavItem>(
    () => resumeSelection(user, ALL_STAGED_ON) ?? navFor(user, staged).main[0],
  );

  // Every navigation records where it went, so an update refresh can come back to it.
  const setSelected = (item: NavItem) => {
    rememberOpenPage(item.key);
    setSelectedState(item);
  };
  const [sidebarOpen, setSidebarOpen] = useState(true);
  const [version, setVersion] = useState("");
  // A shell update is showing → hide the server-published UI update card.
  const [shellUpdate, setShellUpdate] = useState(false);

  useEffect(() => {
    getVersion()
      .then((v) => setVersion(`v${v}`))
      .catch(() => setVersion(""));
  }, []);

  const localDb = isLocalDb();
  const { main: mainNav, admin: adminNav } = navFor(user, staged);

  // Once the flags are known: if the open page is not in the nav (a staged page resumed
  // optimistically, or its switch turned off while it was up), fall back to the first page
  // rather than keep showing what the menu does not offer.
  useEffect(() => {
    if (!featuresLoaded) return;
    if (![...mainNav, ...adminNav].some((item) => item.key === selected.key)) setSelected(mainNav[0]);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [staged.sageLinkOn, staged.reportsOn, staged.oilTanksOn, featuresLoaded]);

  function renderNavItem(item: NavItem) {
    return (
      <li
        key={item.key}
        className={`nav-item${selected.key === item.key ? " active" : ""}${
          item.dev || item.superAdmin ? " nav-item-dev" : ""
        }${item.sage ? " nav-item-sage" : ""}`}
        onClick={() => setSelected(item)}
        title={sidebarOpen ? undefined : item.label}
      >
        <img src={`images/${item.icon}`} alt="" />
        {sidebarOpen && <span>{item.label}</span>}
      </li>
    );
  }

  // Every page is one content card (styles/ContentCard.module.css) with its own PageHeader —
  // title, meta, tabs, and the profile chip with My Account / Log out — so the shell draws
  // no top bar of its own.
  function renderPage() {
    switch (selected.key) {
      case "weighbridge":
        return <WeighbridgeRecordsPage user={user} />;
      case "dispatch":
        return <DispatchPage user={user} />;
      case "stock":
        return <StockPage user={user} />;
      case "costing":
        return <CostingPage user={user} />;
      case "production":
        return <ProductionPage user={user} />;
      case "oil-tanks":
        return <OilTanksPage user={user} />;
      case "customers-suppliers":
        return <CustomersSuppliersPage user={user} />;
      case "products":
        return <ProductsPage user={user} />;
      case "hauliers":
        return <HauliersPage user={user} />;
      case "sage-link":
        return <SageLinkPage user={user} />;
      case "reports":
        return <ReportsPage user={user} />;
      case "admin":
        return <ReferenceDataPage user={user} />;
      case "gate-barrier":
        return <GateBarrierPage user={user} />;
      case "intercom":
        return <IntercomPage />;
      case "system-devices":
        return <SystemDevicesPage user={user} />;
      case "super-admin":
        return <SuperAdminPage user={user} />;
      case "my-orders":
        return <MyOrdersPage user={user} />;
      case "devices":
        return <DevicesPage user={user} />;
      default:
        return <ComingSoon section={selected.label} />;
    }
  }

  return (
    <>
      <div className="main-window">
        <aside className={`sidebar${sidebarOpen ? " open" : ""}`}>
          <div className="sidebar-header">
            <img className="sidebar-logo" src="images/Globe-96.png" alt="" />
            {sidebarOpen && (
              <span className="sidebar-wordmark">
                <span className="word-globe">Globe</span>
                <span className="word-weigh">weigh</span>
              </span>
            )}
            <button
              className="sidebar-toggle"
              onClick={() => setSidebarOpen((open) => !open)}
              title={sidebarOpen ? "Collapse" : "Expand"}
            >
              ☰
            </button>
          </div>

          <div className="nav-main">
            <ul className="nav-list">{mainNav.map(renderNavItem)}</ul>

            {adminNav.length > 0 && (
              <div className="sidebar-super-admin">
                {sidebarOpen && <h2>Globeweigh Admin</h2>}
                <ul className="nav-list">{adminNav.map(renderNavItem)}</ul>
              </div>
            )}

            {localDb && (
              <div className="sidebar-local-db" title="Connected to a local database (dev PC)">
                <span className="local-db-dot" />
                {sidebarOpen && <span>Local DB</span>}
              </div>
            )}
          </div>

          {/* Both update cards live in the same slot above the version number. A
              shell update includes the newer UI, so it supersedes the UI-refresh
              card — only ever one of the two is on screen. */}
          <UpdatePill sidebarOpen={sidebarOpen} onActiveChange={setShellUpdate} />
          {!shellUpdate && <UiRefreshPill sidebarOpen={sidebarOpen} />}
          {import.meta.env.DEV && <SyncDataButton sidebarOpen={sidebarOpen} />}
          {import.meta.env.DEV && <DbSwitcher sidebarOpen={sidebarOpen} />}
          <div className="sidebar-version">
            {sidebarOpen ? (
              <>
                <span className="sidebar-build">Build {__BUILD_VERSION__}</span>
                {version && <span className="sidebar-shell">Shell {version}</span>}
              </>
            ) : (
              __BUILD_VERSION__.slice(0, 10)
            )}
          </div>
        </aside>

        <div className="main-content">
          <SwitchUserBanner />
          <main className="content-area">{renderPage()}</main>
        </div>
      </div>
      {/* Weighbridge intercom: the incoming-call card and call dialog, over every page. */}
      <IncomingCallOverlay />
    </>
  );
}
