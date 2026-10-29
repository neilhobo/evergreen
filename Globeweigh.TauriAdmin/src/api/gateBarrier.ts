import { apiGet, apiPost } from "./client";

export interface Camera {
  name: string;
  ip: string;
  stream: string;
}

// A remote touchscreen exposed via a websockify proxy (ws → VNC). Embedded with noVNC.
export interface RemoteScreen {
  name: string;
  wsUrl: string;
  // This screen's lane camera (go2rtc stream, e.g. the weigh-in intercom), shown first by
  // "Show Camera". Absent = the dialog shows the first Gate/Barrier camera.
  stream?: string | null;
  cameraName?: string | null;
}

export interface GateBarrierConfig {
  gateEnabled: boolean;
  gateRelayIpAddress: string | null;
  gateRelayPortNo: number;
  videoBaseUrl: string | null;
  cameras: Camera[];
  remoteScreens: RemoteScreen[];
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
  // Outage window reported by GlobeweighService: how long the bridge has been sending
  // nothing. -1 while weights are arriving normally. Older service builds omit both.
  dataLostSinceUtc?: string;
  dataLostMs?: number;
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

// Second weighbridge (Moxa 192.168.250.148, GlobeweighService /weighbridge2/weight).
export async function getLiveWeight2(): Promise<WeightReading | null> {
  try {
    return await apiGet<WeightReading>("/gate-barrier/weight2");
  } catch {
    return null;
  }
}
