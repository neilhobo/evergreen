import { useEffect, useMemo, useRef, useState } from "react";
import Modal from "../../components/Modal";
import styles from "../../widgets/DetailPanel.module.css";
import { Field, cx } from "../../widgets/DetailPanel";
import {
  LoadEmailAttachment,
  LoadEmailKind,
  LoadEmailPreview,
  getCommercialInvoicePdf,
  getDeliveryNotePdf,
  getLoadEmail,
  getOrderConfirmationPdf,
  sendLoadEmail,
} from "../../api/dispatch";

interface Props {
  // The load's id — or, on the order confirmation, the ORDER's.
  loadId: number;
  // Which of the load's emails: "ready" (ready for collection, from the Loads grid),
  // "dispatched" (the dispatch confirmation, from the Completed Loads grid) or "precommercial"
  // (the pre-commercial invoices, to the haulier only, from Load detail). Or "confirmation":
  // an order's confirmation, to its customer only, from Open Orders.
  kind: LoadEmailKind;
  // sent: whether at least one email went, so the caller can refresh what shows it.
  onClose: (sent: boolean) => void;
}

// One of the messages the dialog sends: the haulier's, or (on the dispatch confirmation) one
// customer's copy, which carries only their orders and their delivery notes.
interface Msg {
  key: string;
  who: "Haulier" | "Customer";
  name: string;
  customerId?: number;
  recipients: string[];
  subject: string;
  html: string;
  attachments: LoadEmailAttachment[];
}

// One email that will go: a message, to these addresses; the haulier's, with invoices set,
// carries the commercial invoices after its delivery notes.
interface Batch {
  msg: Msg;
  to: string[];
  invoices: boolean;
}

// One step of a send, and how far it has got: a delivery note being rendered for the
// attachments, then an email going out.
interface Job {
  kind: "note" | "email";
  // What the row says: the file name, or the addresses in To.
  label: string;
  // An email's recipient: Haulier or Customer, and their name.
  who?: Msg["who"];
  name?: string;
  orderId?: number;
  status: "pending" | "sending" | "sent" | "failed" | "skipped";
  error?: string;
}

