import { useEffect, useRef, useState } from "react";
import { getIntercomStatus, getRingSnapshot, type IntercomRing, type IntercomStatus } from "../../api/intercom";
import CameraStream from "../gate-barrier/CameraStream";
import { getVideoBaseUrl } from "../../config";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import "./Intercom.css";

const POLL_MS = 1000;
// The newest few rings; the event log below has the rest.
const RINGS_SHOWN = 4;

const time = (utc: string) => new Date(utc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });

function ringLength(r: IntercomRing): string {
  if (!r.endedUtc) return "ringing";
  const secs = Math.max(0, Math.round((Date.parse(r.endedUtc) - Date.parse(r.startedUtc)) / 1000));
  return `${secs}s`;
}

// Weighbridge door intercom (Dahua VTO). The API listens to the unit's event stream; this
// page polls it for call state and shows the unit's camera through go2rtc like Gate/Barrier.
// Phase 1: see who is there and when they rang. Talk-back and door release come later.
export default function IntercomPage() {
  const [status, setStatus] = useState<IntercomStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [thumbs, setThumbs] = useState<Record<number, string>>({});
  const fetched = useRef(new Set<number>());
  // Which unit's camera is on screen: the one picked, else whichever is ringing, else the first.
  const [picked, setPicked] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    const poll = async () => {
      try {
        const s = await getIntercomStatus();
        if (!alive) return;
        setStatus(s);
        setError(null);
      } catch (e) {
        if (alive) setError(e instanceof Error ? e.message : String(e));
      }
    };
    poll();
    const id = window.setInterval(poll, POLL_MS);
    return () => {
      alive = false;
      window.clearInterval(id);
    };
  }, []);

  // Each ring's still is fetched once and kept for as long as the page is open.
  useEffect(() => {
    for (const r of status?.rings ?? []) {
      if (!r.hasSnapshot || fetched.current.has(r.id)) continue;
      fetched.current.add(r.id);
      getRingSnapshot(r.id)
        .then((blob) => setThumbs((t) => ({ ...t, [r.id]: URL.createObjectURL(blob) })))
        .catch(() => fetched.current.delete(r.id));
    }
  }, [status]);

  useEffect(() => () => Object.values(thumbs).forEach(URL.revokeObjectURL), []); // eslint-disable-line react-hooks/exhaustive-deps

  const units = status?.units ?? [];
  const shown = units.find((u) => u.stream === picked) ?? units.find((u) => u.ringing) ?? units[0] ?? null;

  const link = !status
    ? { cls: "ic-pill-off", text: error ? "API unreachable" : "Connecting…" }
    : !status.enabled
      ? { cls: "ic-pill-off", text: "Not configured" }
      : status.connected
        ? { cls: "ic-pill-on", text: "Connected" }
        : { cls: "ic-pill-bad", text: "Disconnected" };

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title="Intercom" />
        <div className={cc.body}>
          <div className="ic-page">
            {/* Fixed-width column: the units and the call state stay put; the event log takes the
                rest of the height and scrolls on its own, so nothing here can squeeze the video. */}
            <aside className="ic-side">
              <section className="ic-block">
                <div className="ic-label">Weighbridge intercoms</div>
                {units.length === 0 ? (
                  <span className={`ic-pill ${link.cls}`}>{link.text}</span>
                ) : (
                  <ul className="ic-units">
                    {units.map((u) => (
                      <li key={u.name}>
                        <button
                          type="button"
                          className={`ic-unit${u.stream === shown?.stream ? " ic-unit-shown" : ""}${u.ringing ? " ic-unit-ringing" : ""}`}
                          onClick={() => setPicked(u.stream)}
                          title="Show this camera"
                        >
                          <span className="ic-unit-name">{u.name}</span>
                          <span className={`ic-pill ${u.listening === false ? "ic-pill-off" : u.connected ? "ic-pill-on" : "ic-pill-bad"}`}>
                            {u.listening === false ? "Video only" : u.ringing ? "Ringing" : u.connected ? "Connected" : "Disconnected"}
                          </span>
                          <span className="ic-sub">{u.host}</span>
                          {u.listening !== false && !u.connected && u.lastError && <span className="ic-sub ic-err-inline">{u.lastError}</span>}
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </section>

              <section className={`ic-bell${status?.ringing ? " ic-bell-ringing" : ""}`}>
                <div className="ic-bell-title">
                  {status?.ringing ? `Driver at ${units.find((u) => u.ringing)?.name ?? "the intercom"}` : "No call"}
                </div>
                <div className="ic-bell-sub">
                  {status?.activeRing ? `Rang at ${time(status.activeRing.startedUtc)}` : "Waiting for the bell"}
                </div>
              </section>

              <section className="ic-block">
                <div className="ic-label">Recent rings</div>
                {(status?.rings.length ?? 0) === 0 ? (
                  <div className="ic-sub">None since the API started.</div>
                ) : (
                  <ul className="ic-rings">
                    {status!.rings.slice(0, RINGS_SHOWN).map((r) => (
                      <li key={r.id} className="ic-ring">
                        {thumbs[r.id] ? <img src={thumbs[r.id]} alt="" /> : <div className="ic-ring-noimg" />}
                        <div>
                          <div className="ic-ring-time">{time(r.startedUtc)}</div>
                          {r.unit && <div className="ic-sub">{r.unit}</div>}
                          <div className="ic-sub">{r.answeredBy ? `Answered by ${r.answeredBy}` : ringLength(r)}</div>
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </section>

              <section className="ic-block ic-log">
                <div className="ic-label">Event log</div>
                <ul className="ic-events">
                  {(status?.events ?? []).map((e, i) => (
                    <li key={`${e.atUtc}-${i}`}>
                      <span className="ic-ev-time">{time(e.atUtc)}</span>
                      <span className="ic-ev-what">
                        <span className="ic-ev-code">{e.code}</span> <span className="ic-ev-action">{e.action}</span>
                        {e.callId && <span className="ic-sub"> #{e.callId}</span>}
                      </span>
                      {e.unit && <span className="ic-ev-unit">{e.unit}</span>}
                    </li>
                  ))}
                </ul>
              </section>
            </aside>

            <div className="ic-video">
              {status?.enabled ? (
                <CameraStream videoBaseUrl={getVideoBaseUrl(shown?.videoBaseUrl ?? status.videoBaseUrl)} stream={shown?.stream ?? status.stream} />
              ) : (
                <div className="ic-video-empty">
                  <div className="ic-video-title">Intercom camera</div>
                  <div className="ic-sub">Add the units under Intercom:Units in the API config to connect.</div>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
