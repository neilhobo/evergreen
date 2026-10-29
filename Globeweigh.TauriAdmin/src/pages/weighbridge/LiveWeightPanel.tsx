import { RelayState } from "../../api/gateBarrier";

// The Live Weight column's body on Deliveries/Incoming: the two bridge readouts, the gate and
// barrier controls with their relay state, and the remote-view buttons for the touchscreens.
// Split out of WeighbridgeRecordsPage so Super Admin > Scale/Relay Styles can draw the same
// component with sample values as a preview of each style.
//
// Two skins share this one markup (Weighbridge.css). "current" is the WPF look carried over: a
// dark readout with green digits, filled Open/Close buttons and a round lamp per relay. "dock" is
// the 2026-09-24 restyle chosen from six mock-ups: the sidebar's navy in the column, white digits
// with a live lamp per bridge, and a state chip beside a plain button. Which one draws is a
// server-held flag (STYLE.scaleRelayDarkDock in api/features.ts), so every PC shows the same.
export type LiveWeightStyle = "current" | "dock";

export interface LiveWeightBodyProps {
  weight: string;
  weightStale: boolean;
  weight2: string;
  weight2Stale: boolean;
  /** How long a silent bridge has sent nothing ("4m"), shown under it as "No data for 4m". Gate/Barrier
      passes these; Deliveries does not, and shows no note. */
  lostFor?: string | null;
  lostFor2?: string | null;
  relay: RelayState;
  /** False until the first relay poll has answered — shows "Connecting", not the fault box. */
  relayChecked: boolean;
  busy: boolean;
  onToggle: (which: "gate" | "barrier") => void;
  onRemote: (name: string) => void;
  style: LiveWeightStyle;
}

export default function LiveWeightBody(p: LiveWeightBodyProps) {
  const dock = p.style === "dock";

  const bridge = (label: string, value: string, stale: boolean, lostFor?: string | null) => (
    <div className="wb-weight-block">
      <div className="wb-weight-label">
        <span>{label}</span>
        {/* Dock: a lamp that is lit while readings are arriving; the digits go grey when not. */}
        {dock && <span className={`wb-weight-led${stale ? "" : " on"}`} aria-hidden="true" />}
      </div>
      <div className="wb-weight">
        <span className={`wb-weight-value${stale ? " stale" : ""}`}>{value}</span>
        <span className="wb-weight-unit">{dock ? "kg" : "Kg"}</span>
      </div>
      {stale && lostFor !== undefined && (
        <div className="wb-weight-lost">{lostFor ? `No data for ${lostFor}` : "No data"}</div>
      )}
    </div>
  );

  const relayRow = (label: string, which: "gate" | "barrier", open: boolean) => (
    <div className="wb-relay-row">
      <span className="wb-relay-label">{dock ? label : `${label}:`}</span>
      {/* Dock: the state is a chip in words; current: the round lamp after the button. */}
      {dock && <span className={`wb-relay-chip${open ? " on" : ""}`}>{open ? "Open" : "Closed"}</span>}
      <button
        className={`wb-relay-btn ${open ? "close" : "open"}`}
        disabled={p.busy || !p.relay.connected}
        onClick={() => p.onToggle(which)}
      >
        {open ? "Close" : "Open"}
      </button>
      {!dock && <span className={`wb-relay-dot ${open ? "on" : "off"}`} />}
    </div>
  );

  return (
    <div className={`wb-live${dock ? " wb-live--dock" : ""}`}>
      <div className="wb-weights">
        {bridge("Bridge 1", p.weight, p.weightStale, p.lostFor)}
        {bridge("Bridge 2", p.weight2, p.weight2Stale, p.lostFor2)}
      </div>

      <div className="wb-relay">
        <div className="wb-relay-controls">
          {relayRow("Gate", "gate", p.relay.gateOpen)}
          {relayRow("Barrier", "barrier", p.relay.barrierOpen)}

          {/* Still waiting on the first relay poll: a soft panel in the column's own colour. */}
          {!p.relayChecked && (
            <div className="wb-relay-checking" role="status">
              <span className="wb-relay-spinner" aria-hidden="true" />
              Connecting to relay…
            </div>
          )}

          {/* IO card / relay fault — shaded error box over the controls + lights. Only once a
              poll has actually answered "not connected" (or failed). */}
          {p.relayChecked && !p.relay.connected && (
            <div className="wb-relay-error" role="alert">
              <span className="wb-relay-error-icon">⚠</span>
              <div>
                <div className="wb-relay-error-title">IO card not connected</div>
                <div className="wb-relay-error-sub">Gate / barrier controls unavailable — check the relay IO card.</div>
              </div>
            </div>
          )}
        </div>
        {p.relay.connected && <span className="wb-relay-status ok">Relay connected</span>}
      </div>

      {/* Remote Connect — VNC to the TOUCH-IN / TOUCH-OUT touchscreens (WPF parity) */}
      <div className="wb-remote">
        {!dock && <h3 className="wb-panel-title">Remote Connect</h3>}
        <div className="wb-remote-links">
          <button className="wb-remote-link" onClick={() => p.onRemote("TOUCH-IN")}>
            TOUCH-IN
          </button>
          <button className="wb-remote-link" onClick={() => p.onRemote("TOUCH-OUT")}>
            TOUCH-OUT
          </button>
        </div>
      </div>
    </div>
  );
}
