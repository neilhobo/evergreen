import { ReactNode, useCallback, useEffect, useState } from "react";
import LoginScreen from "./pages/LoginScreen";
import HomeScreen from "./pages/HomeScreen";
import WeighInScreen from "./pages/WeighInScreen";
import WeighOutScreen from "./pages/WeighOutScreen";
import DeviceConfigScreen from "./pages/DeviceConfigScreen";
import TopBar from "./components/touch/TopBar";
import SuperAdminGear from "./components/touch/SuperAdminGear";
import UpdatePill from "./components/UpdatePill";
import UiRefreshPill from "./components/UiRefreshPill";
import ProductionAlarmAgent from "./components/ProductionAlarmAgent";
import { AdminUserDto } from "./types";
import { clearSession, restoreSession } from "./api/auth";
import { setUnauthorizedHandler } from "./api/client";
import { DeviceConfig, getDeviceByName, getDeviceName } from "./api/touch";
import { initDeviceStore } from "./api/deviceStore";
import "./App.css";
import "./styles/TouchScreen.css";

export default function App() {
  const [user, setUser] = useState<AdminUserDto | null>(() => restoreSession());
  const [device, setDevice] = useState<DeviceConfig | null>(null);
  const [deviceError, setDeviceError] = useState<string | null>(null);
  const [loadingDevice, setLoadingDevice] = useState(true);
  const [showSettings, setShowSettings] = useState(false);

  // Expired JWT (Factory mode) → drop the user, fall back to the login screen.
  useEffect(() => {
    setUnauthorizedHandler(() => {
      clearSession();
      setUser(null);
    });
    return () => setUnauthorizedHandler(null);
  }, []);

  // Resolve this screen's device row (and its mode) FIRST — anonymously, before any login,
  // because Weigh-In/Weigh-Out screens never log in.
  const loadDevice = useCallback(async () => {
    setLoadingDevice(true);
    setDeviceError(null);
    try {
      const name = await getDeviceName();
      const resolved = await getDeviceByName(name);
      // Seed the per-PC hardware config cache (weighscale / relay / printer) BEFORE the
      // device renders — screens read it synchronously during render.
      initDeviceStore(resolved);
      setDevice(resolved);
    } catch (err) {
      setDeviceError((err as Error).message || "Could not reach the server.");
    } finally {
      setLoadingDevice(false);
    }
  }, []);

  useEffect(() => {
    void loadDevice();
  }, [loadDevice]);

  function signOut() {
    clearSession();
    setUser(null);
  }

  // Device not resolved yet (loading or unreachable).
  if (!device) {
    return (
      <div className="tt-app">
        <TopBar />
        <div className="tt-body">
          <div className="tt-center">
            {loadingDevice && <div className="tt-spinner-lg" />}
            {loadingDevice && <div className="tt-note">Identifying this screen…</div>}
            {deviceError && (
              <>
                <div className="tt-error">{deviceError}</div>
                <button className="tt-btn tt-primary" onClick={() => void loadDevice()}>Retry</button>
              </>
            )}
          </div>
        </div>
        <UpdatePill />
        <UiRefreshPill />
      </div>
    );
  }

  // Super Admin settings replace the whole screen (full page, not a popup).
  if (showSettings) {
    return (
      <DeviceConfigScreen
        device={device}
        onClose={(updated) => {
          setShowSettings(false);
          if (updated) {
            initDeviceStore(updated); // settings save returns the fresh row — reseed
            setDevice(updated);
          }
        }}
      />
    );
  }

  // Route on the device mode. Weigh-In/Out are kiosk screens (no login); Factory logs in
  // then shows Home — unless the device has Login disabled, which goes straight to Home;
  // an unconfigured screen prompts to use the settings gear.
  let screen: ReactNode;
  if (device.isWeighIn) {
    screen = <WeighInScreen device={device} onOpenSettings={() => setShowSettings(true)} />;
  } else if (device.isWeighOut) {
    screen = <WeighOutScreen onOpenSettings={() => setShowSettings(true)} />;
  } else if (device.factory) {
    screen = user || !device.loginEnabled
      ? <HomeScreen user={user} device={device} onSignOut={signOut} onOpenSettings={() => setShowSettings(true)} />
      : <LoginScreen onLoggedIn={setUser} />;
  } else {
    screen = (
      <div className="tt-app">
        <TopBar />
        <div className="tt-body">
          <div className="tt-center">
            <div className="tt-note" style={{ fontSize: 18 }}>
              This screen isn't configured yet.
            </div>
            <div className="tt-note">Use the settings gear (bottom-right) to set its mode.</div>
          </div>
        </div>
        {/* The gear is the only way off an unconfigured screen, so it always renders here. */}
        <span className="tt-build-ver">{__BUILD_VERSION__}</span>
        <SuperAdminGear onOpen={() => setShowSettings(true)} />
      </div>
    );
  }

  return (
    <>
      {screen}
      {/* Build number + settings gear are each screen's own business: every mode renders
          them on its home step only (HomeScreen, WeighInScreen, WeighOutScreen), so sub-
          screens keep their bottom edge for row actions and Next. */}
      {/* Missed-scan alarm: popup on flagged PCs, siren relay on the card PC — app-wide,
          whatever screen is showing. Kiosk PCs (flags off) render nothing. */}
      <ProductionAlarmAgent device={device} />
      <UpdatePill />
      <UiRefreshPill />
    </>
  );
}
