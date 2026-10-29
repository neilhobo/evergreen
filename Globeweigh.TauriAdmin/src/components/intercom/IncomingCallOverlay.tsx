import { useEffect, useRef, useState } from "react";
import { ApiError } from "../../api/client";
import { answerRing, getIntercomCall, getRingSnapshot, type IntercomCall, type IntercomRing } from "../../api/intercom";
import { getVideoBaseUrl } from "../../config";
import { startRingtone } from "./ringtone";
import CallDialog from "./CallDialog";
import "./Intercom.css";

const POLL_MS = 1000;
// When the API has no intercom (not configured, or an older API without the endpoint)
// there is nothing to ring, so ask far less often.
const IDLE_POLL_MS = 30000;
// How long "Answered by …" stays up on the machines that did not answer.
const TAKEN_SHOW_MS = 4000;

const PHONE_ICON =
  "M6.6 10.8a15.1 15.1 0 0 0 6.6 6.6l2.2-2.2a1 1 0 0 1 1-.25 11.4 11.4 0 0 0 3.6.57 1 1 0 0 1 1 1V20a1 1 0 0 1-1 1A17 17 0 0 1 3 4a1 1 0 0 1 1-1h3.5a1 1 0 0 1 1 1c0 1.25.2 2.45.57 3.57a1 1 0 0 1-.25 1z";
const X_ICON =
  "M18.3 5.7a1 1 0 0 0-1.4 0L12 10.6 7.1 5.7a1 1 0 0 0-1.4 1.4l4.9 4.9-4.9 4.9a1 1 0 1 0 1.4 1.4l4.9-4.9 4.9 4.9a1 1 0 0 0 1.4-1.4L13.4 12l4.9-4.9a1 1 0 0 0 0-1.4z";

// The incoming-call card, top right over every page, on every running admin app. The first
// to press Answer gets the call (the API arbitrates) and a full-screen CallDialog; everyone
// else sees who took it. Dismiss only hides it on this machine.
export default function IncomingCallOverlay() {
  const [call, setCall] = useState<IntercomCall | null>(null);
  const [dismissed, setDismissed] = useState<number | null>(null);
  // The call this machine answered, kept here so the dialog stays up whatever the polls say.
  const [mine, setMine] = useState<{ ring: IntercomRing; stream: string; videoBaseUrl: string } | null>(null);
  const [taken, setTaken] = useState<{ ringId: number; unit: string; by: string } | null>(null);
  const [answering, setAnswering] = useState(false);
  const [snapshot, setSnapshot] = useState<{ ringId: number; url: string } | null>(null);
  const shownRing = useRef<number | null>(null);

  // Poll the API for the call in progress.
  useEffect(() => {
    let alive = true;
    let timer = 0;
    const poll = async () => {
      let delay = POLL_MS;
      try {
        const c = await getIntercomCall();
        if (!alive) return;
        setCall(c);
        if (!c.enabled) delay = IDLE_POLL_MS;
      } catch {
        delay = IDLE_POLL_MS;
      }
      if (alive) timer = window.setTimeout(poll, delay);
    };
    poll();
    return () => {
      alive = false;
      window.clearTimeout(timer);
    };
  }, []);

  const ring = call?.ring ?? null;
  const ringing = !!ring && !ring.answeredBy && dismissed !== ring.id && mine?.ring.id !== ring.id;

  // Someone else took the call we were showing: say who, briefly.
  useEffect(() => {
    if (ring && ring.answeredBy && mine?.ring.id !== ring.id && shownRing.current === ring.id) {
      setTaken({ ringId: ring.id, unit: ring.unit, by: ring.answeredBy });
      shownRing.current = null;
    }
    if (ringing) shownRing.current = ring!.id;
  }, [ring, ringing, mine]);

  useEffect(() => {
    if (!taken) return;
    const t = window.setTimeout(() => setTaken(null), TAKEN_SHOW_MS);
    return () => window.clearTimeout(t);
  }, [taken]);

  // Ring while the card is up.
  useEffect(() => {
    if (!ringing) return;
    return startRingtone();
  }, [ringing]);

  // The still taken at the press. The API grabs it just after the event, so it may appear a
  // poll or two after the ring does.
  useEffect(() => {
    if (!ring?.hasSnapshot || snapshot?.ringId === ring.id) return;
    let cancelled = false;
    getRingSnapshot(ring.id)
      .then((blob) => {
        if (cancelled) return;
        setSnapshot((old) => {
          if (old) URL.revokeObjectURL(old.url);
          return { ringId: ring.id, url: URL.createObjectURL(blob) };
        });
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [ring?.id, ring?.hasSnapshot]); // eslint-disable-line react-hooks/exhaustive-deps

  const answer = async () => {
    if (!ring || !call || answering) return;
    setAnswering(true);
    try {
      const answered = await answerRing(ring.id);
      setMine({ ring: answered, stream: answered.stream || call.stream, videoBaseUrl: getVideoBaseUrl(call.videoBaseUrl) });
    } catch (e) {
      setTaken({ ringId: ring.id, unit: ring.unit, by: e instanceof ApiError ? e.message.replace(/^Answered by /, "") : "someone else" });
      setDismissed(ring.id);
    } finally {
      setAnswering(false);
    }
  };

  const snapFor = (id: number) => (snapshot?.ringId === id ? snapshot.url : null);

  return (
    <>
      {ringing && ring && (
        <div className="ic-call" role="alert">
          <div className="ic-call-img">{snapFor(ring.id) && <img src={snapFor(ring.id)!} alt="" />}</div>
          <div className="ic-call-body">
            <div className="ic-call-title">{ring.unit || "Weighbridge"}</div>
            <div className="ic-call-sub">Incoming video call…</div>
            <div className="ic-call-actions">
              <div className="ic-call-col">
                <button className="ic-call-btn ic-call-dismiss" onClick={() => setDismissed(ring.id)} aria-label="Dismiss">
                  <svg viewBox="0 0 24 24" aria-hidden="true"><path d={X_ICON} /></svg>
                </button>
                <span>Dismiss</span>
              </div>
              <div className="ic-call-col">
                <button className="ic-call-btn ic-call-answer" onClick={answer} disabled={answering} aria-label="Answer">
                  <svg viewBox="0 0 24 24" aria-hidden="true"><path d={PHONE_ICON} /></svg>
                </button>
                <span>{answering ? "Answering…" : "Answer"}</span>
              </div>
            </div>
          </div>
        </div>
      )}

      {!ringing && taken && (
        <div className="ic-call" role="status">
          <div className="ic-call-img">{snapFor(taken.ringId) && <img src={snapFor(taken.ringId)!} alt="" />}</div>
          <div className="ic-call-body">
            <div className="ic-call-title">{taken.unit || "Weighbridge"}</div>
            <div className="ic-call-sub">
              {taken.by === "The call has ended" ? taken.by : `Answered by ${taken.by}`}
            </div>
          </div>
        </div>
      )}

      {mine && (
        <CallDialog
          ring={mine.ring}
          stream={mine.stream}
          videoBaseUrl={mine.videoBaseUrl}
          snapshotUrl={snapFor(mine.ring.id)}
          onClose={() => setMine(null)}
        />
      )}
    </>
  );
}
