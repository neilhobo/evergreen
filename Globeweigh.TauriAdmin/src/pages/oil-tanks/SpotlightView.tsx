import { useState } from "react";
import { OilTank, litres, signalBars, tankStatus } from "../../api/oilTanks";
import { fmtDate, fmtDateTime } from "../../format";
import TankDrawing from "./TankDrawing";
import { STATUS_DOT, StatusChip, emptyDate } from "./parts";

// "Spotlight": one tank drawn large with its figures and its level over the last `days`;
// the tanks along the top switch which one.
export default function SpotlightView({ tanks, days }: { tanks: OilTank[]; days: number }) {
  const [selId, setSelId] = useState<number>(tanks[0].deviceId);
  const s = tanks.find((t) => t.deviceId === selId) ?? tanks[0];

  return (
    <div className="ot-scroll">
      <div className="ot-picks" role="tablist" aria-label="Tanks">
        {tanks.map((t) => (
          <button
            key={t.deviceId}
            type="button"
            role="tab"
            aria-selected={t.deviceId === s.deviceId}
            className={t.deviceId === s.deviceId ? "ot-pick ot-pick-on" : "ot-pick"}
            onClick={() => setSelId(t.deviceId)}
          >
            <TankDrawing tank={t} width={t.horizontalCylinder ? 64 : 48} label={false} />
            <span className="ot-pick-text">
              <span className="ot-pick-name">{t.name}</span>
              <span className="ot-muted">
                {t.supplyType ?? "—"} · {litres(t.volume)}
              </span>
            </span>
            <span className="ot-pick-pct">{t.percentage.toFixed(1)}%</span>
            <span className="ot-dot" style={{ background: STATUS_DOT[tankStatus(t)] }} aria-label={tankStatus(t)} />
          </button>
        ))}
      </div>

      <div className="ot-spot ot-grid-bg">
        <div className="ot-spot-tank">
          <div className="ot-spot-name">{s.name}</div>
          <StatusChip tank={s} />
          <TankDrawing tank={s} width={s.horizontalCylinder ? 400 : 340} scale={!s.horizontalCylinder} />
        </div>

        <div className="ot-spot-main">
          <div className="ot-stats">
            <div className="ot-stat">
              <div className="ot-stat-label">In the tank</div>
              <div className="ot-stat-value">{litres(s.volume)}</div>
              <div className="ot-muted">of {litres(s.maxCapacity)}</div>
            </div>
            <div className="ot-stat">
              <div className="ot-stat-label">Usage / day</div>
              <div className="ot-stat-value">{s.dayRate == null ? "—" : litres(s.dayRate)}</div>
              <div className="ot-muted">room for {litres(s.ullage)}</div>
            </div>
            <div className="ot-stat">
              <div className="ot-stat-label">Days remaining</div>
              <div className="ot-stat-value">{s.daysRemaining ?? "—"}</div>
              <div className="ot-muted">{s.daysRemaining == null ? " " : `empty ${emptyDate(s.daysRemaining)}`}</div>
            </div>
            <div className="ot-stat">
              <div className="ot-stat-label">Last delivery</div>
              <div className="ot-stat-value">{s.lastFill ? fmtDate(s.lastFill.at) : "—"}</div>
              <div className="ot-muted">{s.lastFill ? `+${litres(s.lastFill.litres)}` : "none in 90 days"}</div>
            </div>
          </div>

          <div className="ot-trend">
            <div className="ot-trend-head">
              <span className="ot-trend-title">Level — last {days} days</span>
              <span className="ot-muted">{s.readings.length} readings</span>
            </div>
            <Trend tank={s} days={days} />
          </div>

          <div className="ot-foot">
            <span>Sensor <strong>{s.deviceId}</strong></span>
            <span>Battery <strong>{s.battery == null ? "—" : `${s.battery}%`}</strong></span>
            <span>Signal <strong>{signalBars(s.csq) == null ? "—" : `${signalBars(s.csq)}/5`}</strong></span>
            <span>Last reading <strong>{fmtDateTime(s.lastReadingAt) || "—"}</strong></span>
          </div>
        </div>
      </div>
    </div>
  );
}

// The fill % over the window, with the amber and red levels. Drawn in a stretched viewBox
// (the strokes are non-scaling), so the axis labels are HTML around it.
function Trend({ tank: t, days }: { tank: OilTank; days: number }) {
  const end = Date.now();
  const start = end - days * 86_400_000;
  const W = 600, H = 200;
  const cap = t.maxCapacity && t.maxCapacity > 0 ? t.maxCapacity : null;
  const y = (pct: number) => H * (1 - Math.min(100, Math.max(0, pct)) / 100);
  const pts = cap
    ? t.readings
        .map((r) => ({ x: ((new Date(r.at).getTime() - start) / (end - start)) * W, y: y((r.volume / cap) * 100) }))
        .filter((p) => p.x >= 0 && p.x <= W)
    : [];
  const line = pts.map((p) => `${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(" ");
  const fmt = (ms: number) => new Date(ms).toLocaleDateString("en-GB", { day: "numeric", month: "short" });

  return (
    <div className="ot-chart">
      <div className="ot-chart-y">
        <span>100%</span>
        <span>50%</span>
        <span>0%</span>
      </div>
      <div className="ot-chart-plot">
        {pts.length < 2 ? (
          <div className="ot-chart-empty">No readings in the last {days} days.</div>
        ) : (
          <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" role="img" aria-label={`${t.name} level over the last ${days} days`}>
            {[25, 50, 75].map((p) => (
              <line key={p} x1="0" x2={W} y1={y(p)} y2={y(p)} stroke="#e9eef5" vectorEffect="non-scaling-stroke" />
            ))}
            {t.amberWarningLevel != null && (
              <line x1="0" x2={W} y1={y(t.amberWarningLevel)} y2={y(t.amberWarningLevel)} stroke="#b45309" strokeWidth="1.5" strokeDasharray="6 4" vectorEffect="non-scaling-stroke" />
            )}
            {t.redWarningLevel != null && (
              <line x1="0" x2={W} y1={y(t.redWarningLevel)} y2={y(t.redWarningLevel)} stroke="#b91c1c" strokeWidth="1.5" strokeDasharray="6 4" vectorEffect="non-scaling-stroke" />
            )}
            <polygon points={`${pts[0].x},${H} ${line} ${pts[pts.length - 1].x},${H}`} fill="#3d5170" opacity="0.14" />
            <polyline points={line} fill="none" stroke="#26354b" strokeWidth="2.5" strokeLinejoin="round" vectorEffect="non-scaling-stroke" />
          </svg>
        )}
        <div className="ot-chart-x">
          <span>{fmt(start)}</span>
          <span>{fmt((start + end) / 2)}</span>
          <span>Today</span>
        </div>
      </div>
    </div>
  );
}
