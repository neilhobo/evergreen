import { useEffect, useState } from "react";
import TopBar from "../components/touch/TopBar";
import CameraStream from "../components/touch/CameraStream";
import { Camera, getGateBarrierConfig } from "../api/touch";
import { getVideoBaseUrl } from "../config";

interface Props {
  onHome: () => void;
}

// Camera. Ports the WPF Touch CameraView (a single live camera). The desktop hard-coded the
// Production camera at 192.168.250.191; here we use the API's Gate/Barrier config (go2rtc
// base + camera list) — the same source TauriAdmin uses — defaulting to the Production camera
// and offering a quick switcher when more than one is configured.
export default function CameraScreen({ onHome }: Props) {
  const [cameras, setCameras] = useState<Camera[]>([]);
  const [videoBase, setVideoBase] = useState("");
  const [selected, setSelected] = useState<string | null>(null); // go2rtc stream name
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        const cfg = await getGateBarrierConfig();
        setCameras(cfg.cameras);
        setVideoBase(getVideoBaseUrl(cfg.videoBaseUrl));
        const prod = cfg.cameras.find(
          (c) => c.stream?.toLowerCase() === "production" || c.name?.toLowerCase() === "production",
        );
        setSelected((prod ?? cfg.cameras[0])?.stream ?? null);
        if (cfg.cameras.length === 0) setError("No cameras are configured.");
      } catch (err) {
        setError((err as Error).message);
      }
    })();
  }, []);

  const switcher =
    cameras.length > 1 ? (
      <div className="tt-barrier" style={{ gap: 8, border: "none", padding: 0 }}>
        {cameras.map((c) => (
          <button
            key={c.stream}
            className={`tt-btn${selected === c.stream ? " tt-primary" : " tt-ghost"}`}
            style={{ minHeight: 40, padding: "0 16px" }}
            onClick={() => setSelected(c.stream)}
          >
            {c.name}
          </button>
        ))}
      </div>
    ) : undefined;

  return (
    <div className="tt-app">
      <TopBar title="Camera" onBack={onHome} right={switcher} hideBrand />
      <div className="tt-body">
        <div className="tt-panel" style={{ height: "100%", padding: 0, overflow: "hidden", display: "flex" }}>
          {error ? (
            <div className="tt-center" style={{ height: "100%", width: "100%" }}>
              <div className="tt-error">{error}</div>
            </div>
          ) : selected && videoBase ? (
            <CameraStream videoBaseUrl={videoBase} stream={selected} />
          ) : (
            <div className="tt-center" style={{ height: "100%", width: "100%" }}>
              <div className="tt-spinner-lg" />
              <div className="tt-note">Connecting to camera…</div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
