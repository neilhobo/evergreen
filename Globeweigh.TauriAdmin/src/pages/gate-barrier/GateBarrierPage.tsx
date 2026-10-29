import { useEffect, useRef, useState } from "react";
import {
  getConfig,
  getState,
  getLiveWeight,
  getLiveWeight2,
  setGate,
  setBarrier,
  type Camera,
  type GateBarrierConfig,
  type RelayState,
  type RemoteScreen,
  type WeightReading,
} from "../../api/gateBarrier";
import CameraStream from "./CameraStream";
import { getVideoBaseUrl, resolveVncWsUrl } from "../../config";
import { AdminUserDto } from "../../types";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import "./GateBarrier.css";
import "../weighbridge/Weighbridge.css";
import LiveWeightBody from "../weighbridge/LiveWeightPanel";
import RemoteScreenModal from "../weighbridge/RemoteScreenModal";
import { messageDialog } from "../../components/confirmDialog";
import { STYLE } from "../../api/features";
import { useFeature } from "../../features";

const STATE_POLL_MS = 1500;
const WEIGHT_POLL_MS = 1000;
// A reading older than this isn't live - the bridge has gone quiet.
const WEIGHT_STALE_MS = 5000;

interface BridgeWeight {
  value: string;      // last known reading, or "—" if we've never had one
  stale: boolean;
  lostFor: string | null;  // "4m", "45s" - null when live or when we can't tell
}

const LIVE_UNKNOWN: BridgeWeight = { value: "—", stale: true, lostFor: null };

function formatOutage(ms: number): string {
  const secs = Math.floor(ms / 1000);
  if (secs < 60) return `${secs}s`;
  const mins = Math.floor(secs / 60);
  if (mins < 60) return `${mins}m`;
  return `${Math.floor(mins / 60)}h ${mins % 60}m`;
}

// Fold a service reading into what the panel shows. A stale bridge keeps its last
// figure greyed out with "no data for ..." underneath, rather than a bare dash that
// gives no hint how long it's been down.
function toBridgeWeight(r: WeightReading | null, prev: BridgeWeight): BridgeWeight {
  if (!r) return { ...prev, stale: true, lostFor: null };

  const live = r.isConnected && r.ageMs >= 0 && r.ageMs < WEIGHT_STALE_MS;
  if (live) return { value: r.weight || "0", stale: false, lostFor: null };

  // Prefer the service's own outage clock; fall back to reading age.
  const lostMs = r.dataLostMs !== undefined && r.dataLostMs >= 0 ? r.dataLostMs : r.ageMs;
  return {
    value: r.weight || prev.value,
    stale: true,
    lostFor: lostMs >= 0 ? formatOutage(lostMs) : null,
  };
}

