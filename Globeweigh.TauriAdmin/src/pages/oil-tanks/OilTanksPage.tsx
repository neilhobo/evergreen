import { useCallback, useEffect, useState } from "react";
import { AdminUserDto } from "../../types";
import { getOilTanks, OilTanksResponse } from "../../api/oilTanks";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import PageTabs, { PageTab } from "../../widgets/PageTabs";
import TankSpecsView from "./TankSpecsView";
import SpotlightView from "./SpotlightView";
import "./OilTanks.css";

type View = "specs" | "spotlight";
const TABS: PageTab<View>[] = [
  { key: "specs", label: "Tank Specs" },
  { key: "spotlight", label: "Spotlight" },
];

// How many days of readings the Spotlight trend shows, and how often the page re-reads
// (the sensors log every few hours, so this only keeps an open page from going stale).
const TREND_DAYS = 14;
const REFRESH_MS = 5 * 60_000;

// Oil Tanks — the site's Specgauge oil-tank sensors (client Specgauge, depot Evergreen),
// read through the API's OilTanksController. Two views of the same data: Tank Specs (the
// default) and Spotlight.
export default function OilTanksPage(_props: { user: AdminUserDto }) {
  const [view, setView] = useState<View>("specs");
  const [data, setData] = useState<OilTanksResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [loadedAt, setLoadedAt] = useState<Date | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setData(await getOilTanks(TREND_DAYS));
      setError(null);
      setLoadedAt(new Date());
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load(), REFRESH_MS);
    return () => window.clearInterval(timer);
  }, [load]);

  const tanks = data?.tanks ?? [];
  const meta = data
    ? `${data.client} · ${data.depot} depot · ${tanks.length} sensor${tanks.length === 1 ? "" : "s"}`
    : undefined;

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader
          title="Oil Tanks"
          meta={meta}
          center={
            <div className="ot-header-actions">
              {loadedAt && (
                <span className="ot-muted">
                  Updated {loadedAt.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" })}
                </span>
              )}
              <button type="button" className="ot-btn" onClick={() => void load()} disabled={loading}>
                {loading ? "Refreshing…" : "Refresh"}
              </button>
            </div>
          }
        />
        <PageTabs tabs={TABS} active={view} onChange={setView} />

        {error && !data ? (
          <div className="ot-message">
            <strong>The tank readings could not be loaded.</strong>
            <span>{error}</span>
          </div>
        ) : !data ? (
          <div className="ot-message">Loading tanks…</div>
        ) : tanks.length === 0 ? (
          <div className="ot-message">Specgauge has no sensors at this depot.</div>
        ) : view === "specs" ? (
          <TankSpecsView tanks={tanks} />
        ) : (
          <SpotlightView tanks={tanks} days={data.days} />
        )}
      </div>
    </div>
  );
}
