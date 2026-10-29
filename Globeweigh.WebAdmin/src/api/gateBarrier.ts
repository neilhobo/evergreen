import { apiGet, apiPost } from "./client";

export interface Camera {
  name: string;
  ip: string;
  stream: string;
}

export interface GateBarrierConfig {
  gateEnabled: boolean;
  gateRelayIpAddress: string | null;
  gateRelayPortNo: number;
  videoBaseUrl: string | null;
  cameras: Camera[];
}

export interface RelayState {
  connected: boolean;
  gateOpen: boolean;
  barrierOpen: boolean;
}

export const getConfig = () => apiGet<GateBarrierConfig>("/gate-barrier/config");
export const getState = () => apiGet<RelayState>("/gate-barrier/state");
export const setGate = (open: boolean) => apiPost<RelayState>("/gate-barrier/gate", { open });
export const setBarrier = (open: boolean) => apiPost<RelayState>("/gate-barrier/barrier", { open });

export interface WeightReading {
  weight: string;
  ageMs: number;
  isConnected: boolean;
}

// Live weight is proxied by the API from the production GlobeweighService (:8765 over
// Tailscale) — always the prod source, same as the desktop Gate/Barrier view.
export async function getLiveWeight(): Promise<WeightReading | null> {
  try {
    return await apiGet<WeightReading>("/gate-barrier/weight");
  } catch {
    return null;
  }
}
