import { useEffect, useRef, useState } from "react";
import {
  getConfig,
  getState,
  getLiveWeight,
  setGate,
  setBarrier,
  type Camera,
  type GateBarrierConfig,
  type RelayState,
} from "../api/gateBarrier";
import CameraStream from "./CameraStream";
import { getVideoBaseUrl } from "../config";
import "./GateBarrier.css";

const STATE_POLL_MS = 1500;
const WEIGHT_POLL_MS = 1000;

export default function GateBarrierPage() {
  const [config, setConfig] = useState<GateBarrierConfig | null>(null);
  const [state, setState] = useState<RelayState>({ connected: false, gateOpen: false, barrierOpen: false });
  const [weight, setWeight] = useState<string>("—");
  const [weightStale, setWeightStale] = useState(true);
  const [selectedCamera, setSelectedCamera] = useState<Camera | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Avoid overlapping/looping polls when a request is slow (relay timeouts).
  const pollingRef = useRef(false);

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
      const r = await getLiveWeight();
      if (cancelled) return;
      if (r && r.isConnected && r.ageMs >= 0 && r.ageMs < 5000) {
        setWeight(r.weight || "0");
        setWeightStale(false);
      } else {
        setWeightStale(true);
      }
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

  const videoBase = getVideoBaseUrl(config?.videoBaseUrl);

  return (
    <div className="gb-page">
      <div className="gb-panel">
        <span className={`gb-status ${state.connected ? "ok" : "bad"}`}>
          <span className={`gb-dot ${state.connected ? "open" : "closed"}`} />
          Relay {state.connected ? "connected" : "not connected"}
        </span>

        {/* Live weight */}
        <div>
          <h3 className="gb-card-title">Live Weight</h3>
          <div className="gb-weight">
            <span className={`gb-weight-value ${weightStale ? "gb-weight-stale" : ""}`}>{weight}</span>
            <span className="gb-weight-unit">Kg</span>
          </div>
        </div>

        {/* Gate / barrier controls */}
        <div>
          <h3 className="gb-card-title">Controls</h3>
          <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            <div className="gb-control">
              <span className="gb-control-label">
                <span className={`gb-dot ${state.gateOpen ? "open" : "closed"}`} />
                Gate
              </span>
              <button
                className={`gb-btn ${state.gateOpen ? "close" : "open"}`}
                disabled={busy || !state.connected}
                onClick={() => toggle("gate")}
              >
                {state.gateOpen ? "Close" : "Open"}
              </button>
            </div>

            <div className="gb-control">
              <span className="gb-control-label">
                <span className={`gb-dot ${state.barrierOpen ? "open" : "closed"}`} />
                Barrier
              </span>
              <button
                className={`gb-btn ${state.barrierOpen ? "close" : "open"}`}
                disabled={busy || !state.connected}
                onClick={() => toggle("barrier")}
              >
                {state.barrierOpen ? "Close" : "Open"}
              </button>
            </div>
          </div>
        </div>

        {/* Camera selector */}
        <div>
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
  );
}
