import { useId } from "react";
import { OilTank, depthFrac, metres } from "../../api/oilTanks";

interface Props {
  tank: OilTank;
  /** Rendered width in px; the height follows the drawing's own proportions. */
  width: number;
  /** The 0–100 scale down the left (vertical tanks). */
  scale?: boolean;
  /** Height/diameter, oil depth and the sensor-to-oil gap. */
  dims?: boolean;
  /** The fill % written on the oil. */
  label?: boolean;
}

// A Specgauge tank drawn in 3D as its real shape: an upright cylinder on legs, or a
// horizontal one on saddles (Tank.TankType.HorizontalCylinder). The oil stands at its true
// depth (a horizontal tank's depth is not its fill %), with the tank's amber and red
// warning levels as dashed lines and the sensor on top.
export default function TankDrawing(props: Props) {
  return props.tank.horizontalCylinder ? <HorizontalTank {...props} /> : <VerticalTank {...props} />;
}

const pctText = (t: OilTank) => `${t.percentage.toFixed(1)}%`;
const aria = (t: OilTank) =>
  `${t.name ?? "Tank"}, ${t.horizontalCylinder ? "horizontal" : "vertical"} tank, ${pctText(t)} full`;

// The sensor-to-oil gap in cm, when both the tank height and the level are known.
function gapCm(t: OilTank): number | null {
  if (t.heightCm == null || t.levelCm == null) return null;
  return Math.max(0, Math.round(t.heightCm - t.levelCm));
}

function warningLevels(t: OilTank): { pct: number; color: string }[] {
  const out: { pct: number; color: string }[] = [];
  if (t.amberWarningLevel != null) out.push({ pct: t.amberWarningLevel, color: "#b45309" });
  if (t.redWarningLevel != null) out.push({ pct: t.redWarningLevel, color: "#b91c1c" });
  return out;
}

// Body spans y 40 (top) to 250 (bottom) in a 280 x 310 box.
function VerticalTank({ tank: t, width, scale, dims, label = true }: Props) {
  const id = useId().replace(/:/g, "");
  const top = 40, bottom = 250, h = bottom - top;
  const yAt = (frac: number) => top + h * (1 - frac);
  const fillY = yAt(depthFrac(t, t.percentage));
  const gap = gapCm(t);
  return (
    <svg viewBox="0 0 280 310" width={width} height={(width * 310) / 280} role="img" aria-label={aria(t)}>
      <defs>
        <linearGradient id={`shell${id}`} x1="0" x2="1"><stop offset="0" stopColor="#cfd8e3" /><stop offset="0.45" stopColor="#f8fafc" /><stop offset="1" stopColor="#b8c4d3" /></linearGradient>
        <linearGradient id={`oil${id}`} x1="0" x2="1"><stop offset="0" stopColor="#26354b" /><stop offset="0.45" stopColor="#3d5170" /><stop offset="1" stopColor="#1e2a3c" /></linearGradient>
      </defs>
      {scale && (
        <g fill="#64748b" fontSize="10" textAnchor="end">
          {[100, 75, 50, 25, 0].map((p) => (
            <g key={p}>
              <text x="34" y={yAt(p / 100) + 4}>{p}</text>
              <line x1="38" x2="44" y1={yAt(p / 100)} y2={yAt(p / 100)} stroke="#94a3b8" />
            </g>
          ))}
        </g>
      )}
      <path d="M70 288 L64 304 M170 288 L176 304" stroke="#64748b" strokeWidth="5" strokeLinecap="round" />
      <rect x="50" y={top} width="140" height={h} fill={`url(#shell${id})`} stroke="#64748b" strokeWidth="2" />
      <ellipse cx="120" cy={bottom} rx="70" ry="14" fill="#b8c4d3" stroke="#64748b" strokeWidth="2" />
      <rect x="51" y={fillY} width="138" height={bottom - fillY} fill={`url(#oil${id})`} />
      <ellipse cx="120" cy={bottom} rx="69" ry="13" fill="#1e2a3c" />
      <ellipse cx="120" cy={fillY} rx="69" ry="13" fill="#53698c" />
      <rect x="50" y={top} width="140" height={h} fill="none" stroke="#64748b" strokeWidth="2" />
      <ellipse cx="120" cy={top} rx="70" ry="14" fill="#e2e8f0" stroke="#64748b" strokeWidth="2" />
      <rect x="108" y="20" width="24" height="16" rx="3" fill="#16a34a" stroke="#14532d" strokeWidth="1.5" />
      <line x1="120" x2="120" y1="36" y2={fillY} stroke="#16a34a" strokeWidth="1.5" strokeDasharray="3 4" />
      {warningLevels(t).map((w) => (
        <line key={w.color} x1="46" x2="194" y1={yAt(depthFrac(t, w.pct))} y2={yAt(depthFrac(t, w.pct))}
          stroke={w.color} strokeWidth="1.5" strokeDasharray="6 4" />
      ))}
      {label && (
        <text x="120" y={Math.min(bottom - 14, fillY + 40)} textAnchor="middle" fontSize="22" fontWeight="700" fill="#ffffff">
          {pctText(t)}
        </text>
      )}
      {dims && (
        <>
          {gap != null && fillY - 13 - 36 > 24 && (
            <text x="128" y={(36 + fillY - 13) / 2 + 4} fontSize="11" fontWeight="600" fill="#15803d">{gap} cm</text>
          )}
          <g stroke="#475569" strokeWidth="1.2" fill="none">
            <line x1="214" x2="214" y1={top} y2={bottom} />
            <path d={`M209 ${top + 6} L214 ${top} L219 ${top + 6} M209 ${bottom - 6} L214 ${bottom} L219 ${bottom - 6}`} />
            <line x1="206" x2="222" y1={top} y2={top} />
            <line x1="206" x2="222" y1={bottom} y2={bottom} />
          </g>
          <text x="226" y={(top + bottom) / 2 + 4} fontSize="12" fontWeight="600" fill="#334155">{metres(t.heightCm)}</text>
        </>
      )}
    </svg>
  );
}

