// Per-PC hardware setup (weighscale / relay / printer), backed by this PC's dbo.Device row
// instead of WebView localStorage — a shell reinstall wiped localStorage and lost the lot.
// The app seeds this in-memory cache from the device row at startup (initDeviceStore), the
// existing get/save helpers in serial.ts / relay.ts / printer.ts read and write the cache,
// and every write persists to the API in the background.
//
// One-time migration: values still sitting in localStorage (the old storage) are adopted
// when the DB field is empty, pushed up, and the local copies removed on success — so an
// already-configured PC keeps its setup without retyping anything.
import { apiPut } from "./client";
import type { DeviceConfig } from "./touch";

interface HardwareState {
  weighscaleConfig: string | null; // JSON, WeighscaleConfig shape (serial.ts)
  relayConfig: string | null; // JSON, RelayConfig shape (relay.ts)
  printerName: string | null; // Windows printer name
}

// The legacy localStorage keys, matching what the modules used to write.
const LEGACY_KEYS: Record<keyof HardwareState, string> = {
  weighscaleConfig: "weighscaleConfig",
  relayConfig: "relayConfig",
  printerName: "printerName",
};

let deviceId: number | null = null;
const state: HardwareState = { weighscaleConfig: null, relayConfig: null, printerName: null };

export function initDeviceStore(device: DeviceConfig): void {
  deviceId = device.id;
  state.weighscaleConfig = device.weighscaleConfig;
  state.relayConfig = device.relayConfig;
  state.printerName = device.printerName;

  let migrated = false;
  (Object.keys(LEGACY_KEYS) as (keyof HardwareState)[]).forEach((k) => {
    try {
      const local = localStorage.getItem(LEGACY_KEYS[k]);
      if (local && !state[k]) {
        state[k] = local;
        migrated = true;
      }
    } catch {
      // storage unavailable — nothing to migrate
    }
  });
  if (migrated) void persist();
}

export function getStoredValue(key: keyof HardwareState): string | null {
  return state[key];
}

export function setStoredValue(key: keyof HardwareState, value: string | null): void {
  state[key] = value;
  void persist();
}

async function persist(): Promise<void> {
  if (deviceId === null) return; // store not seeded yet (shouldn't happen post-boot)
  try {
    await apiPut(`/device/${deviceId}/hardware`, {
      weighscaleConfig: state.weighscaleConfig,
      relayConfig: state.relayConfig,
      printerName: state.printerName,
    });
    // DB write confirmed — the legacy local copies are no longer the source of truth.
    Object.values(LEGACY_KEYS).forEach((k) => {
      try {
        localStorage.removeItem(k);
      } catch {
        // ignore storage failures
      }
    });
  } catch (err) {
    // Keep the cache (the screen keeps working); the next save retries the write.
    console.error("Saving hardware config to the device row failed:", err);
  }
}
