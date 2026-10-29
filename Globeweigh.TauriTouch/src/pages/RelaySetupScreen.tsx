import { useCallback, useEffect, useState } from "react";
import TopBar from "../components/touch/TopBar";
import {
  RelayCardStatus,
  RelayConfig,
  RelayProbe,
  getSavedRelayConfig,
  onRelayState,
  probeRelayPorts,
  relayConnect,
  relayDisconnect,
  relaySet,
  saveRelayConfig,
} from "../api/relay";

interface Props {
  onBack: () => void;
}

const BAUDS = [1200, 2400, 4800, 9600, 19200, 38400, 115200];

// Relay-probe outcomes: every port is asked "are you the relay card?" (GET_VERSION
// handshake), so the right COM port identifies itself — no guesswork.
const PROBE_BADGE: Record<RelayProbe["status"], { color: string; label: string }> = {
  found: { color: "var(--tt-green)", label: "found" }, // label prefixed with the model
  silent: { color: "var(--tt-text-dim)", label: "No reply" },
  streaming: { color: "var(--tt-amber)", label: "Sending data — not a relay" },
  busy: { color: "var(--tt-red)", label: "In use" },
  error: { color: "var(--tt-red)", label: "Error" },
};

// Relay card setup (full page, like the weighscale setup). Left: connection — a probed
// COM port list where the card announces itself (auto-selected), or a network host.
// Right: live control once connected — the 8 relay switches and the 8 opto input states.
// Config persists per-machine in localStorage so the Home pill reflects it; the
// connection made here is a test session and is dropped on leaving the screen.
export default function RelaySetupScreen({ onBack }: Props) {
  const saved = getSavedRelayConfig();
  const [transport, setTransport] = useState<"serial" | "tcp">(saved?.transport ?? "serial");
  const [comPort, setComPort] = useState<string | null>(
    saved?.transport === "serial" ? saved.comPort : null,
  );
  const [baud, setBaud] = useState(saved?.transport === "serial" ? saved.baud : 19200);
  const [host, setHost] = useState(saved?.transport === "tcp" ? saved.host : "");
  const [tcpPort, setTcpPort] = useState(saved?.transport === "tcp" ? saved.port : 4196);

  const [ports, setPorts] = useState<RelayProbe[]>([]);
  const [loadingPorts, setLoadingPorts] = useState(true);
  const [portsError, setPortsError] = useState<string | null>(null);

  const [status, setStatus] = useState<RelayCardStatus | null>(null);
  const [connecting, setConnecting] = useState(false);
  const [connError, setConnError] = useState<string | null>(null);
  const connected = status?.connected ?? false;

  const config: RelayConfig | null =
    transport === "serial"
      ? comPort
        ? { transport: "serial", comPort, baud }
        : null
      : host.trim()
        ? { transport: "tcp", host: host.trim(), port: tcpPort }
        : null;

  // Persist whenever the config is complete, so the Home pill reflects it immediately.
  const configJson = config ? JSON.stringify(config) : null;
  useEffect(() => {
    if (configJson) saveRelayConfig(JSON.parse(configJson));
  }, [configJson]);

  // Probe every port: the relay card answers the version handshake, so it identifies
  // itself in the list — and gets auto-selected unless the user already picked a port
  // that also answered.
  const refreshPorts = useCallback(async () => {
    setLoadingPorts(true);
    setPortsError(null);
    try {
      const probes = await probeRelayPorts();
      setPorts(probes);
      const found = probes.filter((p) => p.status === "found");
      if (found.length > 0) {
        setComPort((current) =>
          current && found.some((p) => p.name === current) ? current : found[0].name,
        );
      }
    } catch (err) {
      setPorts([]);
      // Tauri invoke can reject with a plain string (e.g. ACL denials) — not an Error.
      setPortsError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoadingPorts(false);
    }
  }, []);

  useEffect(() => {
    void refreshPorts();
  }, [refreshPorts]);

  // State events while connected; drop the test connection when leaving the screen.
  useEffect(() => {
    const un = onRelayState((s) => {
      setStatus(s);
      if (s.error) setConnError(s.error);
    });
    return () => {
      void un.then((f) => f());
      void relayDisconnect();
    };
  }, []);

  async function toggleConnect() {
    if (connected) {
      await relayDisconnect(); // state event flips the UI
      return;
    }
    if (!config) return;
    setConnecting(true);
    setConnError(null);
    try {
      const model = await relayConnect(config);
      // Poll events follow within ~300ms; seed the UI so it flips immediately.
      setStatus({ connected: true, relays: 0, inputs: 0, model, error: null });
    } catch (err) {
      setConnError(err instanceof Error ? err.message : String(err));
    } finally {
      setConnecting(false);
    }
  }

  const dotColor = connected ? "var(--tt-green)" : connError ? "var(--tt-red)" : "var(--tt-text-dim)";
  const nums = [1, 2, 3, 4, 5, 6, 7, 8];

  return (
    <div className="tt-app tt-scale-setup">
      <TopBar title="Relay Setup" onBack={onBack} />
      <div className="tt-body" style={{ display: "flex", gap: 12 }}>
        {/* Connection source */}
        <div className="tt-panel" style={{ width: 400, flex: "none", display: "flex", flexDirection: "column" }}>
          <div className="tt-col-title" style={{ marginBottom: 8 }}>Connection</div>
          <div style={{ display: "flex", gap: 8, marginBottom: 12 }}>
            <button
              className={`tt-btn${transport === "serial" ? " tt-primary" : ""}`}
              style={{ flex: 1 }}
              onClick={() => setTransport("serial")}
            >
              COM Port
            </button>
            <button
              className={`tt-btn${transport === "tcp" ? " tt-primary" : ""}`}
              style={{ flex: 1 }}
              onClick={() => setTransport("tcp")}
            >
              Network
            </button>
          </div>

          {transport === "serial" ? (
            <>
              <div style={{ display: "flex", alignItems: "center", marginBottom: 8 }}>
                <div className="tt-col-title" style={{ margin: 0, flex: 1 }}>Ports on this PC</div>
                <button
                  className="tt-btn tt-ghost"
                  disabled={loadingPorts || connected}
                  title={connected ? "Disconnect first — probing opens every port" : undefined}
                  onClick={() => void refreshPorts()}
                >
                  ⟳ {loadingPorts ? "Detecting…" : "Detect"}
                </button>
              </div>
              <div className="tt-list" style={{ flex: "none", maxHeight: 320, overflow: "auto" }}>
                {loadingPorts && <div className="tt-note">Asking each port if it's the relay card…</div>}
                {!loadingPorts && portsError && <div className="tt-error">{portsError}</div>}
                {!loadingPorts && !portsError && ports.length === 0 && (
                  <div className="tt-note">No COM ports found on this PC.</div>
                )}
                {!loadingPorts &&
                  ports.map((p) => {
                    const badge = PROBE_BADGE[p.status];
                    const label = p.status === "found" ? `${p.model} found` : badge.label;
                    return (
                      <div
                        key={p.name}
                        className={`tt-list-item${comPort === p.name ? " tt-selected" : ""}`}
                        onClick={() => setComPort(p.name)}
                      >
                        <div style={{ display: "flex", alignItems: "center", gap: 7 }}>
                          <span style={{ fontWeight: 600, flex: 1 }}>{p.name}</span>
                          <span style={{ width: 9, height: 9, borderRadius: "50%", background: badge.color, flex: "none" }} />
                          <span style={{ fontSize: 12, color: p.status === "found" ? "var(--tt-green)" : "var(--tt-text-dim)" }}>
                            {label}
                          </span>
                        </div>
                        {p.description && (
                          <div style={{ fontSize: 12, color: "var(--tt-text-dim)" }}>{p.description}</div>
                        )}
                      </div>
                    );
                  })}
              </div>
              <div className="tt-col-title" style={{ margin: "12px 0 6px" }}>Baud Rate</div>
              <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
                {BAUDS.map((b) => (
                  <button
                    key={b}
                    className={`tt-btn${baud === b ? " tt-primary" : ""}`}
                    style={{ padding: "0 11px" }}
                    onClick={() => setBaud(b)}
                  >
                    {b}
                  </button>
                ))}
              </div>
              <div className="tt-note" style={{ marginTop: 10 }}>
                The card is a USB Serial COM port and ignores the baud rate.
              </div>
            </>
          ) : (
            <>
              <div className="tt-col-title" style={{ marginBottom: 5 }}>Host / IP</div>
              <input
                className="tt-input"
                value={host}
                placeholder="e.g. 192.168.250.60"
                onChange={(e) => setHost(e.target.value)}
              />
              <div className="tt-col-title" style={{ margin: "10px 0 5px" }}>TCP Port</div>
              <input
                className="tt-input"
                type="number"
                value={tcpPort}
                onChange={(e) => setTcpPort(Number(e.target.value) || 0)}
              />
              <div className="tt-note" style={{ marginTop: 10 }}>
                For a relay card reached via a serial server / network bridge.
              </div>
            </>
          )}

          <div style={{ flex: 1 }} />
          <div style={{ display: "flex", alignItems: "center", gap: 12, marginTop: 12 }}>
            <button
              className={`tt-btn ${connected ? "tt-danger" : "tt-primary"}`}
              style={{ flex: "none", minHeight: 44, padding: "0 22px" }}
              disabled={connecting || (!connected && !config)}
              onClick={() => void toggleConnect()}
            >
              {connecting ? "Connecting…" : connected ? "Disconnect" : "Connect"}
            </button>
            <span style={{ width: 12, height: 12, borderRadius: "50%", background: dotColor, flex: "none" }} />
            <span style={{ color: "var(--tt-text-dim)", fontSize: 13 }}>
              {connected
                ? `Connected · ${status?.model ?? "relay card"}`
                : connecting
                  ? "Opening…"
                  : "Not connected"}
            </span>
          </div>
          {connError && !connected && <div className="tt-error" style={{ marginTop: 8 }}>{connError}</div>}
        </div>

        {/* Relays + inputs */}
        <div className="tt-panel" style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0, overflow: "auto" }}>
          {connected ? (
            <>
              <div className="tt-col-title">Relays</div>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "4px 26px" }}>
                {nums.map((n) => {
                  const on = ((status?.relays ?? 0) & (1 << (n - 1))) !== 0;
                  return (
                    <div className="tt-toggle-row" key={n}>
                      <div>Relay {n}</div>
                      <button
                        className={`tt-toggle${on ? " tt-on" : ""}`}
                        onClick={() => void relaySet(n, !on).catch((e) => setConnError(String(e)))}
                        aria-label={`Relay ${n}`}
                      >
                        <span className="tt-knob" />
                      </button>
                    </div>
                  );
                })}
              </div>

              <div className="tt-col-title" style={{ marginTop: 18 }}>Inputs</div>
              <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 10 }}>
                {nums.map((n) => {
                  const high = ((status?.inputs ?? 0) & (1 << (n - 1))) !== 0;
                  return (
                    <div
                      key={n}
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: 8,
                        padding: "10px 12px",
                        borderRadius: 8,
                        background: "rgba(255,255,255,0.04)",
                      }}
                    >
                      <span
                        style={{
                          width: 11,
                          height: 11,
                          borderRadius: "50%",
                          background: high ? "var(--tt-green)" : "var(--tt-text-dim)",
                          flex: "none",
                        }}
                      />
                      <span style={{ fontSize: 14 }}>Input {n}</span>
                    </div>
                  );
                })}
              </div>
              <div className="tt-note" style={{ marginTop: 12 }}>
                Inputs are read-only signals fed into the card.
              </div>
            </>
          ) : (
            <div className="tt-center" style={{ height: "100%", gap: 10 }}>
              <div className="tt-note" style={{ fontSize: 17 }}>
                {config
                  ? "Connect to control the relays and see the inputs."
                  : transport === "serial"
                    ? "Select the relay card's COM port — Detect finds it automatically."
                    : "Enter the relay card's host and port."}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