// Body spans y 50 (top) to 210 (bottom) in a 320 x 260 box; the left end is the face.
function HorizontalTank({ tank: t, width, dims, label = true }: Props) {
  const id = useId().replace(/:/g, "");
  const top = 50, bottom = 210, h = bottom - top;
  const yAt = (frac: number) => top + h * (1 - frac);
  const fillY = yAt(depthFrac(t, t.percentage));
  const gap = gapCm(t);
  return (
    <svg viewBox="0 0 320 260" width={width} height={(width * 260) / 320} role="img" aria-label={aria(t)}>
      <defs>
        <linearGradient id={`shell${id}`} x1="0" x2="0" y1="0" y2="1"><stop offset="0" stopColor="#cfd8e3" /><stop offset="0.4" stopColor="#f8fafc" /><stop offset="1" stopColor="#b8c4d3" /></linearGradient>
        <clipPath id={`body${id}`}><rect x="70" y={top} width="200" height={h} /><ellipse cx="270" cy="130" rx="24" ry="80" /></clipPath>
        <clipPath id={`face${id}`}><ellipse cx="70" cy="130" rx="24" ry="80" /></clipPath>
      </defs>
      <path d="M110 205 L100 240 L140 240 L130 205 M230 205 L220 240 L260 240 L250 205" fill="#94a3b8" stroke="#64748b" strokeWidth="2" />
      <rect x="70" y={top} width="200" height={h} fill={`url(#shell${id})`} />
      <ellipse cx="270" cy="130" rx="24" ry="80" fill="#b8c4d3" />
      <rect x="40" y={fillY} width="260" height={bottom + 5 - fillY} fill="#3d5170" clipPath={`url(#body${id})`} />
      <path d={`M70 ${top} L270 ${top} A24 80 0 0 1 270 ${bottom} L70 ${bottom}`} fill="none" stroke="#64748b" strokeWidth="2" />
      <ellipse cx="70" cy="130" rx="24" ry="80" fill="#e2e8f0" stroke="#64748b" strokeWidth="2" />
      <rect x="40" y={fillY} width="60" height={bottom + 5 - fillY} fill="#53698c" clipPath={`url(#face${id})`} />
      <ellipse cx="70" cy="130" rx="24" ry="80" fill="none" stroke="#64748b" strokeWidth="2" />
      {warningLevels(t).map((w) => (
        <line key={w.color} x1="46" x2="296" y1={yAt(depthFrac(t, w.pct))} y2={yAt(depthFrac(t, w.pct))}
          stroke={w.color} strokeWidth="1.5" strokeDasharray="6 4" />
      ))}
      <rect x="158" y="32" width="24" height="16" rx="3" fill="#16a34a" stroke="#14532d" strokeWidth="1.5" />
      <line x1="170" x2="170" y1="48" y2={fillY} stroke="#16a34a" strokeWidth="1.5" strokeDasharray="3 4" />
      {label && (
        <text x="170" y={Math.min(bottom - 8, fillY + 34)} textAnchor="middle" fontSize="20" fontWeight="700" fill="#ffffff">
          {pctText(t)}
        </text>
      )}
      {dims && (
        <>
          {gap != null && fillY - 48 > 24 && (
            <text x="178" y={(48 + fillY) / 2 + 4} fontSize="11" fontWeight="600" fill="#15803d">{gap} cm</text>
          )}
          <g stroke="#475569" strokeWidth="1.2" fill="none">
            <line x1="306" x2="306" y1={top} y2={bottom} />
            <path d={`M301 ${top + 6} L306 ${top} L311 ${top + 6} M301 ${bottom - 6} L306 ${bottom} L311 ${bottom - 6}`} />
          </g>
          <text x="300" y="30" textAnchor="end" fontSize="12" fontWeight="600" fill="#334155">⌀ {metres(t.heightCm)}</text>
        </>
      )}
    </svg>
  );
}
