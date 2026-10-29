import { useEffect, useRef, useState } from "react";
import {
  DeviceConfig,
  ProductionAlarmState,
  getProductionAlarm,
  suppressProductionAlarm,
} from "../api/touch";
import { getSavedRelayConfig, relayConnect, relayDisconnect, relaySet } from "../api/relay";
import { inTauriShell } from "../api/serial";

// App-wide production missed-scan alarm (mounted at App level, so it works whatever
// screen is showing). Two independent roles, both driven by polling the shared alarm
// state on the API:
//   * POPUP — devices with the ProductionAlarm flag show a centre-screen alarm with a
//     Silence button. Silencing acks the missed pallets on the API, which clears the
//     popup AND the siren on every PC within one poll tick.
//   * SIREN — the ONE device with the RelayCard flag + a saved relay config drives
//     relay 1 (an alarm siren is hard-wired to it on site) to match the alarm state.
//
// SAFETY: dev builds NEVER touch the relay — not even "off". A real siren is wired to
// relay 1 at the client site, and a dev shell can carry a TCP relay config pointing at
// that machine's bridge. Only the deployed production build actuates hardware.
const DRIVE_RELAY_ALLOWED = !import.meta.env.DEV;

const POLL_MS = 3000;
const ALARM_RELAY = 1;
// Cap the listed pallets — the list scrolls, but a huge backlog still shouldn't
// bury the Silence button under pages of codes.
const MAX_LISTED = 12;

export default function ProductionAlarmAgent({ device }: { device: DeviceConfig }) {
  const showPopup = device.productionAlarm;
  const driveRelay =
    DRIVE_RELAY_ALLOWED && device.relayCard && inTauriShell() && getSavedRelayConfig() !== null;

  const [alarm, setAlarm] = useState<ProductionAlarmState | null>(null);
  const [suppressing, setSuppressing] = useState(false);

  // Last state successfully written to the relay; null = unknown (forces a write).
  const appliedRef = useRef<boolean | null>(null);
  const relayBusyRef = useRef(false);

  async function syncRelay(on: boolean) {
    if (!driveRelay || relayBusyRef.current || appliedRef.current === on) return;
    relayBusyRef.current = true;
    try {
      const config = getSavedRelayConfig();
      if (!config) return;
      await relayConnect(config);
      await relaySet(ALARM_RELAY, on);
      appliedRef.current = on;
      // Leave the port free while quiet (so e.g. the Relay setup screen can probe it).
      if (!on) await relayDisconnect();
    } catch {
      // Card unreachable — appliedRef stays stale, so the next poll tick retries.
    } finally {
      relayBusyRef.current = false;
    }
  }

  useEffect(() => {
    if (!showPopup && !driveRelay) return;
    let stopped = false;
    // Startup safety reset: force the siren off so a crashed session can't leave it wailing.
    void syncRelay(false);
    const tick = async () => {
      try {
        const a = await getProductionAlarm();
        if (stopped) return;
        setAlarm(a);
        void syncRelay(a.active);
      } catch {
        // API unreachable — keep the last known state; retry next tick.
      }
    };
    void tick();
    const t = window.setInterval(() => void tick(), POLL_MS);
    return () => {
      stopped = true;
      window.clearInterval(t);
      void syncRelay(false); // best-effort silence when the app tears down
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showPopup, driveRelay]);

  async function silence() {
    setSuppressing(true);
    try {
      await suppressProductionAlarm(device.deviceName);
      setAlarm({ active: false, pallets: [] });
      void syncRelay(false); // instant if this PC is also the relay PC; others follow the poll
    } catch {
      // Suppress failed (API blip) — keep the popup so it can be pressed again.
    } finally {
      setSuppressing(false);
    }
  }

  if (!showPopup || !alarm?.active) return null;

  return (
    <div className="tt-alarm-overlay">
      <div className="tt-alarm-panel">
        <div className="tt-alarm-ico" aria-hidden="true">🚨</div>
        <div className="tt-alarm-title">MISSED SCAN</div>
        <div className="tt-alarm-sub">
          A pallet passed the scanner without being scanned — the alarm is sounding.
        </div>
        <div className="tt-alarm-pallets">
          {alarm.pallets
            .slice(0, MAX_LISTED)
            .map((p) => p.uniqueCode || `#${p.id}`)
            .join("   ·   ")}
        </div>
        {alarm.pallets.length > MAX_LISTED && (
          <div className="tt-alarm-more">+ {alarm.pallets.length - MAX_LISTED} more</div>
        )}
        <button className="tt-btn tt-alarm-btn" disabled={suppressing} onClick={() => void silence()}>
          {suppressing ? "Silencing…" : "🔕 Silence Alarm"}
        </button>
      </div>
    </div>
  );
}
