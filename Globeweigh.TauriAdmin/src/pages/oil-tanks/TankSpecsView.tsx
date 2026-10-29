import { OilTank, litres, metres, signalBars } from "../../api/oilTanks";
import { fmtDateTime } from "../../format";
import TankDrawing from "./TankDrawing";
import { StatusChip, emptyDate } from "./parts";

// "Tank specs" (the default view): every tank drawn side by side as its real shape with
// its dimensions on it, and one table underneath comparing them spec by spec.
export default function TankSpecsView({ tanks }: { tanks: OilTank[] }) {
  const rows: { label: string; value: (t: OilTank) => string; strong?: boolean }[] = [
    { label: "Shape", value: (t) => (t.horizontalCylinder ? "Horizontal cylinder" : "Vertical cylinder") },
    { label: "Height / diameter", value: (t) => metres(t.heightCm) },
    { label: "Oil depth", value: (t) => metres(t.levelCm) },
    { label: "Capacity", value: (t) => litres(t.maxCapacity) },
    { label: "Space to fill", value: (t) => litres(t.ullage) },
    { label: "Usage / day", value: (t) => (t.dayRate == null ? "—" : litres(t.dayRate)) },
    {
      label: "Days remaining",
      value: (t) => (t.daysRemaining == null ? "—" : `${t.daysRemaining} (empty ${emptyDate(t.daysRemaining)})`),
    },
    { label: "Product density", value: (t) => (t.density == null ? "—" : `${t.density.toFixed(2)} kg/L`) },
    { label: "Battery", value: (t) => (t.battery == null ? "—" : `${t.battery}%`) },
    {
      label: "Signal",
      value: (t) => {
        const b = signalBars(t.csq);
        return b == null ? "—" : `${b}/5`;
      },
    },
    { label: "Last reading", value: (t) => fmtDateTime(t.lastReadingAt) || "—" },
  ];

  return (
    <div className="ot-scroll">
      <div className="ot-farm">
        {tanks.map((t) => (
          <div key={t.deviceId} className="ot-farm-col">
            <div className="ot-col-head">
              <span className="ot-tank-name">{t.name}</span>
              <span className="ot-muted">
                {t.horizontalCylinder ? "Horizontal" : "Vertical"} · {t.supplyType ?? "—"}
              </span>
              <StatusChip tank={t} />
            </div>
            <div className="ot-drawing">
              <TankDrawing tank={t} width={t.horizontalCylinder ? 320 : 280} scale={false} dims />
            </div>
            <div className="ot-volume">
              <span className="ot-volume-big">{litres(t.volume)}</span>
              <span className="ot-muted">
                of {litres(t.maxCapacity)} · {t.percentage.toFixed(1)}%
              </span>
            </div>
          </div>
        ))}
      </div>

      <div className="ot-table-wrap">
        <table className="ot-specs">
          <thead>
            <tr>
              <th />
              {tanks.map((t) => (
                <th key={t.deviceId}>{t.name}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.label} className={r.strong ? "ot-strong" : undefined}>
                <td className="ot-spec-label">{r.label}</td>
                {tanks.map((t) => (
                  <td key={t.deviceId}>{r.value(t)}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
        <div className="ot-legend">
          <span><i className="ot-dash ot-dash-amber" /> Amber level</span>
          <span><i className="ot-dash ot-dash-red" /> Red level</span>
          <span><i className="ot-sensor" /> Specgauge sensor, with its gap to the oil</span>
        </div>
      </div>
    </div>
  );
}
