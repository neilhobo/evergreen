// Relay card via the Tauri shell — a Robot Electronics (Devantech) USB relay board
// (the client PC reports a USB-OPTO-RLY88: 8 relays + 8 opto-isolated inputs). It's
// USB-serial, so it appears as a COM port; the single-byte command protocol lives in
// the Rust side (lib.rs). Same transports as the weighscale: the card on THIS PC
// (serial) or reached over a network bridge (tcp).
//
// Same make as the gate/barrier relay that GlobeweighService's RelayWorker drives, but
// that one is an ETH-series card with a different (TCP, 0x10/0x20) command set.

import { inTauriShell } from "./serial";
import { getStoredValue, setStoredValue } from "./deviceStore";

export type RelayConfig =
  | { transport: "serial"; comPort: string; baud: number }
  | { transport: "tcp"; host: string; port: number };

const notInShell = () =>
  new Error("The relay card is only available inside the Globeweigh Touch app.");

// A port's relay-probe outcome: "found" means it answered the GET_VERSION handshake and
// IS the relay card (model identified from the module id byte, as the Robot Electronics
// test app does). "streaming" ports transmit unsolicited (a scale) so are ruled out.
export interface RelayProbe {
  name: string;
  description: string;
  status: "found" | "silent" | "streaming" | "busy" | "error";
  model: string | null; // e.g. "USB-OPTO-RLY88", when found
  detail: string | null; // firmware version, or the open error
}

export async function probeRelayPorts(): Promise<RelayProbe[]> {
  if (!inTauriShell()) throw notInShell();
  const { invoke } = await import("@tauri-apps/api/core");
  return invoke<RelayProbe[]>("probe_relay_ports");
}

// Connect + handshake; resolves to the model name, rejects if nothing answers.
export async function relayConnect(config: RelayConfig): Promise<string> {
  if (!inTauriShell()) throw notInShell();
  const { invoke } = await import("@tauri-apps/api/core");
  return invoke<string>("relay_connect", { config });
}

export async function relayDisconnect(): Promise<void> {
  if (!inTauriShell()) return;
  const { invoke } = await import("@tauri-apps/api/core");
  await invoke("relay_disconnect");
}

// Switch one relay (1-8) on/off. No ack from the card — the next state event reflects it.
export async function relaySet(relay: number, on: boolean): Promise<void> {
  if (!inTauriShell()) throw notInShell();
  const { invoke } = await import("@tauri-apps/api/core");
  await invoke("relay_set", { relay, on });
}

// Emitted ~every 300ms while connected: the relay and input bitmaps (bit 0 = number 1).
export interface RelayCardStatus {
  connected: boolean;
  relays: number;
  inputs: number;
  model: string | null;
  error: string | null;
}

export async function onRelayState(cb: (s: RelayCardStatus) => void): Promise<() => void> {
  if (!inTauriShell()) return () => {};
  const { listen } = await import("@tauri-apps/api/event");
  return listen<RelayCardStatus>("relay://state", (e) => cb(e.payload));
}

// Like the weighscale config, this is a property of the PC (which USB socket / which host)
// and lives on this PC's Device row via deviceStore — localStorage was wiped by reinstalls.
export function getSavedRelayConfig(): RelayConfig | null {
  try {
    const raw = getStoredValue("relayConfig");
    return raw ? (JSON.parse(raw) as RelayConfig) : null;
  } catch {
    return null;
  }
}

export function saveRelayConfig(config: RelayConfig | null) {
  setStoredValue("relayConfig", config ? JSON.stringify(config) : null);
}

// Short label for the Home-bar status pill, e.g. "COM3 @ 19200" or "192.168.250.60:4196".
export function describeRelay(config: RelayConfig): string {
  return config.transport === "serial"
    ? `${config.comPort} @ ${config.baud}`
    : `${config.host}:${config.port}`;
}
