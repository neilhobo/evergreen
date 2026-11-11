import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  LeaveEmployee,
  LeaveRequestRow,
  LeaveStatus,
  RequestPreview,
  cancelLeaveRequest,
  decideLeaveRequest,
  deleteLeaveRequest,
  fmtDays,
  fmtLeft,
  fmtRange,
  getLeaveRequest,
  previewLeaveRequest,
  updateLeaveRequest,
} from "../../api/annualLeave";
import { fmtDateTime } from "../../format";
import { CheckList, StatusChip } from "./parts";

interface Props {
  requestId: number;
  /** For the email address and balance; the calendar already has the list. */
  employee?: LeaveEmployee;
  onClose: (changed: boolean) => void;
}

// One request, opened from the calendar (a blue or hatched day): what it is, what it does to
// their balance and the policy checks, and everything that can be done with it — approve or
// decline it while it waits, change its dates, length or note, cancel it (the record stays and
// the days go back) or delete it (entered by mistake).
export default function LeaveRequestModal({ requestId, employee, onClose }: Props) {
  const [req, setReq] = useState<LeaveRequestRow | null>(null);
  const [editing, setEditing] = useState(false);
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [length, setLength] = useState(1);
  const [note, setNote] = useState("");
  const [decisionNote, setDecisionNote] = useState("");
  const [sendEmail, setSendEmail] = useState(true);
  const [preview, setPreview] = useState<RequestPreview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function load() {
    try {
      const r = await getLeaveRequest(requestId);
      setReq(r);
      setStart(r.startDate);
      setEnd(r.endDate);
      setLength(r.dayLength);
      setNote(r.note ?? "");
    } catch (e) {
      void messageDialog((e as Error).message);
      onClose(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestId]);

  const single = start !== "" && start === end;

  // While editing, the preview follows the new dates (worked out without this request itself).
  useEffect(() => {
    setPreview(null);
    if (!editing || !req || !start || !end) return;
    const t = window.setTimeout(() => {
      previewLeaveRequest({ requestId: req.id, employeeId: req.employeeId, startDate: start, endDate: end, dayLength: single ? length : 1 })
        .then(setPreview)
        .catch((e) => setError((e as Error).message));
    }, 250);
    return () => window.clearTimeout(t);
  }, [editing, req, start, end, length, single]);

  if (!req) return null;

  const waiting = req.status === LeaveStatus.Waiting;
  const live = waiting || req.status === LeaveStatus.Approved;
  const emailTo = employee?.emailTo ?? null;
  const first = req.employeeName.split(" ")[0];
  const blocked = editing && (preview?.checks.some((c) => c.level === "error") ?? true);

  async function run(work: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await work();
      onClose(true);
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }

  const decide = (approve: boolean) =>
    run(async () => {
      const res = await decideLeaveRequest(req.id, approve, decisionNote.trim() || null, sendEmail && !!emailTo);
      if (sendEmail && emailTo && !res.emailed)
        void messageDialog(`The request was ${approve ? "approved" : "declined"}, but the email to ${emailTo} didn't go. Check Super Admin > Email Setup.`);
    });

  const save = () =>
    run(() =>
      updateLeaveRequest(req.id, {
        employeeId: req.employeeId,
        startDate: start,
        endDate: end,
        dayLength: single ? length : 1,
        note: note.trim() || null,
      }),
    );

  async function cancel() {
    if (!(await confirmDialog(`Cancel ${first}'s leave on ${fmtRange(req!.startDate, req!.endDate, req!.dayLength)}? The days go back on their balance and the record is kept as cancelled.`)))
      return;
    await run(() => cancelLeaveRequest(req!.id));
  }

  async function remove() {
    if (!(await confirmDialog(`Delete this request completely? Use this only for leave entered by mistake — for leave that is no longer being taken, Cancel it instead so the record is kept.`)))
      return;
    await run(() => deleteLeaveRequest(req!.id));
  }

  const footer = editing ? (
    <>
      <button className="toolbar-btn" onClick={() => setEditing(false)} disabled={busy}>
        Back
      </button>
      <button className="btn-blue" onClick={() => void save()} disabled={busy || blocked}>
        {busy ? "Saving…" : "Save changes"}
      </button>
    </>
  ) : (
    <>
      <button className="al-btn-danger" onClick={() => void remove()} disabled={busy} style={{ marginRight: "auto" }}>
        Delete
      </button>
      {live && (
        <button className="toolbar-btn" onClick={() => void cancel()} disabled={busy}>
          Cancel leave
        </button>
      )}
      {live && (
        <button className="toolbar-btn" onClick={() => setEditing(true)} disabled={busy}>
          Edit
        </button>
      )}
      {waiting ? (
        <>
          <button className="al-btn-danger" onClick={() => void decide(false)} disabled={busy}>
            Decline
          </button>
          <button className="btn-blue" onClick={() => void decide(true)} disabled={busy || req.checks.some((c) => c.level === "error")}>
            Approve
          </button>
        </>
      ) : (
        <button className="btn-blue" onClick={() => onClose(false)} disabled={busy}>
          Close
        </button>
      )}
    </>
  );

  return (
    <Modal title={`${req.employeeName} — ${editing ? "Change Leave" : "Leave"}`} width={600} onClose={() => onClose(false)} footer={footer}>
      {!editing ? (
        <>
          <div className="al-lr-head">
            <div>
              <div className="al-lr-dates">{fmtRange(req.startDate, req.endDate, req.dayLength)}</div>
              <div className="al-muted">
                {fmtDays(req.days)} day{req.days === 1 ? "" : "s"} · {req.categoryName}
              </div>
            </div>
            <StatusChip status={req.status} />
          </div>

          {live && (
            <div className="al-tiles">
              <div className={`al-tile${req.remainingBefore < 0 ? " bad" : ""}`}>
                <div className="al-tile-label">Left without this</div>
                <div className="al-tile-value">{fmtLeft(req.remainingBefore)}</div>
              </div>
              <div className="al-tile">
                <div className="al-tile-label">This leave</div>
                <div className="al-tile-value">{fmtDays(req.days)}</div>
              </div>
              <div className={`al-tile${req.remainingAfter < 0 ? " bad" : ""}`}>
                <div className="al-tile-label">{waiting ? "Left if approved" : "Left with it"}</div>
                <div className="al-tile-value">{fmtLeft(req.remainingAfter)}</div>
              </div>
            </div>
          )}

          <dl className="al-facts">
            <dt>Entered</dt>
            <dd>
              {req.requestedByName ? `${req.requestedByName}, ` : ""}
              {fmtDateTime(req.dateCreated)}
            </dd>
            {!waiting && (
              <>
                <dt>{req.status === LeaveStatus.Approved ? "Approved by" : req.status === LeaveStatus.Declined ? "Declined by" : "Cancelled by"}</dt>
                <dd>
                  {req.decidedByName || "—"}
                  {req.dateDecided ? `, ${fmtDateTime(req.dateDecided)}` : ""}
                </dd>
              </>
            )}
            {req.note && (
              <>
                <dt>Note</dt>
                <dd>{req.note}</dd>
              </>
            )}
            {req.decisionNote && (
              <>
                <dt>Decision note</dt>
                <dd>{req.decisionNote}</dd>
              </>
            )}
          </dl>

          {live && (
            <div className="al-checks">
              <div className="al-checks-title">Policy checks</div>
              <CheckList checks={req.checks} />
            </div>
          )}

          {waiting && (
            <div className="form-grid">
              <div className="form-field form-field-wide">
                <label className="form-label">Note to {first} (optional)</label>
                <input className="form-input" value={decisionNote} onChange={(e) => setDecisionNote(e.target.value)} />
              </div>
              <div className="form-field form-field-wide">
                <label className="al-check-label" title={emailTo ?? "No email address on their Staff record"}>
                  <input type="checkbox" checked={sendEmail && !!emailTo} disabled={!emailTo} onChange={(e) => setSendEmail(e.target.checked)} />
                  {emailTo ? `Email ${emailTo} the decision` : "No email address on file"}
                </label>
              </div>
            </div>
          )}
        </>
      ) : (
        <>
          <div className="form-grid">
            <div className="form-field">
              <label className="form-label">First day</label>
              <input
                className="form-input"
                type="date"
                value={start}
                autoFocus
                onChange={(e) => {
                  setStart(e.target.value);
                  if (!end || e.target.value > end) setEnd(e.target.value);
                }}
              />
            </div>
            <div className="form-field">
              <label className="form-label">Last day</label>
              <input className="form-input" type="date" value={end} min={start || undefined} onChange={(e) => setEnd(e.target.value)} />
            </div>
            {single && (
              <div className="form-field form-field-wide">
                <label className="form-label">How long</label>
                <div className="al-lengths">
                  {[
                    [1, "Full day"],
                    [0.5, "Half day"],
                    [0.25, "Quarter day"],
                  ].map(([v, label]) => (
                    <button key={v} type="button" className={`al-seg${length === v ? " on" : ""}`} onClick={() => setLength(v as number)}>
                      {label}
                    </button>
                  ))}
                </div>
              </div>
            )}
            <div className="form-field form-field-wide">
              <label className="form-label">Note</label>
              <input className="form-input" value={note} onChange={(e) => setNote(e.target.value)} />
            </div>
          </div>
          <p className="al-muted" style={{ margin: "4px 0 0" }}>
            It stays {waiting ? "waiting" : "approved"}; a change of dates is noted on the request with your name.
          </p>
          {preview && (
            <div className="al-preview">
              <div className="al-preview-sum">
                <span>
                  Uses {fmtDays(preview.days)} day{preview.days === 1 ? "" : "s"}
                </span>
                <span className={preview.remainingAfter < 0 ? "al-neg" : undefined}>
                  {fmtLeft(preview.remainingBefore)} → {fmtLeft(preview.remainingAfter)} {preview.remainingAfter < 0 ? "(paid leave used up)" : "floating left"}
                </span>
              </div>
              <CheckList checks={preview.checks} />
            </div>
          )}
        </>
      )}
      {error && <p className="al-error">{error}</p>}
    </Modal>
  );
}
