import { useState } from "react";
import TopBar from "../components/touch/TopBar";
import { keyboard } from "../components/touch/Keyboard";
import { messageDialog } from "../components/confirmDialog";
import { DeviceConfig, setDeviceConfig } from "../api/touch";

interface Props {
  device: DeviceConfig;
  onClose: (saved: DeviceConfig | null) => void;
}

type Mode = "isWeighIn" | "isWeighOut" | "factory";
type Button = "sampleWeight" | "production" | "productionSummary" | "camera" | "manualStock";

const MODES: { key: Mode; label: string; desc: string }[] = [
  { key: "isWeighIn", label: "Weigh In", desc: "Vehicles weighing in (Delivery / Collection). No login." },
  { key: "isWeighOut", label: "Weigh Out", desc: "Vehicles weighing out from the pending list. No login." },
  { key: "factory", label: "Factory", desc: "The Home menu, with optional login." },
];

const BUTTONS: { key: Button; label: string }[] = [
  { key: "sampleWeight", label: "Sample Weight" },
  { key: "production", label: "Production" },
  { key: "productionSummary", label: "Prod. Summary" },
  { key: "camera", label: "Camera" },
  { key: "manualStock", label: "Manual Stock" },
];

// Super Admin: set what this screen is used for (full page, not a popup). The three modes
// are mutually exclusive; turning one on clears the others. The right-hand panel shows the
// options for the chosen mode: Factory the Home menu button toggles and hardware, Weigh In
// how the Delivery lists are laid out.
export default function DeviceConfigScreen({ device, onClose }: Props) {
  const [mode, setMode] = useState<Mode | null>(
    device.isWeighIn ? "isWeighIn" : device.isWeighOut ? "isWeighOut" : device.factory ? "factory" : null,
  );
  const [buttons, setButtons] = useState<Record<Button, boolean>>({
    sampleWeight: device.sampleWeight,
    production: device.production,
    productionSummary: device.productionSummary,
    camera: device.camera,
    manualStock: device.manualStock,
  });
  const [weighscale, setWeighscale] = useState(device.weighscale);
  const [printer, setPrinter] = useState(device.printer);
  const [relayCard, setRelayCard] = useState(device.relayCard);
  const [productionAlarm, setProductionAlarm] = useState(device.productionAlarm);
  const [haulierOwnScreen, setHaulierOwnScreen] = useState(device.haulierOwnScreen);
  const [loginEnabled, setLoginEnabled] = useState(device.loginEnabled);
  const [displayName, setDisplayName] = useState(device.displayName ?? "");
  const [busy, setBusy] = useState(false);

  function chooseMode(key: Mode, on: boolean) {
    setMode(on ? key : null);
  }

  // On-screen keyboard — touch screens have no physical one.
  async function editPcName() {
    const v = await keyboard({ title: "PC Name", initial: displayName });
    if (v !== null) setDisplayName(v.trim());
  }

  async function onSave() {
    setBusy(true);
    try {
      const updated = await setDeviceConfig(device.id, {
        isWeighIn: mode === "isWeighIn",
        isWeighOut: mode === "isWeighOut",
        factory: mode === "factory",
        // Button toggles only apply in Factory mode.
        sampleWeight: mode === "factory" && buttons.sampleWeight,
        production: mode === "factory" && buttons.production,
        productionSummary: mode === "factory" && buttons.productionSummary,
        camera: mode === "factory" && buttons.camera,
        manualStock: mode === "factory" && buttons.manualStock,
        weighscale: mode === "factory" && weighscale,
        printer: mode === "factory" && printer,
        relayCard: mode === "factory" && relayCard,
        productionAlarm: mode === "factory" && productionAlarm,
        // Weigh-In-only option, gated the way the Factory ones are.
        haulierOwnScreen: mode === "isWeighIn" && haulierOwnScreen,
        // Not mode-gated: keeps its value so switching modes doesn't silently turn login off.
        loginEnabled,
        displayName: displayName.trim() || null,
      });
      onClose(updated);
    } catch (err) {
      void messageDialog("Failed to save device config.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    // tt-device-config scopes compact toggle/panel sizes so everything fits unscrolled.
    <div className="tt-app tt-device-config">
      <TopBar
        title="Screen Settings"
        onBack={() => onClose(null)}
        right={
          <span className="tt-note" style={{ fontSize: 15 }}>
            Device: <strong style={{ color: "#fff" }}>{device.deviceName}</strong>
          </span>
        }
      />
      <div className="tt-body">
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 26, height: "100%" }}>
          {/* PC name + screen mode */}
          <div className="tt-panel" style={{ overflow: "auto" }}>
            <div className="tt-col-title">PC Name</div>
            <div className="tt-list-item" onClick={() => void editPcName()}>
              <div style={{ fontSize: 13, color: "#94a3b8" }}>
                Shown in big writing above the Home menu buttons
              </div>
              <div style={{ fontWeight: 600, minHeight: 24 }}>{displayName || "Tap to enter"}</div>
            </div>

            <div className="tt-col-title" style={{ marginTop: 10 }}>Screen Mode</div>
            {MODES.map((m) => (
              <div className="tt-toggle-row" key={m.key}>
                <div>
                  <div>{m.label}</div>
                  <div style={{ fontSize: 13, color: "#94a3b8" }}>{m.desc}</div>
                </div>
                <button
                  className={`tt-toggle${mode === m.key ? " tt-on" : ""}`}
                  onClick={() => chooseMode(m.key, mode !== m.key)}
                  aria-label={m.label}
                >
                  <span className="tt-knob" />
                </button>
              </div>
            ))}
          </div>

          {/* Options for the selected mode */}
          <div className="tt-panel" style={{ overflow: "auto" }}>
            {mode === "factory" ? (
              <>
                <div className="tt-col-title">Login</div>
                <div className="tt-toggle-row">
                  <div>Login enabled</div>
                  <button
                    className={`tt-toggle${loginEnabled ? " tt-on" : ""}`}
                    onClick={() => setLoginEnabled((v) => !v)}
                    aria-label="Login enabled"
                  >
                    <span className="tt-knob" />
                  </button>
                </div>

                <div className="tt-col-title" style={{ marginTop: 10 }}>Home page buttons</div>
                {BUTTONS.map((b) => (
                  <div className="tt-toggle-row" key={b.key}>
                    <div>{b.label}</div>
                    <button
                      className={`tt-toggle${buttons[b.key] ? " tt-on" : ""}`}
                      onClick={() => setButtons((f) => ({ ...f, [b.key]: !f[b.key] }))}
                      aria-label={b.label}
                    >
                      <span className="tt-knob" />
                    </button>
                  </div>
                ))}

                <div className="tt-col-title" style={{ marginTop: 10 }}>Hardware</div>
                <div className="tt-toggle-row">
                  <div>Weighscale</div>
                  <button
                    className={`tt-toggle${weighscale ? " tt-on" : ""}`}
                    onClick={() => setWeighscale((v) => !v)}
                    aria-label="Weighscale"
                  >
                    <span className="tt-knob" />
                  </button>
                </div>
                <div className="tt-toggle-row">
                  <div>Printer</div>
                  <button
                    className={`tt-toggle${printer ? " tt-on" : ""}`}
                    onClick={() => setPrinter((v) => !v)}
                    aria-label="Printer"
                  >
                    <span className="tt-knob" />
                  </button>
                </div>
                <div className="tt-toggle-row">
                  {/* DB column is still Device.RelayCard; "Alarm Relay" distinguishes it from
                      other relay cards we may add later. */}
                  <div>
                    <div>Alarm Relay</div>
                    <div style={{ fontSize: 13, color: "#94a3b8" }}>
                      The relay card the missed-scan alarm siren is wired to.
                    </div>
                  </div>
                  <button
                    className={`tt-toggle${relayCard ? " tt-on" : ""}`}
                    onClick={() => setRelayCard((v) => !v)}
                    aria-label="Alarm Relay"
                  >
                    <span className="tt-knob" />
                  </button>
                </div>

                <div className="tt-col-title" style={{ marginTop: 10 }}>Alerts</div>
                <div className="tt-toggle-row">
                  <div>
                    <div>Production Alarm</div>
                    <div style={{ fontSize: 13, color: "#94a3b8" }}>
                      Show the missed-scan alarm popup on this PC (with a Silence button).
                    </div>
                  </div>
                  <button
                    className={`tt-toggle${productionAlarm ? " tt-on" : ""}`}
                    onClick={() => setProductionAlarm((v) => !v)}
                    aria-label="Production Alarm"
                  >
                    <span className="tt-knob" />
                  </button>
                </div>
              </>
            ) : mode === "isWeighIn" ? (
              <>
                <div className="tt-col-title">Delivery</div>
                <div className="tt-toggle-row">
                  <div>
                    <div>Show Hauliers on one screen</div>
                    <div style={{ fontSize: 13, color: "#94a3b8" }}>
                      Pick the haulier on a screen of its own, then Product / Supplier / Bog
                      together. Wider lists, easier to read than four columns at once.
                    </div>
                  </div>
                  <button
                    className={`tt-toggle${haulierOwnScreen ? " tt-on" : ""}`}
                    onClick={() => setHaulierOwnScreen((v) => !v)}
                    aria-label="Show Hauliers on one screen"
                  >
                    <span className="tt-knob" />
                  </button>
                </div>
              </>
            ) : (
              <div className="tt-center" style={{ height: "100%", gap: 16 }}>
                <div className="tt-note" style={{ fontSize: 18 }}>
                  Switch to Factory or Weigh In mode to configure this screen.
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
      {/* Bottom action bar: big touch-friendly Cancel / Save. */}
      <div className="tt-bottombar" style={{ justifyContent: "flex-end", gap: 14 }}>
        <button className="tt-btn tt-ghost" style={{ minHeight: 52, padding: "0 34px" }} onClick={() => onClose(null)}>
          Cancel
        </button>
        <button className="tt-btn tt-primary" style={{ minHeight: 52, padding: "0 44px" }} disabled={busy} onClick={onSave}>
          {busy ? "Savingâ€¦" : "Save"}
        </button>
      </div>
    </div>
  );
}
