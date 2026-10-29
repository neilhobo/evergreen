// Weighscale transport via the Tauri shell. Two connection types, same byte stream:
//   serial — the indicator is plugged into THIS PC (production factory setup);
//   tcp    — the indicator is reached over the network (Moxa NPort in TCP-server mode, or
//            the dev weighscale-bridge.ps1 on a remote PC).
// Only works inside the app shell — a plain browser (server-hosted UI) has no serial/TCP
// access, and the API functions here say so.

export interface ComPort {
  name: string; // e.g. "COM3"
  description: string; // e.g. "USB Serial Port (FTDI)" — may be empty
}

// How to pull a weight out of an indicator frame, worked out interactively on the setup
// screen: frames are split into numeric fields and the user picks which one is the weight,
// plus how many implied decimal places it has (e.g. "1152" with 1 decimal → 115.2).
export interface ParseConfig {
  weightField: number; // 0-based index among the numeric fields of a frame
  decimals: number;
}

// On-request scales send nothing until asked: this command (+CR appended) is sent every
// interval, e.g. "q%". Absent = continuous-mode scale (nothing is ever sent to it).
export interface PollConfig {
  command: string;
  intervalMs: number;
}

export type WeighscaleConfig = (
  | { transport: "serial"; comPort: string; baud: number }
  | { transport: "tcp"; host: string; port: number }
) & { parse?: ParseConfig; poll?: PollConfig };

// Numeric fields of one raw frame (the tokens a ParseConfig.weightField indexes into).
export const frameFields = (frame: string): string[] =>
  Array.from(frame.matchAll(/-?\d+/g), (m) => m[0]);

// Apply a ParseConfig to a raw frame; null when the frame doesn't have that field.
export function parseWeight(frame: string, parse: ParseConfig): number | null {
  const raw = frameFields(frame)[parse.weightField];
  if (raw === undefined) return null;
  const n = parseInt(raw, 10);
  return Number.isFinite(n) ? n / Math.pow(10, parse.decimals) : null;
}

export const inTauriShell = () =>
  typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;

const notInShell = () =>
  new Error("The weighscale is only available inside the Globeweigh Touch app.");

export async function listComPorts(): Promise<ComPort[]> {
  if (!inTauriShell()) throw notInShell();
  const { invoke } = await import("@tauri-apps/api/core");
  return invoke<ComPort[]>("list_com_ports");
}

// A port's probe outcome: can it be opened, and is anything transmitting on it? Answers
// the "which of these 5 COM ports is the scale?" question without HyperTerminal.
export interface PortProbe {
  name: string;
  description: string;
  status: "streaming" | "silent" | "busy" | "error";
  detail: string | null; // sample of received data, or the open error
}

export async function probeComPorts(baud: number): Promise<PortProbe[]> {
  if (!inTauriShell()) throw notInShell();
  const { invoke } = await import("@tauri-apps/api/core");
  return invoke<PortProbe[]>("probe_com_ports", { baud });
}

export async function connectWeighscale(config: WeighscaleConfig): Promise<void> {
  if (!inTauriShell()) throw notInShell();
  const { invoke } = await import("@tauri-apps/api/core");
  await invoke("weighscale_connect", { config });
}

export async function disconnectWeighscale(): Promise<void> {
  if (!inTauriShell()) return;
  const { invoke } = await import("@tauri-apps/api/core");
  await invoke("weighscale_disconnect");
}

// Incoming bytes from the scale (utf8-lossy chunks, as received — no line framing yet).
export async function onWeighscaleData(cb: (text: string) => void): Promise<() => void> {
  if (!inTauriShell()) return () => {};
  const { listen } = await import("@tauri-apps/api/event");
  return listen<string>("weighscale://data", (e) => cb(e.payload));
}

export interface ScaleStatus {
  connected: boolean;
  error: string | null;
}

export async function onWeighscaleStatus(cb: (s: ScaleStatus) => void): Promise<() => void> {
  if (!inTauriShell()) return () => {};
  const { listen } = await import("@tauri-apps/api/event");
  return listen<ScaleStatus>("weighscale://status", (e) => cb(e.payload));
}

import { getStoredValue, setStoredValue } from "./deviceStore";

// The connection config is a property of the PC (which USB socket / which bridge host) and
// lives on this PC's Device row via deviceStore — localStorage was wiped by shell reinstalls.
export function getSavedWeighscaleConfig(): WeighscaleConfig | null {
  try {
    const raw = getStoredValue("weighscaleConfig");
    return raw ? (JSON.parse(raw) as WeighscaleConfig) : null;
  } catch {
    return null;
  }
}

export function saveWeighscaleConfig(config: WeighscaleConfig | null) {
  setStoredValue("weighscaleConfig", config ? JSON.stringify(config) : null);
}

// Short label for the Home-bar status pill, e.g. "COM5 @ 9600" or "7.220.201.200:4001".
export function describeWeighscale(config: WeighscaleConfig): string {
  return config.transport === "serial"
    ? `${config.comPort} @ ${config.baud}`
    : `${config.host}:${config.port}`;
}
