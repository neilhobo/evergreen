import { apiDownload, apiGet, apiPost } from "./client";

export interface IntercomRing {
  id: number;
  unit: string;   // which intercom rang, e.g. "Bridge 1 – In" (blank from older APIs)
  stream: string; // its go2rtc stream
  callId: string | null;
  startedUtc: string;
  endedUtc: string | null;
  answeredBy: string | null;
  answeredUtc: string | null;
  hasSnapshot: boolean;
}

export interface IntercomEvent {
  atUtc: string;
  unit: string;
  code: string;
  action: string;
  callId: string | null;
  userId: string | null;
}

export interface IntercomUnit {
  name: string;
  host: string;
  stream: string;
  videoBaseUrl: string | null; // go2rtc serving this unit's camera
  listening?: boolean;         // false = video only: its bell events are not monitored by this API (absent from older APIs)
  connected: boolean;
  lastError: string | null;
  lastHeardUtc: string | null;
  ringing: boolean;
  activeRing: IntercomRing | null;
}

export interface IntercomStatus {
  enabled: boolean;
  units: IntercomUnit[];
  host: string | null;
  connected: boolean;
  lastError: string | null;
  lastHeardUtc: string | null;
  stream: string;              // go2rtc stream name
  videoBaseUrl: string | null; // go2rtc base; null = derive from the API host
  ringing: boolean;
  activeRing: IntercomRing | null;
  rings: IntercomRing[];
  events: IntercomEvent[];
}

export const getIntercomStatus = () => apiGet<IntercomStatus>("/intercom/status");
// The still the API took the moment the bell was pressed.
export const getRingSnapshot = (ringId: number) => apiDownload(`/intercom/rings/${ringId}/snapshot`);

// The call in progress (ring null = none; ring.answeredBy set = someone has it). Every admin
// app polls this for the incoming-call overlay.
export interface IntercomCall {
  enabled: boolean;
  ring: IntercomRing | null;
  stream: string;
  videoBaseUrl: string | null;
}

export const getIntercomCall = () => apiGet<IntercomCall>("/intercom/call");
// First to answer wins; anyone after gets a 409 ApiError whose message names who has it.
export const answerRing = (ringId: number) => apiPost<IntercomRing>(`/intercom/rings/${ringId}/answer`, {});
export const hangupRing = (ringId: number) => apiPost<void>(`/intercom/rings/${ringId}/hangup`, {});
