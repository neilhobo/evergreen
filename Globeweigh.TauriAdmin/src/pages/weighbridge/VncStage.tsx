import { FormEvent, MutableRefObject, useEffect, useRef, useState } from "react";
import RFB from "@novnc/novnc";
// Its rs-* styles live here; imported by the stage itself so it looks right from any page.
import "./Weighbridge.css";

export type VncStatus = "connecting" | "credentials" | "connected" | "disconnected" | "error";

interface Props {
  wsUrl: string;
  name: string;
  // Bump to (re)connect.
  generation?: number;
  onReconnect?: () => void;
  onStatus?: (status: VncStatus, message: string) => void;
  // Lets the owner reach the live connection (Ctrl+Alt+Del).
  rfbRef?: MutableRefObject<RFB | null>;
}

// The embedded remote-desktop view (full control) of a touchscreen, via noVNC over a websockify
// proxy. Shared by the Remote Connect dialog and the intercom call's split view.
export default function VncStage({ wsUrl, name, generation = 0, onReconnect, onStatus, rfbRef }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const ownRef = useRef<RFB | null>(null);
  const [status, setStatusState] = useState<VncStatus>("connecting");
  const [message, setMessageState] = useState("");
  const [password, setPassword] = useState("");
  const [localGen, setLocalGen] = useState(0);
  const onStatusRef = useRef(onStatus);
  onStatusRef.current = onStatus;

  const report = (s: VncStatus, m: string) => {
    setStatusState(s);
    setMessageState(m);
    onStatusRef.current?.(s, m);
  };

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    report("connecting", "");

    let rfb: RFB;
    try {
      rfb = new RFB(container, wsUrl, { shared: true });
    } catch (e) {
      report("error", (e as Error).message);
      return;
    }
    rfb.viewOnly = false; // full keyboard/mouse control
    rfb.scaleViewport = true; // fit the remote framebuffer to the panel
    rfb.focusOnClick = true;
    rfb.background = "#1a2332";
    ownRef.current = rfb;
    if (rfbRef) rfbRef.current = rfb;

    const onConnect = () => {
      report("connected", "");
      rfb.focus();
    };
    const onDisconnect = (e: Event) => {
      const clean = (e as CustomEvent).detail?.clean;
      report("disconnected", clean ? "Disconnected." : "Connection lost — check the proxy / screen.");
    };
    const onCredentials = () => report("credentials", "");
    const onSecurityFailure = (e: Event) => {
      const reason = (e as CustomEvent).detail?.reason;
      report("error", "Authentication failed" + (reason ? `: ${reason}` : "."));
    };

    rfb.addEventListener("connect", onConnect);
    rfb.addEventListener("disconnect", onDisconnect);
    rfb.addEventListener("credentialsrequired", onCredentials);
    rfb.addEventListener("securityfailure", onSecurityFailure);

    return () => {
      rfb.removeEventListener("connect", onConnect);
      rfb.removeEventListener("disconnect", onDisconnect);
      rfb.removeEventListener("credentialsrequired", onCredentials);
      rfb.removeEventListener("securityfailure", onSecurityFailure);
      try {
        rfb.disconnect();
      } catch {
        /* already disconnected */
      }
      ownRef.current = null;
      if (rfbRef) rfbRef.current = null;
    };
  }, [wsUrl, generation, localGen]); // eslint-disable-line react-hooks/exhaustive-deps

  function submitPassword(e: FormEvent) {
    e.preventDefault();
    ownRef.current?.sendCredentials({ password });
    report("connecting", "");
  }

  const reconnect = () => (onReconnect ? onReconnect() : setLocalGen((g) => g + 1));

  return (
    <div className="rs-stage">
      <div ref={containerRef} className="rs-canvas" />

      {status === "credentials" && (
        <form className="rs-cred" onSubmit={submitPassword}>
          <h3>{name} — VNC password</h3>
          <input
            className="form-input"
            type="password"
            autoFocus
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          <button className="btn-blue" type="submit" disabled={!password}>
            Connect
          </button>
        </form>
      )}

      {(status === "disconnected" || status === "error") && (
        <div className="rs-overlay">
          <div className="rs-overlay-msg">{message || "Disconnected."}</div>
          <button className="btn-blue" onClick={reconnect}>
            Reconnect
          </button>
        </div>
      )}
    </div>
  );
}
