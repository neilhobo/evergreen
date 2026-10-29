// Mobile-device deployment API — backs the dev-only Devices page. Talks to the
// Globeweigh.Api DeviceManagementController (route /api/devices). Android Picking
// devices register themselves; the admin uploads release APKs and pushes them.
import { apiGet, apiPost, apiUpload, getToken } from "./client";
import { getApiBaseUrl } from "../config";

export interface DeviceRow {
  id: number;
  deviceName: string | null;
  applicationName: string | null;
  platform: string | null;
  currentVersion: string | null;
  // The Shorebird patch the app is running on top of currentVersion. Null on the bare
  // release, or from an app too old to report it — a patch never changes the version.
  currentPatch: number | null;
  ipAddress: string | null;
  lastSeen: string | null;
  recycleRequested: boolean;
  latestPushStatus: string | null;
  latestPushDetail: string | null;
  latestPushJobId: number;
  latestPushRowId: number;
  // Remote-control (scrcpy / wireless-ADB) setup for the detail panel.
  remoteHost: string | null;
  remotePort: number | null;
  remoteNotes: string | null;
}

export interface DeviceRemote {
  remoteHost: string | null;
  remotePort: number | null;
  remoteNotes: string | null;
}

export interface PackageInfo {
  fileName: string;
  version: string | null;
  sizeBytes: number;
  uploadedUtc: string;
}

export interface PushJobDevice {
  rowId: number;
  deviceId: number | null;
  deviceName: string | null;
  status: string;
  detail: string | null;
  updatedUtc: string;
}

export interface PushJob {
  id: number;
  version: string | null;
  packageFileName: string | null;
  status: string;
  createdUtc: string;
  completedUtc: string | null;
  devices: PushJobDevice[];
}

// Every device on the system (touch screens, factory PCs, tablets) — backs the
// client-facing Devices page. The filtered DeviceRow list stays for Device Manager.
export interface SystemDevice {
  id: number;
  deviceName: string | null;
  applicationName: string | null;
  currentVersion: string | null;
  ipAddress: string | null;
  lastSeen: string | null;
  hidden: boolean;
}

export const getAllDevices = (includeHidden = false) =>
  apiGet<SystemDevice[]>(`/devices/all${includeHidden ? "?includeHidden=true" : ""}`);

// Dev-mode Devices-overview tools: hide/unhide a device from the client-facing list.
export const setDeviceHidden = (deviceId: number, hidden: boolean) =>
  apiPost<void>(`/devices/${deviceId}/hidden`, { hidden });

export const getDevices = () => apiGet<DeviceRow[]>("/devices");
export const deleteDevices = (deviceIds: number[]) => apiPost<void>("/devices/delete", deviceIds);

// Persist a device's remote-control (VNC) setup — host/port/notes.
export const saveDeviceRemote = (deviceId: number, body: DeviceRemote) =>
  apiPost<void>(`/devices/${deviceId}/remote`, body);

// WebSocket URL for the API's per-device VNC bridge (embedded noVNC viewer).
// The API server does the TCP hop to the device, so the browser only needs the API.
export const deviceVncWsUrl = (deviceId: number) =>
  `${getApiBaseUrl().replace(/^http/, "ws")}/api/devices/${deviceId}/vnc` +
  `?access_token=${encodeURIComponent(getToken() ?? "")}`;

// Manually add a device by name + IP (one that has never run the app / self-registered).
export const addManualDevice = (deviceName: string, ipAddress: string) =>
  apiPost<DeviceRow>("/devices/manual", { deviceName, ipAddress });

// ICMP reachability of each device's last-known IP, keyed by device id. Polled by the
// grids. Default scope = mobile devices (Device Manager); all=true = every non-admin
// device (the client-facing Devices overview).
export const pingDevices = (all = false) =>
  apiGet<Record<number, boolean>>(`/devices/ping${all ? "?all=true" : ""}`);

export const getPackages = () => apiGet<PackageInfo[]>("/devices/packages");
export const uploadPackage = (fileName: string, bytes: Uint8Array) =>
  apiUpload<PackageInfo>(`/devices/packages?fileName=${encodeURIComponent(fileName)}`, bytes);

export const pushPackage = (packageFileName: string, version: string | null, deviceIds: number[]) =>
  apiPost<{ jobId: number }>("/devices/push", { packageFileName, version, deviceIds });

export const getPushJob = (jobId: number) => apiGet<PushJob>(`/devices/push/${jobId}`);
export const cancelPushJob = (jobId: number) => apiPost<void>(`/devices/push/${jobId}/cancel`, {});
export const cancelDevicePush = (rowId: number) =>
  apiPost<void>(`/devices/push-device/${rowId}/cancel`, {});
