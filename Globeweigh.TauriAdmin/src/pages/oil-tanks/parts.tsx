import { OilTank, TankStatus, tankStatus } from "../../api/oilTanks";

// Bits both Oil Tanks views share.

export const STATUS_DOT: Record<TankStatus, string> = { Green: "#16a34a", Amber: "#d97706", Red: "#dc2626" };

export function StatusChip({ tank }: { tank: OilTank }) {
  const s = tankStatus(tank);
  return <span className={`ot-chip ot-chip-${s.toLowerCase()}`}>{s}</span>;
}

// The day the tank runs dry at today's usage, as "Sun 26 Oct".
export function emptyDate(daysRemaining: number): string {
  const d = new Date();
  d.setDate(d.getDate() + daysRemaining);
  return d.toLocaleDateString("en-GB", { weekday: "short", day: "numeric", month: "short" });
}
