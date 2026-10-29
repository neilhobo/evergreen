import { useCallback, useEffect, useRef, useState } from "react";
import TopBar from "../components/touch/TopBar";
import {
  PortProbe,
  WeighscaleConfig,
  connectWeighscale,
  disconnectWeighscale,
  frameFields,
  getSavedWeighscaleConfig,
  inTauriShell,
  onWeighscaleData,
  onWeighscaleStatus,
  probeComPorts,
  saveWeighscaleConfig,
} from "../api/serial";

interface Props {
  onBack: () => void;
}

const BAUDS = [1200, 2400, 4800, 9600, 19200, 38400, 115200];
const DECIMALS = [0, 1, 2, 3];
const POLL_INTERVALS = [250, 500, 1000];

// Port probe outcomes, in plain words. "Receiving data" is almost certainly the scale
// (continuous mode); "silent" ports may hold an on-request scale — or nothing.
const PROBE_BADGE: Record<PortProbe["status"], { color: string; label: string }> = {
  streaming: { color: "var(--tt-green)", label: "Receiving data" },
  silent: { color: "var(--tt-amber)", label: "Opens OK — silent" },
  busy: { color: "var(--tt-red)", label: "In use" },
  error: { color: "var(--tt-red)", label: "Error" },
};

// Reusable weighscale setup screen (full page, not a popup): configure the indicator this PC
// reads AND work out its protocol by eye. Left: connection — a probed COM port list or a
// network host (Moxa NPort / weighscale-bridge.ps1), plus continuous vs polled mode. Right:
// live raw frames split per line; the numeric fields of the latest frame are offered as
// buttons so the user can point at the one that is the weight (put a known weight on the
// scale, tap the field that changes), plus an implied-decimals choice — together they turn
// the raw stream into the live kg readout. Everything persists per-machine in localStorage;
// the connection made here is a test session and is dropped on leaving the screen.
export default function WeighscaleSetupScreen({ onBack }: Props) {
  const saved = getSavedWeighscaleConfig();
  const [transport, setTransport] = useState<"serial" | "tcp">(saved?.transport ?? "serial");
  const [comPort, setComPort] = useState<string | null>(
    saved?.transport === "serial" ? saved.comPort : null,
  );
  const [baud, setBaud] = useState(saved?.transport === "serial" ? saved.baud : 9600);
  const [host, setHost] = useState(saved?.transport === "tcp" ? saved.host : "");
  const [tcpPort, setTcpPort] = useState(saved?.transport === "tcp" ? saved.port : 4001);
  const [weightField, setWeightField] = useState<number | null>(saved?.parse?.weightField ?? null);
  const [decimals, setDecimals] = useState(saved?.parse?.decimals ?? 0);
  const [mode, setMode] = useState<"continuous" | "poll">(saved?.poll ? "poll" : "continuous");
  const [pollCommand, setPollCommand] = useState(saved?.poll?.command ?? "q%");
  const [pollInterval, setPollInterval] = useState(saved?.poll?.intervalMs ?? 500);

  const [ports, setPorts] = useState<PortProbe[]>([]);
  const [loadingPorts, setLoadingPorts] = useState(true);
  const [portsError, setPortsError] = useState<string | null>(null);

  const [connected, setConnected] = useState(false);
  const [connecting, setConnecting] = useState(false);
  const [connError, setConnError] = useState<string | null>(null);
  const [lines, setLines] = useState<string[]>([]);
  // Total frames since connect/clear — the monitor only keeps the last 200, and with an
  // identical repeating frame this ticking count is the visible proof data is still flowing.
  const [lineCount, setLineCount] = useState(0);
  const tailRef = useRef(""); // partial frame carried between chunks
  const monitorRef = useRef<HTMLPreElement>(null);

  const config: WeighscaleConfig | null =
    transport === "serial"
      ? comPort
        ? { transport: "serial", comPort, baud }
        : null
      : host.trim()
        ? { transport: "tcp", host: host.trim(), port: tcpPort }
        : null;
  if (config && weightField !== null) config.parse = { weightField, decimals };
  if (config && mode === "poll" && pollCommand.trim()) {
    config.poll = { command: pollCommand.trim(), intervalMs: pollInterval };
  }

  // Persist whenever the config is complete, so the Home pill reflects it immediately.
  const configJson = config ? JSON.stringify(config) : null;
  useEffect(() => {
    if (configJson) saveWeighscaleConfig(JSON.parse(configJson));
  }, [configJson]);

  // Probe every port (parallel, <1s): can it open, and is anything transmitting? Probing
  // opens ports, so it's blocked while connected — it would steal/report our own port.
  const refreshPorts = useCallback(async (baudToUse: number) => {
    setLoadingPorts(true);
    setPortsError(null);
    try {
      setPorts(await probeComPorts(baudToUse));
    } catch (err) {
      setPorts([]);
      // Tauri invoke can reject with a plain string (e.g. ACL denials) — not an Error.
      setPortsError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoadingPorts(false);
    }
  }, []);

  useEffect(() => {
    void refreshPorts(9600);
  }, [refreshPorts]);

  // Stream + status events; drop the test connection when leaving the screen.
  useEffect(() => {
    if (!inTauriShell()) return;
    const unData = onWeighscaleData((text) => {
      // Frame the byte stream into lines on CR/LF; the tail is an incomplete frame.
      const parts = (tailRef.current + text).split(/[\r\n]+/);
      tailRef.current = parts.pop() ?? "";
      // Strip control chars (STX etc.); the checksum byte after CR stays visible at the
      // start of the next line, exactly as HyperTerminal shows these indicators.
      const frames = parts.map((p) => p.replace(/[\x00-\x1f\x7f]/g, "")).filter((p) => p.trim());
      if (frames.length) {
        setLines((ls) => [...ls, ...frames].slice(-200));
        setLineCount((n) => n + frames.length);
      }
    });
    const unStatus = onWeighscaleStatus((s) => {
      setConnected(s.connected);
      if (s.error) setConnError(s.error);
    });
    return () => {
      void unData.then((f) => f());
      void unStatus.then((f) => f());
      void disconnectWeighscale();
    };
  }, []);

  // Keep the monitor pinned to the newest frame.
  useEffect(() => {
    const el = monitorRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [lines]);

  function clearMonitor() {
    tailRef.current = "";
    setLines([]);
    setLineCount(0);
  }

  async function toggleConnect() {
    if (connected) {
      await disconnectWeighscale(); // status event flips the UI
      return;
    }
    if (!config) return;
    setConnecting(true);
    setConnError(null);
    clearMonitor();
    try {
      await connectWeighscale(config);
      setConnected(true); // command success = connected, even if the status event was missed
    } catch (err) {
      setConnError(String(err));
    } finally {
      setConnecting(false);
    }
  }

  // Parsing preview, all from the latest frame.
  const lastFrame = lines.length > 0 ? lines[lines.length - 1] : null;
  const fields = lastFrame ? frameFields(lastFrame) : [];
  const rawWeight = weightField !== null ? fields[weightField] : undefined;
  const liveWeight =
    rawWeight !== undefined && Number.isFinite(parseInt(rawWeight, 10))
      ? parseInt(rawWeight, 10) / Math.pow(10, decimals)
      : null;

  const dotColor = connected ? "var(--tt-green)" : connError ? "var(--tt-red)" : "var(--tt-text-dim)";

  return (
    <div className="tt-app tt-scale-setup">
      <TopBar title="Weighscale Setup" onBack={onBack} />
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
                  onClick={() => void refreshPorts(baud)}
                >
                  ⟳ {loadingPorts ? "Checking…" : "Re-check"}
                </button>
              </div>
              <div className="tt-list" style={{ flex: "none", maxHeight: 320, overflow: "auto" }}>
                {loadingPorts && <div className="tt-note">Checking each port for a connection…</div>}
                {!loadingPorts && portsError && <div className="tt-error">{portsError}</div>}
                {!loadingPorts && !portsError && ports.length === 0 && (
                  <div className="tt-note">No COM ports found on this PC.</div>
                )}
                {!loadingPorts &&
                  ports.map((p) => {
                    const badge = PROBE_BADGE[p.status];
                    return (
                      <div
                        key={p.name}
                        className={`tt-list-item${comPort === p.name ? " tt-selected" : ""}`}
                        onClick={() => setComPort(p.name)}
                      >
                        <div style={{ display: "flex", alignItems: "center", gap: 7 }}>
                          <span style={{ fontWeight: 600, flex: 1 }}>{p.name}</span>
                          <span style={{ width: 9, height: 9, borderRadius: "50%", background: badge.color, flex: "none" }} />
                          <span style={{ fontSize: 12, color: "var(--tt-text-dim)" }}>{badge.label}</span>
                        </div>
                        {p.description && (
                          <div style={{ fontSize: 12, color: "var(--tt-text-dim)" }}>{p.description}</div>
                        )}
                        {p.detail && (
                          <div
                            style={{
                              fontSize: 11,
                              color: p.status === "streaming" ? "#7ee787" : "var(--tt-text-dim)",
                              fontFamily: p.status === "streaming" ? "Consolas, monospace" : "inherit",
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              whiteSpace: "nowrap",
                            }}
                          >
                            {p.detail}
                          </div>
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
            </>
          ) : (
            <>
              {/* Moxa NPort in TCP-server mode, or weighscale-bridge.ps1 on the remote PC. */}
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
                For a Moxa/serial-server indicator, or the weighscale bridge on another PC.
              </div>
            </>
          )}

          {/* Continuous scales stream on their own; on-request scales must be asked, so we
              send the poll command (+ Enter) on an interval once connected. */}
          <div className="tt-col-title" style={{ margin: "12px 0 6px" }}>Scale Mode</div>
          <div style={{ display: "flex", gap: 8 }}>
            <button
              className={`tt-btn${mode === "continuous" ? " tt-primary" : ""}`}
              style={{ flex: 1 }}
              onClick={() => setMode("continuous")}
            >
              Continuous
            </button>
            <button
              className={`tt-btn${mode === "poll" ? " tt-primary" : ""}`}
              style={{ flex: 1 }}
              onClick={() => setMode("poll")}
            >
              On Request
            </button>
          </div>
          {mode === "poll" && (
            <div style={{ display: "flex", gap: 8, marginTop: 8, alignItems: "center", flexWrap: "wrap" }}>
              <input
                className="tt-input"
                style={{ width: 90, flex: "none" }}
                value={pollCommand}
                placeholder="q%"
                onChange={(e) => setPollCommand(e.target.value)}
              />
              <span className="tt-note" style={{ flex: "none" }}>+ Enter, every</span>
              {POLL_INTERVALS.map((ms) => (
                <button
                  key={ms}
                  className={`tt-btn${pollInterval === ms ? " tt-primary" : ""}`}
                  style={{ padding: "0 10px" }}
                  onClick={() => setPollInterval(ms)}
                >
                  {ms >= 1000 ? `${ms / 1000}s` : `${ms}ms`}
                </button>
              ))}
            </div>
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
              {connected ? "Connected" : connecting ? "Opening…" : "Not connected"}
            </span>
          </div>
          {connError && !connected && <div className="tt-error" style={{ marginTop: 8 }}>{connError}</div>}
        </div>

        {/* Raw stream + parsing */}
        <div className="tt-panel" style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 5 }}>
            <div className="tt-col-title" style={{ margin: 0 }}>Live Data</div>
            <span
              style={{
                fontSize: 13,
                color: lineCount > 0 ? "#7ee787" : "var(--tt-text-dim)",
                fontVariantNumeric: "tabular-nums",
                flex: 1,
              }}
            >
              {lineCount.toLocaleString()} lines
            </span>
            <button className="tt-btn tt-ghost" disabled={lineCount === 0} onClick={clearMonitor}>
              Clear
            </button>
          </div>
          <pre ref={monitorRef} className="tt-monitor" style={{ flex: 1, minHeight: 100 }}>
            {lines.length > 0
              ? lines.join("\n")
              : connected
                ? "Waiting for data from the indicator…"
                : "Connect to see the raw indicator stream."}
          </pre>

          <div className="tt-col-title" style={{ margin: "12px 0 4px" }}>Weight Parsing</div>
          <div className="tt-note" style={{ marginBottom: 8 }}>
            Numeric parts found in the last line — put a known weight on the scale and tap the part
            that shows it.
          </div>
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap", marginBottom: 10 }}>
            {fields.length === 0 && <div className="tt-note">No numbers detected yet.</div>}
            {fields.map((value, i) => (
              <button
                key={i}
                className={`tt-btn${weightField === i ? " tt-primary" : ""}`}
                style={{ minHeight: 48, padding: "5px 15px", display: "flex", flexDirection: "column", gap: 1 }}
                onClick={() => setWeightField(weightField === i ? null : i)}
              >
                <span style={{ fontSize: 17, fontVariantNumeric: "tabular-nums" }}>{value}</span>
                <span style={{ fontSize: 10, fontWeight: 400, opacity: 0.7 }}>Field {i + 1}</span>
              </button>
            ))}
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: 14, flexWrap: "wrap" }}>
            <div>
              <div className="tt-col-title" style={{ marginBottom: 5 }}>Decimal Places</div>
              <div style={{ display: "flex", gap: 6 }}>
                {DECIMALS.map((d) => (
                  <button
                    key={d}
                    className={`tt-btn${decimals === d ? " tt-primary" : ""}`}
                    style={{ padding: "0 16px" }}
                    onClick={() => setDecimals(d)}
                  >
                    {d}
                  </button>
                ))}
              </div>
            </div>
            <div style={{ flex: 1 }} />
            <div className={`tt-gauge${connected && liveWeight !== null ? "" : " tt-stale"}`} style={{ padding: "8px 20px" }}>
              <div className="tt-gauge-val" style={{ fontSize: 38 }}>
                {liveWeight === null ? "----" : liveWeight.toFixed(decimals)}
                <span className="tt-gauge-unit"> kg</span>
              </div>
              <div className="tt-gauge-status">
                {weightField === null ? "Tap the weight field above" : `Field ${weightField + 1}, ${decimals} dp`}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
