import { apiGet, apiPut } from "./client";

// Oil Tanks — the site's Specgauge oil-tank sensors, as the Specgauge API returns them
// (GET api/public/depots/{depot}/tanks), passed through by OilTanksController.
// Volumes are litres, heights cm; dates are UK local time with no zone.
export interface OilTankReading { at: string; volume: number; }
export interface OilTankFill { at: string; litres: number; }
export interface OilTank {
  deviceId: number;
  name: string | null;
  supplyType: string | null;
  percentage: number;
  volume: number;
  maxCapacity: number | null;
  ullage: number | null;
  dayRate: number | null;
  daysRemaining: number | null;
  lastReadingAt: string | null;
  battery: number | null;
  csq: number | null;
  redWarningLevel: number | null;
  amberWarningLevel: number | null;
  warningsOnHighLevel: boolean;
  horizontalCylinder: boolean;
  tankType: string | null;
  model: string | null;
  manufacturer: string | null;
  heightCm: number | null;
  levelCm: number | null;
  density: number | null;
  bunded: boolean | null;
  singleSkin: boolean | null;
  lastFill: OilTankFill | null;
  readings: OilTankReading[];
}
export interface OilTanksResponse { client: string; depot: string; days: number; tanks: OilTank[]; }

export const getOilTanks = (days = 14) => apiGet<OilTanksResponse>(`/oil-tanks?days=${days}`);

// Who sees the page: chosen user by user in Super Admin > Oil Tanks (the super-admin always does).
export interface OilTanksAccessRow {
  id: number;
  firstName: string | null;
  surname: string | null;
  username: string | null;
  disabled: boolean;
  canView: boolean;
}

/** Whether the signed-in user is offered the page in the menu. */
export const getOilTanksAccessMe = () => apiGet<{ canView: boolean }>("/oil-tanks/access/me");
/** Super-admin only: every admin-app user and whether they see the page. */
export const getOilTanksAccess = () => apiGet<OilTanksAccessRow[]>("/oil-tanks/access");
/** Super-admin only: switch one user; returns the whole list back. */
export const setOilTanksAccess = (userId: number, enabled: boolean) =>
  apiPut<OilTanksAccessRow[]>(`/oil-tanks/access/${userId}`, { enabled });

export type TankStatus = "Green" | "Amber" | "Red";

// Red/Amber/Green from the fill % against the tank's own warning levels — the same rule
// as Specgauge's device detail (logRag): collection tanks (warningsOnHighLevel) warn on a
// HIGH level instead, and a tank with no levels set falls back to 40 / 75.
export function tankStatus(t: OilTank): TankStatus {
  const p = t.percentage;
  const red = t.redWarningLevel, amber = t.amberWarningLevel;
  if (red != null || amber != null) {
    if (t.warningsOnHighLevel) {
      if (red != null && p >= red) return "Red";
      if (amber != null && p >= amber) return "Amber";
      return "Green";
    }
    if (red != null && p <= red) return "Red";
    if (amber != null && p <= amber) return "Amber";
    return "Green";
  }
  if (p >= 75) return "Green";
  if (p >= 40) return "Amber";
  return "Red";
}

// How far up a horizontal cylinder (0..1 of its diameter) the oil stands when the tank is
// `volFrac` full by volume: the segment area (θ − sin θ)/2π solved for θ by bisection.
// A vertical tank's depth is its volume fraction, so it never needs this.
export function horizontalDepthFrac(volFrac: number): number {
  const v = Math.min(1, Math.max(0, volFrac));
  let lo = 0, hi = 2 * Math.PI;
  for (let i = 0; i < 40; i++) {
    const mid = (lo + hi) / 2;
    if ((mid - Math.sin(mid)) / (2 * Math.PI) < v) lo = mid; else hi = mid;
  }
  const theta = (lo + hi) / 2;
  return (1 - Math.cos(theta / 2)) / 2;
}

// The drawing's oil height for a fill %, by the tank's shape.
export function depthFrac(t: OilTank, pct: number): number {
  const v = Math.min(100, Math.max(0, pct)) / 100;
  return t.horizontalCylinder ? horizontalDepthFrac(v) : v;
}

// CSQ (0–31, 99 = unknown) as 0–5 bars.
export function signalBars(csq: number | null): number | null {
  if (csq == null || csq === 99) return null;
  if (csq >= 20) return 5;
  if (csq >= 15) return 4;
  if (csq >= 10) return 3;
  if (csq >= 5) return 2;
  return csq > 0 ? 1 : 0;
}

export const litres = (n: number | null | undefined) =>
  n == null ? "—" : `${Math.round(n).toLocaleString("en-GB")} L`;

export const metres = (cm: number | null | undefined) =>
  cm == null ? "—" : `${(cm / 100).toFixed(2)} m`;
