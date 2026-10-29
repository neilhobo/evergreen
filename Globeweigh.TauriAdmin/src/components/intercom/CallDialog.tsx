import { useEffect, useRef, useState } from "react";
import { hangupRing, type IntercomRing } from "../../api/intercom";
import { listMicrophones, startGoRtcCall, type GoRtcCall } from "./goRtcCall";

interface Props {
  ring: IntercomRing;
  stream: string;
  videoBaseUrl: string;
  snapshotUrl: string | null;
  onClose: () => void;
}

// This PC's chosen microphone, remembered between calls. A per-machine convenience only:
// if storage is unavailable the call just uses the Windows default.
const MIC_KEY = "intercomMicId";
function savedMic(): string | null {
  try {
    return localStorage.getItem(MIC_KEY);
  } catch {
    return null;
  }
}
function saveMic(id: string) {
  try {
    localStorage.setItem(MIC_KEY, id);
  } catch {
    // not remembered — fine
  }
}

function elapsed(fromMs: number, nowMs: number): string {
  const s = Math.max(0, Math.floor((nowMs - fromMs) / 1000));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
}

// Full-screen call with the driver at the weighbridge: their video and sound, and this
// PC's microphone out to the intercom speaker. Closes only on Hang up.
export default function CallDialog({ ring, stream, videoBaseUrl, snapshotUrl, onClose }: Props) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const callRef = useRef<GoRtcCall | null>(null);
  const [state, setState] = useState<"connecting" | "connected" | "failed" | "closed">("connecting");
  const [mic, setMic] = useState<{ ok: boolean; reason?: string } | null>(null);
  const [mics, setMics] = useState<MediaDeviceInfo[]>([]);
  const [micId, setMicId] = useState<string>(() => savedMic() ?? "");
  const [level, setLevel] = useState(0);
  const [muted, setMuted] = useState(false);
  const [now, setNow] = useState(Date.now());
  const [startedMs] = useState(() => Date.now());

  useEffect(() => {
    let cancelled = false;
    startGoRtcCall({
      videoBaseUrl,
      stream,
      video: videoRef.current!,
      micDeviceId: savedMic(),
      onMic: (ok, reason) => {
        if (cancelled) return;
        setMic({ ok, reason });
        // Device names only come through once the microphone has been allowed.
        if (ok) listMicrophones().then(setMics).catch(() => {});
      },
      onState: (s) => !cancelled && setState(s),
    }).then((call) => {
      if (cancelled) call.hangup();
      else callRef.current = call;
    });
    const tick = window.setInterval(() => setNow(Date.now()), 1000);
    const meter = window.setInterval(() => {
      callRef.current?.micLevel().then((l) => setLevel(l ?? 0)).catch(() => {});
    }, 150);
    return () => {
      cancelled = true;
      window.clearInterval(tick);
      window.clearInterval(meter);
      callRef.current?.hangup();
      callRef.current = null;
    };
  }, [videoBaseUrl, stream]);

  const hangUp = () => {
    callRef.current?.hangup();
    callRef.current = null;
    hangupRing(ring.id).catch(() => {});
    onClose();
  };

  const toggleMute = () => {
    const next = !muted;
    callRef.current?.setMicMuted(next);
    setMuted(next);
  };

  const chooseMic = async (id: string) => {
    setMicId(id);
    saveMic(id);
    await callRef.current?.setMicDevice(id);
  };

  // audioLevel is linear; a square root makes ordinary speech fill a sensible part of the bar.
  const meterPct = Math.min(100, Math.round(Math.sqrt(level) * 100));

  return (
    <div className="icd-backdrop" role="dialog" aria-label="Intercom call">
      <div className="icd">
        <div className="icd-head">
          <div>
            <div className="icd-title">{ring.unit || "Weighbridge intercom"}</div>
            <div className="icd-sub">
              {state === "connected" ? `In call · ${elapsed(startedMs, now)}` : state === "failed" ? "Could not connect to the intercom" : "Connecting…"}
            </div>
          </div>
          {mic && !mic.ok && <div className="icd-warn">Listen only — no microphone ({mic.reason})</div>}
          {mic?.ok && (
            <div className="icd-mic">
              <select value={micId} onChange={(e) => chooseMic(e.target.value)} title="Microphone">
                <option value="">Windows default microphone</option>
                {mics
                  .filter((d) => d.deviceId && d.deviceId !== "default" && d.deviceId !== "communications")
                  .map((d) => (
                    <option key={d.deviceId} value={d.deviceId}>
                      {d.label || "Microphone"}
                    </option>
                  ))}
              </select>
              <div className="icd-meter" title="Microphone level">
                <div className="icd-meter-fill" style={{ width: `${muted ? 0 : meterPct}%` }} />
              </div>
            </div>
          )}
        </div>

        <div className="icd-video">
          <video ref={videoRef} autoPlay playsInline poster={snapshotUrl ?? undefined} />
        </div>

        <div className="icd-actions">
          <button className="icd-btn icd-btn-mute" onClick={toggleMute} disabled={!mic?.ok}>
            {muted ? "Unmute mic" : "Mute mic"}
          </button>
          <button className="icd-btn icd-btn-hangup" onClick={hangUp}>
            Hang up
          </button>
        </div>
      </div>
    </div>
  );
}