// The test address is remembered on this PC between uses (a convenience only: nothing
// breaks if it comes back empty).
const TEST_ADDRESS_KEY = "sendLoadEmail.testAddress";
const looksLikeEmail = (s: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(s.trim());
// A ticked address, per message: the same address can be both a haulier's and a customer's.
const pick = (key: string, address: string) => `${key}\n${address}`;
const plural = (n: number, word: string) => `${n} ${n === 1 ? word : word + "s"}`;
// An attachment, by what it is and whose: an order's note and its invoice are different files.
const attKey = (a: LoadEmailAttachment) => `${a.kind ?? "note"}-${a.orderId}`;
const attName = (a: LoadEmailAttachment) =>
  a.kind === "invoice"
    ? "commercial invoice"
    : a.kind === "preinvoice"
      ? "pre-commercial invoice"
      : a.kind === "confirmation"
        ? "order confirmation"
        : "delivery note";

// Send Email, for a load. Two messages go to the haulier through the one dialog: "ready for
// collection" (the 3-digit ref they key into the weighbridge to book in, and the orders and
// addresses they are taking) while the load is on the Loads grid, and the dispatch confirmation
// (it has weighed out and left, with what is on board, and each order's delivery note attached
// as PDF) once it is on the Completed Loads grid. The dispatch confirmation also goes to each
// customer on the load, in a copy of their own: their orders only, with only their delivery
// notes. In the haulier's group, Include Commercial Invoice (lavender, off by default) attaches
// each order's commercial invoice to the haulier's email after its delivery notes. Every message is composed by the API and shown here exactly as it will go out, one
// preview tab each; an attachment can be put in the frame in place of the message. Down the
// left, each message's recipients in a group of its own, marked Haulier or Customer, ticked by
// default; each group gets one email with all its ticked addresses in To. Sending puts a
// progress sheet over the dialog: a row per delivery note being rendered and then per email,
// each a spinner that becomes a tick, or a cross with the reason.
export default function SendLoadEmailModal({ loadId, kind, onClose }: Props) {
  const heading =
    kind === "dispatched"
      ? "Dispatch Confirmation"
      : kind === "precommercial"
        ? "Pre-Commercial Invoice"
        : kind === "confirmation"
          ? "Order Confirmation"
          : "Send Email";
  const [preview, setPreview] = useState<LoadEmailPreview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [chosen, setChosen] = useState<Set<string>>(new Set());
  // Testing: send to an address typed here instead of the recipients'.
  const [useOverride, setUseOverride] = useState(false);
  const [overrideTo, setOverrideTo] = useState(() => {
    try {
      return localStorage.getItem(TEST_ADDRESS_KEY) ?? "";
    } catch {
      return "";
    }
  });
  function rememberOverride(value: string) {
    setOverrideTo(value);
    try {
      localStorage.setItem(TEST_ADDRESS_KEY, value);
    } catch {
      /* not remembered, still usable */
    }
  }
  const [jobs, setJobs] = useState<Job[] | null>(null);

  // The haulier's message first, then each customer's in the order of their first drop. The
  // order confirmation has only the one, and it is the customer's.
  const messages = useMemo<Msg[]>(() => {
    if (!preview) return [];
    const toCustomer = kind === "confirmation";
    return [
      {
        key: "haulier",
        who: toCustomer ? "Customer" : "Haulier",
        name: toCustomer ? preview.customerName ?? "" : preview.haulierName,
        recipients: preview.recipients,
        subject: preview.subject,
        html: preview.html,
        attachments: preview.attachments,
      },
      ...(preview.customers ?? []).map<Msg>((c) => ({
        key: `customer-${c.customerId}`,
        who: "Customer",
        name: c.customerName,
        customerId: c.customerId,
        recipients: c.recipients,
        subject: c.subject,
        html: c.html,
        attachments: c.attachments,
      })),
    ];
  }, [preview, kind]);
  // Each order's commercial invoice (dispatch confirmation only), for the haulier's email.
  const invoices = useMemo(() => preview?.commercialInvoices ?? [], [preview]);

  useEffect(() => {
    let cancelled = false;
    getLoadEmail(loadId, kind)
      .then((p) => {
        if (cancelled) return;
        setPreview(p);
        setChosen(
          new Set([
            ...p.recipients.map((r) => pick("haulier", r)),
            ...(p.customers ?? []).flatMap((c) => c.recipients.map((r) => pick(`customer-${c.customerId}`, r))),
          ]),
        );
      })
      .catch((err) => {
        if (!cancelled) setError((err as Error).message);
      });
    return () => {
      cancelled = true;
    };
  }, [loadId, kind]);

  // The message on show in the preview.
  const [tab, setTab] = useState("haulier");
  const current = messages.find((m) => m.key === tab) ?? messages[0] ?? null;

  // Include Commercial Invoice, in the haulier's group: off until asked for.
  const [includeInvoices, setIncludeInvoices] = useState(false);
  const withInvoices = invoices.length > 0 && includeInvoices;
  // What the message on show carries: the haulier's gets the invoices too once any are asked for.
  const attachments = current
    ? current.key === "haulier" && withInvoices
      ? [...current.attachments, ...invoices]
      : current.attachments
    : [];

  // The attachments' PDFs, fetched once each and kept as blob URLs for the frame. The API
  // renders a note on the first ask and keeps it, so a note viewed here is not rendered again
  // when the email is sent. The URLs are this dialog's, and go with it.
  // Keyed by attKey: a delivery note and a commercial invoice are separate renders.
  const [pdfUrls, setPdfUrls] = useState<Record<string, string>>({});
  const pdfUrlsRef = useRef<Record<string, string>>({});
  const inflight = useRef<Map<string, Promise<string>>>(new Map());
  function docPdf(a: LoadEmailAttachment): Promise<string> {
    const k = attKey(a);
    const have = pdfUrlsRef.current[k];
    if (have) return Promise.resolve(have);
    let p = inflight.current.get(k);
    if (!p) {
      p = (
        a.kind === "invoice" || a.kind === "preinvoice"
          ? getCommercialInvoicePdf(a.orderId, a.kind === "preinvoice")
          : a.kind === "confirmation"
            ? getOrderConfirmationPdf(a.orderId)
            : getDeliveryNotePdf(a.orderId)
      )
        .then((blob) => {
          // Typed here as well: the frame shows a blob: URL as the file only when the blob
          // says it is a PDF, and not every fetch path carries the type through.
          const url = URL.createObjectURL(new Blob([blob], { type: "application/pdf" }));
          pdfUrlsRef.current = { ...pdfUrlsRef.current, [k]: url };
          setPdfUrls(pdfUrlsRef.current);
          return url;
        })
        .finally(() => inflight.current.delete(k));
      inflight.current.set(k, p);
    }
    return p;
  }
  useEffect(
    () => () => {
      Object.values(pdfUrlsRef.current).forEach((u) => URL.revokeObjectURL(u));
    },
    [],
  );

  // What the frame shows: the message, or one of its attachments (by attKey).
  const [viewing, setViewing] = useState<string | null>(null);
  const [viewError, setViewError] = useState<string | null>(null);
  async function showAttachment(a: LoadEmailAttachment) {
    setViewError(null);
    setViewing(attKey(a));
    try {
      await docPdf(a);
    } catch (err) {
      setViewError((err as Error).message);
    }
  }
  function showTab(key: string) {
    if (key === tab) return;
    setTab(key);
    setViewing(null);
    setViewError(null);
  }
  const viewingAtt = attachments.find((a) => attKey(a) === viewing) ?? null;
  const viewingName = viewingAtt?.fileName ?? "";

  const ticked = (m: Msg) => m.recipients.filter((r) => chosen.has(pick(m.key, r)));
  // What will actually go: per message, one email carrying every ticked address (the haulier's
  // with the commercial invoices when Include Commercial Invoice is ticked). A test address
  // takes every message, each once, and nobody else gets anything.
  const batches = useMemo<Batch[]>(
    () =>
      useOverride
        ? looksLikeEmail(overrideTo)
          ? messages.map((m) => ({ msg: m, to: [overrideTo.trim()], invoices: m.key === "haulier" && withInvoices }))
          : []
        : messages.flatMap((m) => {
            const to = m.recipients.filter((r) => chosen.has(pick(m.key, r)));
            return to.length > 0 ? [{ msg: m, to, invoices: m.key === "haulier" && withInvoices }] : [];
          }),
    [useOverride, overrideTo, messages, chosen, withInvoices],
  );

  function toggle(m: Msg, address: string) {
    const k = pick(m.key, address);
    setChosen((prev) => {
      const next = new Set(prev);
      if (next.has(k)) next.delete(k);
      else next.add(k);
      return next;
    });
  }

  const sending = jobs != null && jobs.some((j) => j.status === "pending" || j.status === "sending");
  const finished = jobs != null && !sending;
  const anySent = jobs != null && jobs.some((j) => j.kind === "email" && j.status === "sent");

  // The notes first, one at a time: each is rendered on the API and kept there for the sends,
  // so a note that will not render stops everything before any email has gone. Then the
  // emails, one at a time, so the sheet's ticks arrive in order and a failure is seen where
  // it happened rather than lost in a batch.
  async function send() {
    if (batches.length === 0) return;
    // Every note (and invoice) the emails going out carry, once each (the haulier's has every
    // note, and with the invoices every invoice).
    const needed = new Map<string, LoadEmailAttachment>();
    for (const b of batches)
      for (const a of b.invoices ? [...b.msg.attachments, ...invoices] : b.msg.attachments) needed.set(attKey(a), a);
    const docs = [...needed.values()];
    const notes: Job[] = docs.map((a) => ({
      kind: "note",
      label: a.fileName,
      orderId: a.orderId,
      status: "pending",
    }));
    const mails: Job[] = batches.map((b) => ({
      kind: "email",
      label: b.to.join(", "),
      who: b.msg.who,
      name: b.invoices ? `${b.msg.name} · with commercial invoices` : b.msg.name,
      status: "pending",
    }));
    const all = [...notes, ...mails];
    setJobs(all);
    const patch = (i: number, p: Partial<Job>) =>
      setJobs((js) => js!.map((j, k) => (k === i ? { ...j, ...p } : j)));

    for (let i = 0; i < notes.length; i++) {
      patch(i, { status: "sending" });
      try {
        await docPdf(docs[i]);
        patch(i, { status: "sent" });
      } catch (err) {
        patch(i, { status: "failed", error: (err as Error).message });
        for (let k = i + 1; k < all.length; k++) patch(k, { status: "skipped" });
        return;
      }
    }
    for (let i = 0; i < batches.length; i++) {
      const at = notes.length + i;
      const b = batches[i];
      patch(at, { status: "sending" });
      try {
        await sendLoadEmail(
          loadId,
          useOverride ? [] : b.to,
          useOverride ? b.to[0] : undefined,
          kind,
          b.msg.customerId,
          b.invoices,
        );
        patch(at, { status: "sent" });
      } catch (err) {
        patch(at, { status: "failed", error: (err as Error).message });
      }
    }
  }

  const single = messages.length === 1;
  const haulierTicked = messages[0] ? ticked(messages[0]).length : 0;
  const canSend = preview != null && preview.emailReady && batches.length > 0 && jobs == null;
  const sendLabel =
    batches.length === 0
      ? "Send"
      : useOverride
        ? batches.length === 1
          ? "Send test"
          : `Send ${batches.length} tests`
        : single
          ? `Send to ${haulierTicked}`
          : `Send ${plural(batches.length, "email")}`;

  const sendingNote = useOverride
    ? looksLikeEmail(overrideTo)
      ? single
        ? `One email, to the test address only. The ${messages[0].who.toLowerCase()} gets nothing.`
        : `${plural(messages.length, "email")}, one of each, all to the test address only. Nobody else gets anything.`
      : "Enter a test address."
    : batches.length === 0
      ? "Tick at least one address."
      : single
        ? `One email, with ${haulierTicked === 1 ? "the address" : `all ${haulierTicked} addresses`} in To.`
        : `${plural(batches.length, "email")}: one to each ticked recipient, with all their ticked addresses in To.`;

  const jobState = (j: Job) =>
    j.status === "pending"
      ? "Waiting"
      : j.status === "sending"
        ? j.kind === "note"
          ? "Rendering"
          : "Sending"
        : j.status === "sent"
          ? j.kind === "note"
            ? "Ready"
            : "Sent"
          : j.status === "skipped"
            ? "Not sent"
            : (j.error ?? "Failed");

  const whoTag = (who: Msg["who"]) => cx("le-who", who === "Customer" ? "le-who-customer" : "le-who-haulier");

  // The test address sits folded away at the foot of the list; open while it is in use.
  const [testOpen, setTestOpen] = useState(false);

  return (
    <Modal
      title={
        preview
          ? kind === "confirmation"
            ? `${heading} — Order ${preview.ref}`
            : `${heading} — Load ${kind === "dispatched" ? `#${loadId}` : preview.ref}`
          : heading
      }
      width={1080}
      height={820}
      // Not while emails are in flight: the sheet is the record of what got sent.
      onClose={() => {
        if (!sending) onClose(anySent);
      }}
      footer={
        finished ? (
          <button className="btn-blue" onClick={() => onClose(anySent)}>
            Close
          </button>
        ) : (
          <>
            <button className="toolbar-btn" onClick={() => onClose(false)} disabled={sending}>
              Cancel
            </button>
            <button className="btn-blue" disabled={!canSend} onClick={() => void send()}>
              {sendLabel}
            </button>
          </>
        )
      }
    >
      <div className="cl-form le-root">
        {error ? (
          <div className="data-table-empty">{error}</div>
        ) : !preview || !current ? (
          <div className="data-table-empty">Loading…</div>
        ) : (
          <div className="ld-panel le-panel">
            <div className="le-body">
              {/* Left: who each message goes to, and how. Clicking a group shows its message. */}
              <div className="le-side">
                {messages.map((m) => (
                  <section
                    key={m.key}
                    className={cx(
                      styles.section,
                      "le-group",
                      m.who === "Customer" && "le-group-customer",
                      !single && m.key === current.key && "le-group-on",
                    )}
                    onClick={() => showTab(m.key)}
                  >
                    <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
                      <span className={whoTag(m.who)}>{m.who}</span>
                      <span className={cx(styles.statusTag, "le-group-name")} title={m.name}>
                        {m.name || m.who}
                      </span>
                    </h3>
                    {m.recipients.length === 0 ? (
                      <p className="le-note">
                        {m.who === "Customer"
                          ? "This customer has no email addresses. Add them under Customers (Emails tab)."
                          : "This haulier has no email addresses. Add them under Hauliers."}
                      </p>
                    ) : (
                      <ul className="le-list">
                        {m.recipients.map((r) => (
                          <li key={r}>
                            <label className="le-check">
                              <input
                                type="checkbox"
                                checked={chosen.has(pick(m.key, r))}
                                disabled={useOverride}
                                onChange={() => toggle(m, r)}
                              />
                              <span>{r}</span>
                            </label>
                          </li>
                        ))}
                      </ul>
                    )}
                    {m.attachments.length > 0 && (
                      <p className="le-note">
                        {m.attachments.length === 1
                          ? `With the ${attName(m.attachments[0])} for order ${m.attachments[0].orderNo}.`
                          : `With ${m.attachments.length} ${attName(m.attachments[0])}s.`}
                      </p>
                    )}
                    {/* The commercial invoices ride on the haulier's email, off until asked for. */}
                    {m.key === "haulier" && invoices.length > 0 && (
                      <label
                        className="le-check le-include-invoices"
                        title={
                          invoices.length === 1
                            ? `Attach the commercial invoice for order ${invoices[0].orderNo}`
                            : `Attach ${invoices.length} commercial invoices`
                        }
                      >
                        <input
                          type="checkbox"
                          checked={includeInvoices}
                          onChange={(e) => setIncludeInvoices(e.target.checked)}
                        />
                        <span>{invoices.length === 1 ? "Include Commercial Invoice" : "Include Commercial Invoices"}</span>
                      </label>
                    )}
                  </section>
                ))}
                <section className={styles.section}>
                  <h3 className={styles.sectionTitle}>Sending</h3>
                  <p className="le-note">{sendingNote}</p>
                  {!preview.emailReady && (
                    <p className="le-note le-warn">
                      Email is not set up. Super Admin › Email Setup first.
                    </p>
                  )}
                </section>
                {/* Testing only, so folded away at the foot unless it is in use. */}
                <section className={cx(styles.section, "le-test", (testOpen || useOverride) && "le-test-open")}>
                  <h3 className={styles.sectionTitle}>
                    <button
                      type="button"
                      className="le-test-toggle"
                      aria-expanded={testOpen || useOverride}
                      onClick={() => setTestOpen((o) => !(o || useOverride))}
                      disabled={useOverride}
                    >
                      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                        <path d="M9 6l6 6-6 6" />
                      </svg>
                      Test address
                    </button>
                  </h3>
                  {(testOpen || useOverride) && (
                    <>
                      <label className="le-check">
                        <input
                          type="checkbox"
                          checked={useOverride}
                          onChange={(e) => setUseOverride(e.target.checked)}
                        />
                        <span>Send to a test address instead</span>
                      </label>
                      {useOverride && (
                        <input
                          className="ld-input le-override"
                          type="email"
                          placeholder="you@example.com"
                          value={overrideTo}
                          onChange={(e) => rememberOverride(e.target.value)}
                          aria-label="Test address"
                          autoFocus
                        />
                      )}
                    </>
                  )}
                </section>
              </div>

              {/* Right: the message on show, exactly as it will go out, or one of its attachments. */}
              <section className={cx(styles.section, "le-preview")}>
                <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
                  {single ? (
                    <span>Preview</span>
                  ) : (
                    <span className="ld-tabs le-tabs" role="tablist">
                      {messages.map((m) => (
                        <button
                          key={m.key}
                          type="button"
                          role="tab"
                          aria-selected={m.key === current.key}
                          className={cx("ld-tab", m.key === current.key && "active")}
                          title={`${m.who}: ${m.name}`}
                          onClick={() => showTab(m.key)}
                        >
                          <span className="le-tab-text">
                            {m.who === "Haulier" ? "Haulier" : `Customer · ${m.name || "?"}`}
                          </span>
                        </button>
                      ))}
                    </span>
                  )}
                </h3>
                <Field
                  label="From"
                  value={
                    preview.fromAddress
                      ? `${preview.fromName ? preview.fromName + " " : ""}<${preview.fromAddress}>`
                      : null
                  }
                />
                <Field
                  label="To"
                  value={
                    useOverride ? (
                      looksLikeEmail(overrideTo) ? (
                        overrideTo.trim()
                      ) : null
                    ) : ticked(current).length > 0 ? (
                      ticked(current).join(", ")
                    ) : (
                      <span className="le-muted">Nobody ticked. This email will not be sent.</span>
                    )
                  }
                />
                <Field label="Subject" value={current.subject} />
                {attachments.length > 0 && (
                  <Field
                    label="Attached"
                    value={
                      <span className="le-attach">
                        {attachments.map((a) => (
                          <button
                            key={attKey(a)}
                            type="button"
                            className={cx(
                              "le-attach-item",
                              (a.kind === "invoice" || a.kind === "preinvoice") && "le-attach-invoice",
                              viewing === attKey(a) && "le-attach-on",
                            )}
                            title={`Show this ${attName(a)}`}
                            onClick={() => void showAttachment(a)}
                          >
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                              <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z" />
                              <path d="M14 3v5h5" />
                            </svg>
                            {a.fileName}
                          </button>
                        ))}
                      </span>
                    }
                  />
                )}
                {viewing == null ? (
                  <iframe
                    key={current.key}
                    className="le-frame"
                    title="Email preview"
                    sandbox=""
                    srcDoc={current.html}
                  />
                ) : (
                  <>
                    <div className="le-viewing">
                      <span>Showing {viewingName}</span>
                      <button type="button" className={styles.inlineLink} onClick={() => setViewing(null)}>
                        back to the message
                      </button>
                    </div>
                    {viewError ? (
                      <div className="le-frame le-frame-msg">{viewError}</div>
                    ) : pdfUrls[viewing] ? (
                      // Not sandboxed: a sandboxed frame will not run the PDF viewer, and this
                      // is our own file.
                      <iframe className="le-frame" title={viewingName} src={pdfUrls[viewing]} />
                    ) : (
                      <div className="le-frame le-frame-msg">
                        Rendering the {viewingAtt ? attName(viewingAtt) : "document"}…
                      </div>
                    )}
                  </>
                )}
              </section>
            </div>
          </div>
        )}

        {jobs && (
          <div className="le-sheet" role="status" aria-live="polite">
            <div className="le-sheet-card">
              <h3 className="le-sheet-title">
                {sending
                  ? "Sending…"
                  : jobs.every((j) => j.status === "sent")
                    ? "All sent"
                    : "Finished, with problems"}
              </h3>
              <ul className="le-jobs">
                {jobs.map((j, i) => (
                  <li key={i} className={`le-job le-job-${j.status}`}>
                    <span className="le-job-icon" aria-hidden>
                      {j.status === "sent" ? (
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
                          <path d="M5 13l4 4L19 7" />
                        </svg>
                      ) : j.status === "failed" ? (
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round">
                          <path d="M6 6l12 12M18 6L6 18" />
                        </svg>
                      ) : j.status === "sending" ? (
                        <span className="le-spinner" />
                      ) : (
                        <span className="le-dot" />
                      )}
                    </span>
                    <span className="le-job-text">
                      {j.who && (
                        <span className="le-job-who">
                          <span className={whoTag(j.who)}>{j.who}</span>
                          <span>{j.name}</span>
                        </span>
                      )}
                      <span className="le-job-to">{j.label}</span>
                      <span className="le-job-state">{jobState(j)}</span>
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
}
