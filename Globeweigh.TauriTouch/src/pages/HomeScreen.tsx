import { ReactNode, useState } from "react";
import { exit } from "@tauri-apps/plugin-process";
import { AdminUserDto, fullName } from "../types";
import { DeviceConfig } from "../api/touch";
import TopBar from "../components/touch/TopBar";
import BigButton from "../components/touch/BigButton";
import SuperAdminGear from "../components/touch/SuperAdminGear";
import { IconBoxes, IconCamera, IconChart, IconFactory, IconScale } from "../components/touch/TouchIcons";
import DbSwitcher from "../components/DbSwitcher";
import SyncDataButton from "../components/SyncDataButton";
import SampleWeightScreen from "./SampleWeightScreen";
import ProductionScreen from "./ProductionScreen";
import ProductionSummaryScreen from "./ProductionSummaryScreen";
import CameraScreen from "./CameraScreen";
import ManualStockScreen from "./ManualStockScreen";
import WeighscaleSetupScreen from "./WeighscaleSetupScreen";
import RelaySetupScreen from "./RelaySetupScreen";
import { describeWeighscale, getSavedWeighscaleConfig } from "../api/serial";
import { describeRelay, getSavedRelayConfig } from "../api/relay";
import { requireSuperAdminPin } from "../components/touch/superAdminPin";
import PrinterSelectDialog from "../components/touch/PrinterSelectDialog";
import { getSavedPrinter } from "../api/printer";
import { rememberOpenModule, takeResumeModule } from "../components/touch/resumeAfterRefresh";

interface Props {
  // Null when the device has Login disabled (straight to Home, nobody signed in).
  user: AdminUserDto | null;
  device: DeviceConfig;
  onSignOut: () => void;
  // Opens the Super Admin device settings (the gear lives on the Home menu, not sub-screens).
  onOpenSettings: () => void;
}

// Neutral Home menu, shown when the device has no Weigh-In/Weigh-Out mode set. Buttons mirror
// the WPF Touch HomeView; the modules behind them are a later phase, so they show "Coming soon".
type Module =
  | "sampleWeight"
  | "production"
  | "productionSummary"
  | "camera"
  | "manualStock"
  | "weighscaleSetup"
  | "relaySetup";

// Which module an update refresh may reopen. The two setup screens are deliberately absent:
// they sit behind the Super Admin pin, and restoring straight into one would walk past it.
function resumeModule(device: DeviceConfig): Module | null {
  const saved = takeResumeModule();
  if (!saved) return null;
  // Honour the device's current buttons too — a module turned off since should not reappear.
  const enabled: Record<string, boolean> = {
    sampleWeight: device.sampleWeight,
    production: device.production,
    productionSummary: device.productionSummary,
    camera: device.camera,
    manualStock: device.manualStock,
  };
  return enabled[saved] ? (saved as Module) : null;
}

