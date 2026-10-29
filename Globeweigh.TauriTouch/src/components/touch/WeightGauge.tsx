import { useEffect, useRef, useState } from "react";
import { getLiveWeight } from "../../api/touch";

interface Props {
  // Called on every poll with the latest reading so a parent weigh action can read it.
  onChange?: (weight: number | null, healthy: boolean) => void;
  pollMs?: number;
}

// Live weight read-out (green 7-segment style), polling the API's gate-barrier weight proxy —
// the same GlobeweighService source the WPF ScaleService uses. Marks itself stale/unhealthy when
// the service is unreachable or the reading is old, so the weigh button can be disabled.
export default function WeightGauge({ onChange, pollMs = 500 }: Props) {
  const [weight, setWeight] = useState<number | null>(null);
  const [healthy, setHealthy] = useState(false);
  const cb = useRef(onChange);
  cb.current = onChange;

  useEffect(() => {
    let cancelled = false;
    async function poll() {
      try {
        const r = await getLiveWeight();
        if (cancelled) return;
        // weight arrives as a string ("-40") from GlobeweighService — parse it.
        const num =
          typeof r.weight === "number"
            ? r.weight
            : r.weight != null && String(r.weight).trim() !== ""
              ? parseFloat(String(r.weight))
              : NaN;
        const w = Number.isFinite(num) ? Math.round(num) : null;
        const ok = !!r.isConnected && w !== null && (r.ageMs == null || r.ageMs < 5000);
        setWeight(w);
        setHealthy(ok);
        cb.current?.(w, ok);
      } catch {
        if (cancelled) return;
        setHealthy(false);
        cb.current?.(null, false);
      }
    }
    void poll();
    const t = window.setInterval(poll, pollMs);
    return () => {
      cancelled = true;
      window.clearInterval(t);
    };
  }, [pollMs]);

  return (
    <div className={`tt-gauge${healthy ? "" : " tt-stale"}`}>
      <div className="tt-gauge-val">
        {weight === null ? "----" : weight}
        <span className="tt-gauge-unit"> kg</span>
      </div>
      <div className={`tt-gauge-status${healthy ? "" : " tt-bad"}`}>
        {healthy ? "Live weight" : "Scale not live — please wait"}
      </div>
    </div>
  );
}
