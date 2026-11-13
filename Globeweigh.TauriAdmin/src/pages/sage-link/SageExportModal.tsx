import { useEffect, useRef, useState } from "react";
import Modal from "../../components/Modal";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  SageExportJob,
  SageExportPretest,
  exportOrdersToSage,
  getSageExportProgress,
  pretestSageExport,
} from "../../api/sage";
import "./SageLink.css";

// Completed Orders > "Export to Sage". Three steps in one dialog:
//   1. the pre-test — what would be posted, what is in the way (nothing is written);
//   2. the confirmation — "Export N orders", naming the Sage company they go into;
//   3. the posts, followed order by order until Sage has answered for each.
// The API only queues; the sidecar on the Sage machine does the posting, a few seconds later.
// Which company (TEST or LIVE) is the server's decision and is shown before anything is sent.

const money = (n: number) => n.toLocaleString("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const plural = (n: number, noun: string) => `${n} ${noun}${n === 1 ? "" : "s"}`;

// How long the dialog follows the posts before saying "they are queued, look later".
const FOLLOW_MS = 3 * 60 * 1000;
const POLL_MS = 2000;

export default function SageExportModal({ orderIds, onClose }: { orderIds: number[]; onClose: (exported: boolean) => void }) {
  const [test, setTest] = useState<SageExportPretest | null>(null);
  const [testing, setTesting] = useState(true);
  const [sending, setSending] = useState(false);
  // Set once the orders are queued: from then on the dialog follows the jobs.
  const [jobs, setJobs] = useState<SageExportJob[] | null>(null);
  const [skipped, setSkipped] = useState<{ orderId: number; orderNo: number; reason: string }[]>([]);
  const [gaveUp, setGaveUp] = useState(false);
  const alive = useRef(true);

  async function runTest() {
    setTesting(true);
    try {
      const r = await pretestSageExport(orderIds);
      if (alive.current) setTest(r);
    } catch (err) {
      if (!alive.current) return;
      void messageDialog("The pre-test could not be run.\n" + (err as Error).message);
      onClose(false);
    } finally {
      if (alive.current) setTesting(false);
    }
  }

  useEffect(() => {
    alive.current = true;
    void runTest();
    return () => {
      alive.current = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Follow the queued jobs until each is Done or Failed (or FOLLOW_MS has passed).
  const queuedIds = jobs?.map((j) => j.dispatchOrderId).join(",") ?? "";
  useEffect(() => {
    if (!queuedIds) return;
    const ids = queuedIds.split(",").map(Number);
    const started = Date.now();
    let timer: number | undefined;
    let stopped = false;
    async function tick() {
      try {
        const latest = await getSageExportProgress(ids);
        if (stopped) return;
        setJobs(latest);
        if (latest.every((j) => j.status === "Done" || j.status === "Failed")) return;
      } catch {
        // A missed poll is not worth a dialog; the next one will do.
      }
      if (stopped) return;
      if (Date.now() - started > FOLLOW_MS) return setGaveUp(true);
      timer = window.setTimeout(() => void tick(), POLL_MS);
    }
    timer = window.setTimeout(() => void tick(), POLL_MS);
    return () => {
      stopped = true;
      window.clearTimeout(timer);
    };
  }, [queuedIds]);

  const isTest = test?.target !== "Live";
  const company = isTest ? "TEST" : "LIVE";
  const exportable = test ? test.orders.filter((o) => o.status !== "Blocked") : [];
  const net = exportable.reduce((n, o) => n + o.netTotal, 0);

  async function onExport() {
    if (!test || exportable.length === 0) return;
    const ok = await confirmDialog(
      `Post ${plural(exportable.length, "order")} into the Sage ${company} company?` +
        (isTest
          ? "\n\nThis is the test company — nothing reaches the live accounts."
          : "\n\nThis is the LIVE company: real sales orders will be created in the client's accounts."),
    );
    if (!ok) return;
    setSending(true);
    try {
      const r = await exportOrdersToSage(exportable.map((o) => o.orderId));
      if (!alive.current) return;
      setSkipped(r.skipped);
      setJobs(r.jobs);
    } catch (err) {
      void messageDialog("Nothing was sent.\n" + (err as Error).message);
    } finally {
      if (alive.current) setSending(false);
    }
  }

  const done = jobs?.filter((j) => j.status === "Done").length ?? 0;
  const failed = jobs?.filter((j) => j.status === "Failed").length ?? 0;
  const finished = jobs != null && done + failed === jobs.length;

  return (
    <Modal
      title={orderIds.length === 1 ? "Export order to Sage" : `Export ${orderIds.length} orders to Sage`}
      width={860}
      height={620}
      onClose={() => onClose(jobs != null)}
      footer={
        jobs ? (
          <>
            <span className="sl-imp-foot-note">
              {finished
                ? `${done} posted${failed > 0 ? `, ${failed} failed` : ""}.`
                : gaveUp
                  ? "Still waiting on Sage. The orders stay queued and will post when the sidecar gets to them."
                  : "Posting… this dialog can be closed; the orders stay queued."}
            </span>
            <button className="btn-blue" onClick={() => onClose(true)}>
              Close
            </button>
          </>
        ) : (
          <>
            <span className="sl-imp-foot-note">
              {test && exportable.length > 0 && `${plural(exportable.length, "order")} · net ${money(net)}`}
            </span>
            <button className="toolbar-btn" onClick={() => onClose(false)}>
              Cancel
            </button>
            <button className="toolbar-btn" disabled={testing || sending} onClick={() => void runTest()}>
              {testing && test ? "Testing…" : "Run pre-test again"}
            </button>
            <button
              className="btn-blue"
              disabled={!test || testing || sending || !test.canExport}
              title={test && !test.canExport ? "The pre-test found nothing that can be exported" : undefined}
              onClick={() => void onExport()}
            >
              {sending ? "Sending…" : `Export ${plural(exportable.length, "order")}`}
            </button>
          </>
        )
      }
    >
      {!test ? (
        <div className="sl-empty">Running the pre-test…</div>
      ) : (
        <div className="sl-imp">
          <div className={`sl-exp-target ${isTest ? "test" : "live"}`}>
            <b>{company} company</b>
            <span>
              {isTest
                ? "These orders go into the Sage test company. Nothing reaches the live accounts."
                : "These orders will be created as real sales orders in the client's live Sage accounts."}
              {isTest && test.sidecar.testCompany && <em> {test.sidecar.testCompany}</em>}
            </span>
          </div>

          {test.blockers.length > 0 && (
            <div className="sl-exp-blockers">
              <b>Nothing can be exported right now</b>
              {test.blockers.map((b, i) => (
                <div key={i}>{b}</div>
              ))}
            </div>
          )}

          {!jobs && (
            <div className="sl-imp-stats">
              <Stat n={test.ready} label="ready" />
              <Stat n={test.warnings} label={test.warnings === 1 ? "with a warning" : "with warnings"} warn={test.warnings > 0} muted={test.warnings === 0} />
              <Stat n={test.blocked} label="cannot be exported" block={test.blocked > 0} muted={test.blocked === 0} />
            </div>
          )}

          <div className="sl-imp-list">
            {test.orders.map((o) => {
              const job = jobs?.find((j) => j.dispatchOrderId === o.orderId);
              const skip = skipped.find((s) => s.orderId === o.orderId);
              return (
                <div key={o.orderId} className="sl-imp-row">
                  <div className="sl-imp-ac">{o.orderNo || "—"}</div>
                  <div className="sl-imp-main">
                    <div className="sl-imp-name">
                      <span className="sl-exp-cust">{o.customerName ?? "Unknown order"}</span>
                      {o.accountRef && <span className="sl-imp-meta">{o.accountRef}</span>}
                      <span className="sl-exp-fill" />
                      {o.lines > 0 && (
                        <span className="sl-imp-meta">
                          {plural(o.lines, "line")} · {money(o.netTotal)}
                        </span>
                      )}
                      {jobs ? <JobChip job={job} blocked={o.status === "Blocked" || !!skip} /> : <StatusChip status={o.status} />}
                    </div>
                    {job?.status === "Failed" && job.lastError && <div className="sl-exp-issue block">{job.lastError}</div>}
                    {jobs && !job && skip && <div className="sl-exp-issue block">{skip.reason}</div>}
                    {(!jobs || (!job && !skip)) &&
                      o.issues.map((i, n) => (
                        <div key={n} className={`sl-exp-issue ${i.severity}`}>
                          {i.text}
                        </div>
                      ))}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </Modal>
  );
}

function StatusChip({ status }: { status: "Ready" | "Warning" | "Blocked" }) {
  const label = status === "Ready" ? "Ready" : status === "Warning" ? "Ready, with warnings" : "Cannot export";
  return <span className={`sl-exp-chip ${status.toLowerCase()}`}>{label}</span>;
}

function JobChip({ job, blocked }: { job: SageExportJob | undefined; blocked: boolean }) {
  if (!job) return <span className="sl-exp-chip blocked">{blocked ? "Not sent" : "Not queued"}</span>;
  if (job.status === "Done") return <span className="sl-exp-chip ready">Sage order {job.sageOrderNo}</span>;
  if (job.status === "Failed") return <span className="sl-exp-chip blocked">Failed</span>;
  return (
    <span className="sl-exp-chip busy">
      {job.status === "Exporting" ? "Posting…" : job.attempts > 0 ? "Retrying…" : "Queued…"}
    </span>
  );
}

function Stat({ n, label, muted, warn, block }: { n: number; label: string; muted?: boolean; warn?: boolean; block?: boolean }) {
  return (
    <div className={`sl-imp-stat${muted ? " muted" : ""}${warn ? " warn" : ""}${block ? " block" : ""}`}>
      <b>{n.toLocaleString()}</b>
      <span>{label}</span>
    </div>
  );
}