export default function HomeScreen({ user, device, onSignOut, onOpenSettings }: Props) {
  // Lazy initialiser: reads (and clears) the resume marker once, on the first render.
  const [active, setActiveState] = useState<Module | null>(() => resumeModule(device));

  // Every navigation records where it went, so a refresh mid-shift can come back to it.
  const setActive = (module: Module | null) => {
    rememberOpenModule(module);
    setActiveState(module);
  };
  const [printerDialog, setPrinterDialog] = useState(false);
  // Bumped when the printer dialog closes so the pill re-reads the saved selection.
  const [printerName, setPrinterName] = useState<string | null>(getSavedPrinter());
  // Re-read on every render: returning from the setup screen re-renders Home, so the
  // pill reflects a connection configured there without any extra state plumbing.
  const scaleConfig = getSavedWeighscaleConfig();
  const relayConfig = getSavedRelayConfig();

  if (active === "sampleWeight") {
    return <SampleWeightScreen onHome={() => setActive(null)} />;
  }
  if (active === "production") {
    return (
      <ProductionScreen
        deviceName={device.deviceName}
        userName={user ? fullName(user) : null}
        onHome={() => setActive(null)}
      />
    );
  }
  if (active === "productionSummary") {
    return <ProductionSummaryScreen onHome={() => setActive(null)} />;
  }
  if (active === "camera") {
    return <CameraScreen onHome={() => setActive(null)} />;
  }
  if (active === "manualStock") {
    return (
      <ManualStockScreen
        userName={user ? fullName(user) : ""}
        deviceName={device.deviceName}
        onHome={() => setActive(null)}
      />
    );
  }
  if (active === "weighscaleSetup") {
    return <WeighscaleSetupScreen onBack={() => setActive(null)} />;
  }
  if (active === "relaySetup") {
    return <RelaySetupScreen onBack={() => setActive(null)} />;
  }

  // Which Home buttons appear is set per-device in the Super Admin screen settings.
  const buttons: { label: string; icon: ReactNode; onClick: () => void; show: boolean }[] = [
    { label: "Sample Weight", icon: <IconScale />, onClick: () => setActive("sampleWeight"), show: device.sampleWeight },
    { label: "Production", icon: <IconFactory />, onClick: () => setActive("production"), show: device.production },
    { label: "Prod. Summary", icon: <IconChart />, onClick: () => setActive("productionSummary"), show: device.productionSummary },
    { label: "Camera", icon: <IconCamera />, onClick: () => setActive("camera"), show: device.camera },
    { label: "Manual Stock", icon: <IconBoxes />, onClick: () => setActive("manualStock"), show: device.manualStock },
  ];
  const visible = buttons.filter((b) => b.show);

  return (
    <div className="tt-app">
      <TopBar
        title="Home"
        userName={user ? fullName(user) : null}
        // Quit the app from the Home menu (same corner action as the Sign In screen).
        onExit={() => exit(0)}
        right={
          user && (
            <button className="tt-btn tt-ghost" style={{ minHeight: 44 }} onClick={onSignOut}>
              Sign out
            </button>
          )
        }
      />
      <div className="tt-body">
        <div className="tt-center">
          {/* The buttons stay vertically centred at all times; the PC name sits above
              them out of the flow (absolute), so showing it never shifts them. */}
          <div className="tt-home-menu">
            {/* Friendly PC name (Screen Settings > PC Name), big above the menu buttons. */}
            {device.displayName && <div className="tt-pc-name">{device.displayName}</div>}
            {/* A single button would auto-fit to the grid's full 900px — cap it to one
                two-across column width (900 minus the 22px gap, halved) so it renders the
                same size as when two buttons show. */}
            {visible.length > 0 ? (
              <div className="tt-bigbtn-grid" style={visible.length === 1 ? { maxWidth: 439 } : undefined}>
                {visible.map((b) => (
                  <BigButton key={b.label} label={b.label} icon={b.icon} onClick={b.onClick} />
                ))}
              </div>
            ) : (
              <div className="tt-note" style={{ fontSize: 18 }}>
                No buttons enabled. Use the settings gear to choose Home buttons.
              </div>
            )}
          </div>
        </div>
      </div>
      {/* Bottom bar: weighscale status on the left (when the device has a scale attached);
          the fixed Super Admin gear occupies the right end. */}
      <div className="tt-bottombar">
        {device.weighscale && (
          <button
            className={`tt-scale-status${scaleConfig ? " tt-configured" : ""}`}
            // Setup is a Super Admin area: same pin gate as the settings gear.
            onClick={async () => {
              if (await requireSuperAdminPin()) setActive("weighscaleSetup");
            }}
          >
            <span className="tt-dot" />
            Weighscale
            <span className="tt-port">{scaleConfig ? describeWeighscale(scaleConfig) : "Not set up"}</span>
          </button>
        )}
        {device.relayCard && (
          <button
            className={`tt-scale-status${relayConfig ? " tt-configured" : ""}`}
            // Setup is a Super Admin area: same pin gate as the weighscale.
            onClick={async () => {
              if (await requireSuperAdminPin()) setActive("relaySetup");
            }}
          >
            <span className="tt-dot" />
            Alarm Relay
            <span className="tt-port">{relayConfig ? describeRelay(relayConfig) : "Not set up"}</span>
          </button>
        )}
        {device.printer && (
          <button
            className={`tt-scale-status${printerName ? " tt-configured" : ""}`}
            // No pin gate — printer setup opens directly.
            onClick={() => setPrinterDialog(true)}
          >
            <span className="tt-dot" />
            Printer
            <span className="tt-port">{printerName ?? "Not set up"}</span>
          </button>
        )}
      </div>
      {printerDialog && (
        <PrinterSelectDialog
          onClose={() => {
            setPrinterDialog(false);
            setPrinterName(getSavedPrinter());
          }}
        />
      )}
      {/* Build number + settings gear live on the Home MENU only (sub-screens return
          early above), so module screens keep their bottom edge for row actions. */}
      <span className="tt-build-ver">{__BUILD_VERSION__}</span>
      <SuperAdminGear onOpen={onOpenSettings} />
      {/* Dev-only DB switcher + Sync Data, Home menu only (sub-screens return early above). */}
      {import.meta.env.DEV && (
        <div className="tt-devbar">
          <SyncDataButton />
          <DbSwitcher />
        </div>
      )}
    </div>
  );
}
