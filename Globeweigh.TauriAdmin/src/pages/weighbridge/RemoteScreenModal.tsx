import { useEffect, useRef, useState } from "react";
import RFB from "@novnc/novnc";
import Modal from "../../components/Modal";
import VncStage, { type VncStatus } from "./VncStage";
import { Camera, RemoteScreen, getConfig } from "../../api/gateBarrier";
import CameraStream from "../gate-barrier/CameraStream";
import { getVideoBaseUrl } from "../../config";
// Its rs-* styles live here; imported by the dialog itself so it looks right from any page.
import "./Weighbridge.css";

interface Props {
  screen: RemoteScreen;
  onClose: () => void;
}

type Status = VncStatus;

// Embedded remote-desktop view (full control) for a touchscreen, via noVNC over a
// websockify proxy (screen.wsUrl). No VNC client install required. Replaces the old
// external RealVNC launch.
export default function RemoteScreenModal({ screen, onClose }: Props) {
  const rfbRef = useRef<RFB | null>(null);
  const [status, setStatus] = useState<Status>("connecting");
  const [message, setMessage] = useState("");
  const [generation, setGeneration] = useState(0); // bump to (re)connect

  // "Show Camera" (footer switch): the dialog goes full screen and splits — the touchscreen on
  // the left, a camera on the right, so whoever is driving the screen can see the lorry.
  // Remembered per PC. The camera starts on this screen's own lane camera (its intercom, from
  // RemoteScreens[].Stream) when it has one, else the first Gate/Barrier camera; the panel's
  // header switches between them.
  const [showCamera, setShowCamera] = useState(readShowCamera);
  const [camera, setCamera] = useState<{ list: Camera[]; videoBase: string } | null>(null);
  const [cameraStream, setCameraStream] = useState<string | null>(null);
  const [cameraError, setCameraError] = useState<string | null>(null);

  useEffect(() => {
    if (!showCamera || camera) return;
    let cancelled = false;
    getConfig()
      .then((c) => {
        if (cancelled) return;
        const lane: Camera[] = screen.stream
          ? [{ name: screen.cameraName || `${screen.name} lane`, ip: "", stream: screen.stream }]
          : [];
        const list = [...lane, ...c.cameras.filter((x) => x.stream && x.stream !== screen.stream)];
        setCamera({ list, videoBase: getVideoBaseUrl(c.videoBaseUrl) });
        setCameraStream((s) => s ?? list[0]?.stream ?? null);
      })
      .catch((e) => !cancelled && setCameraError((e as Error).message));
    return () => {
      cancelled = true;
    };
  }, [showCamera, camera]);

  // The VNC view scales to its box (scaleViewport), and it only re-measures on a window resize —
  // so nudge it once the full-screen / split layout has settled.
  useEffect(() => {
    const id = requestAnimationFrame(() => window.dispatchEvent(new Event("resize")));
    return () => cancelAnimationFrame(id);
  }, [showCamera]);

  function toggleCamera() {
    const next = !showCamera;
    setShowCamera(next);
    writeShowCamera(next);
  }

  const statusLabel: Record<Status, string> = {
    connecting: "Connecting…",
    credentials: "Password required",
    connected: "Connected",
    disconnected: "Disconnected",
    error: "Error",
  };

  return (
    <Modal
      title={`Remote Connect — ${screen.name}`}
      width={1100}
      fullScreen={showCamera}
      height={860}
      maximizable={!showCamera}
      onClose={onClose}
      footer={
        <>
          <span className={`rs-status ${status}`}>
            {statusLabel[status]}
            {message ? ` — ${message}` : ""}
          </span>
          <span style={{ flex: 1 }} />
          <label className="rs-switch">
            <button
              type="button"
              role="switch"
              aria-checked={showCamera}
              className={`rs-switch-track${showCamera ? " on" : ""}`}
              onClick={toggleCamera}
            >
              <span className="rs-switch-knob" />
            </button>
            Show Camera
          </label>
          <button
            className="toolbar-btn"
            disabled={status !== "connected"}
            onClick={() => rfbRef.current?.sendCtrlAltDel()}
          >
            Ctrl+Alt+Del
          </button>
          <button className="toolbar-btn" onClick={() => setGeneration((g) => g + 1)}>
            Reconnect
          </button>
          <button className="btn-blue" onClick={onClose}>
            Close
          </button>
        </>
      }
    >
      {/* Always this wrapper, so the VNC stage below keeps its element (and its connection) when
          the camera is switched on or off. */}
      <div className={`rs-split${showCamera ? " on" : ""}`}>
      <VncStage
        wsUrl={screen.wsUrl}
        name={screen.name}
        generation={generation}
        onReconnect={() => setGeneration((g) => g + 1)}
        rfbRef={rfbRef}
        onStatus={(s, m) => {
          setStatus(s);
          setMessage(m);
        }}
      />

      {showCamera && (
        <div className="rs-camera">
          <div className="rs-camera-head">
            {camera && camera.list.length > 1 ? (
              <select
                id="rs-camera-pick"
                className="rs-camera-pick"
                value={cameraStream ?? ""}
                onChange={(e) => setCameraStream(e.target.value)}
                aria-label="Camera"
              >
                {camera.list.map((c) => (
                  <option key={c.stream} value={c.stream}>
                    {c.name} camera
                  </option>
                ))}
              </select>
            ) : camera?.list[0] ? (
              `${camera.list[0].name} camera`
            ) : (
              "Camera"
            )}
          </div>
          <div className="rs-camera-body">
            {cameraError ? (
              <div className="rs-camera-note">Camera settings could not be loaded — {cameraError}</div>
            ) : !camera ? (
              <div className="rs-camera-note">Loading camera…</div>
            ) : cameraStream ? (
              <CameraStream key={`${camera.videoBase}|${cameraStream}`} videoBaseUrl={camera.videoBase} stream={cameraStream} />
            ) : (
              <div className="rs-camera-note">No camera stream is configured (GateBarrier:Cameras on the API).</div>
            )}
          </div>
        </div>
      )}
      </div>
    </Modal>
  );
}

const SHOW_CAMERA_KEY = "remoteScreen.showCamera";

function readShowCamera(): boolean {
  try {
    return localStorage.getItem(SHOW_CAMERA_KEY) === "1";
  } catch {
    return false;
  }
}

function writeShowCamera(on: boolean) {
  try {
    localStorage.setItem(SHOW_CAMERA_KEY, on ? "1" : "0");
  } catch {
    // The choice just will not survive the session.
  }
}