// Gate / Barrier (Web) — ported from Globeweigh.WebAdmin. Replaces the LibVLC
// desktop view: relay control + live weight + go2rtc camera, all via the API.
export default function GateBarrierPage(_props: { user: AdminUserDto }) {
  const [config, setConfig] = useState<GateBarrierConfig | null>(null);
  const [state, setState] = useState<RelayState>({ connected: false, gateOpen: false, barrierOpen: false });
  // False until the first relay poll answers (either way): until then the IO-card fault box is
  // not shown, only a soft "Connecting" panel, so a page load never flashes the fault.
  const [relayChecked, setRelayChecked] = useState(false);
  const [weight, setWeight] = useState<BridgeWeight>(LIVE_UNKNOWN);
  const [weight2, setWeight2] = useState<BridgeWeight>(LIVE_UNKNOWN);
  const [selectedCamera, setSelectedCamera] = useState<Camera | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Avoid overlapping/looping polls when a request is slow (relay timeouts).
  const pollingRef = useRef(false);

  // Super Admin > Scale/Relay Styles: the "Dark dock" skin, shared with Deliveries/Incoming.
  const dock = useFeature(STYLE.scaleRelayDarkDock);

  useEffect(() => {
    getConfig()
      .then((c) => {
        setConfig(c);
        if (c.cameras.length > 0) setSelectedCamera(c.cameras[0]);
      })
      .catch((e) => setError(e.message));
  }, []);

  // Poll relay state.
  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      if (pollingRef.current) return;
      pollingRef.current = true;
      try {
        const s = await getState();
        if (!cancelled) setState(s);
      } catch {
        if (!cancelled) setState((p) => ({ ...p, connected: false }));
      } finally {
        if (!cancelled) setRelayChecked(true);
        pollingRef.current = false;
      }
    };
    tick();
    const id = setInterval(tick, STATE_POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);

  // Poll live weight from GlobeweighService.
  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      const [r, r2] = await Promise.all([getLiveWeight(), getLiveWeight2()]);
      if (cancelled) return;
      setWeight((prev) => toBridgeWeight(r, prev));
      setWeight2((prev) => toBridgeWeight(r2, prev));
    };
    tick();
    const id = setInterval(tick, WEIGHT_POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);

  const toggle = async (which: "gate" | "barrier") => {
    setBusy(true);
    setError(null);
    try {
      const open = which === "gate" ? !state.gateOpen : !state.barrierOpen;
      const s = which === "gate" ? await setGate(open) : await setBarrier(open);
      setState(s);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Command failed");
    } finally {
      setBusy(false);
    }
  };


  // ---- Remote Connect (embedded noVNC to the TOUCH-IN / TOUCH-OUT touchscreens) ----
  // Same lookup as Deliveries/Incoming: the configured screen by name, else a localStorage
  // override of its ws URL so it works before the API config is populated.
  const [openScreen, setOpenScreen] = useState<RemoteScreen | null>(null);
  function openRemote(name: string) {
    const found = (config?.remoteScreens ?? []).find((s) => s.name.toLowerCase() === name.toLowerCase());
    let override = "";
    try {
      override = localStorage.getItem(`vncWs:${name}`) ?? "";
    } catch {
      // Unreadable storage: no override.
    }
    const wsUrl = found?.wsUrl || override;
    if (!wsUrl) {
      void messageDialog(
        `No remote screen configured for ${name}.\n` +
          `Set GateBarrier:RemoteScreens on the API (or localStorage "vncWs:${name}").`,
      );
      return;
    }
    // Keep the rest of the configured screen (its lane camera) — only the ws URL is rewritten.
    setOpenScreen({ ...found, name, wsUrl: resolveVncWsUrl(wsUrl) });
  }
  const videoBase = getVideoBaseUrl(config?.videoBaseUrl);

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title="Gate/Barrier" />
        <div className={cc.body}>
    <div className="gb-page">
      <div className={`gb-panel${dock ? " gb-panel--dock" : ""}`}>
        {/* The same Live Weight body as Deliveries/Incoming (pages/weighbridge/LiveWeightPanel):
            the two readouts, then the touchscreen buttons beside the gate/barrier rows, in
            whichever style Super Admin > Scale/Relay Styles has chosen. */}
        <div className="gb-panel-weight">
          <h3 className="gb-card-title">Live Weight</h3>
          <LiveWeightBody
            weight={weight.value}
            weightStale={weight.stale}
            lostFor={weight.lostFor}
            weight2={weight2.value}
            weight2Stale={weight2.stale}
            lostFor2={weight2.lostFor}
            relay={state}
            relayChecked={relayChecked}
            busy={busy}
            onToggle={(w) => void toggle(w)}
            onRemote={openRemote}
            style={dock ? "dock" : "current"}
          />
        </div>

        {/* Camera selector */}
        <div className="gb-panel-cameras">
          <h3 className="gb-card-title">Camera</h3>
          <div className="gb-cameras">
            {(config?.cameras ?? []).map((cam) => (
              <label key={cam.name} className="gb-radio">
                <input
                  type="radio"
                  name="camera"
                  checked={selectedCamera?.name === cam.name}
                  onChange={() => setSelectedCamera(cam)}
                />
                {cam.name} <span className="gb-ip">({cam.ip})</span>
              </label>
            ))}
          </div>
        </div>

        {error && <div style={{ color: "var(--error-text)", fontSize: 12 }}>{error}</div>}
      </div>

      {/* Live video via go2rtc (WebRTC/MSE) */}
      <div className="gb-video">
        {selectedCamera?.stream ? (
          <CameraStream
            key={`${videoBase}|${selectedCamera.stream}`}
            videoBaseUrl={videoBase}
            stream={selectedCamera.stream}
          />
        ) : (
          <div className="gb-video-inner">
            <div className="gb-video-cam">{selectedCamera ? `${selectedCamera.name} Camera` : "No camera"}</div>
            <div className="gb-video-note">
              Video stream not configured. Set <code>GateBarrier:VideoBaseUrl</code> on the API and run
              go2rtc with the camera config.
            </div>
          </div>
        )}
      </div>
    </div>
        </div>
      </div>
      {openScreen && <RemoteScreenModal screen={openScreen} onClose={() => setOpenScreen(null)} />}
    </div>
  );
}
