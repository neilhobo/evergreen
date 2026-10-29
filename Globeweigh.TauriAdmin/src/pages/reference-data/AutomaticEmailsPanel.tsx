import { useEffect, useMemo, useRef, useState } from "react";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  AutomaticEmail,
  AutomaticEmailKind,
  AutomaticEmailPreview,
  AutomaticEmailTexts,
  getAutomaticEmails,
  previewAutomaticEmail,
  saveAutomaticEmail,
} from "../../api/automaticEmails";
import s from "./AutomaticEmailsPanel.module.css";

// Admin > Automatic Emails: the wording of the two emails that go to the haulier — "ready for
// collection" (Loads grid) and the dispatch confirmation (Completed Loads grid). Each email is
// a fixed layout with a handful of pieces of prose in it; the pieces are edited here, one field
// each, with {Placeholders} for the load's details, and the email is shown beside the form as it
// would go out, on a made-up load, re-composed by the API as you type. The layout itself, the
// reference panel and the deliveries table are not editable, so a change cannot break the email.

// What every field of every email says on the form, keyed by email then field.
type Drafts = Record<string, AutomaticEmailTexts>;

const textsOf = (email: AutomaticEmail): AutomaticEmailTexts =>
  Object.fromEntries(email.fields.map((f) => [f.key, f.text]));

const sameTexts = (a: AutomaticEmailTexts, b: AutomaticEmailTexts) =>
  Object.keys(a).every((k) => a[k] === b[k]);

export default function AutomaticEmailsPanel() {
  const [emails, setEmails] = useState<AutomaticEmail[] | null>(null);
  const [kind, setKind] = useState<AutomaticEmailKind>("ready");
  const [drafts, setDrafts] = useState<Drafts>({});
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null);

  // The email as it would go out with what is on the form, and which face of it to show.
  const [preview, setPreview] = useState<AutomaticEmailPreview | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [view, setView] = useState<"html" | "text">("html");

  function apply(list: AutomaticEmail[]) {
    setEmails(list);
    setDrafts(Object.fromEntries(list.map((e) => [e.kind, textsOf(e)])));
  }

  useEffect(() => {
    getAutomaticEmails()
      .then(apply)
      .catch((err) => void messageDialog("Failed to load the automatic emails.\n" + (err as Error).message));
  }, []);

  const email = emails?.find((e) => e.kind === kind) ?? null;
  const draft = drafts[kind];
  const saved = useMemo(() => (email ? textsOf(email) : null), [email]);
  const dirty = draft != null && saved != null && !sameTexts(draft, saved);
  const subjectBlank = draft != null && draft.subject != null && draft.subject.trim() === "";
  const valid = !subjectBlank;

  function set(key: string, value: string) {
    setDrafts((d) => ({ ...d, [kind]: { ...d[kind], [key]: value } }));
    setResult(null);
  }

  // The preview follows the form, a beat behind the typing, and a reply from an earlier ask
  // that lands after a later one is dropped.
  const previewSeq = useRef(0);
  useEffect(() => {
    if (!draft) return;
    const seq = ++previewSeq.current;
    const timer = setTimeout(() => {
      previewAutomaticEmail(kind, draft)
        .then((p) => {
          if (seq !== previewSeq.current) return;
          setPreview(p);
          setPreviewError(null);
        })
        .catch((err) => {
          if (seq === previewSeq.current) setPreviewError((err as Error).message);
        });
    }, 300);
    return () => clearTimeout(timer);
  }, [kind, draft]);

  // A placeholder chip drops itself into the field last typed in, at the caret.
  const lastFocused = useRef<{ key: string; el: HTMLInputElement | HTMLTextAreaElement } | null>(null);
  function insertPlaceholder(name: string) {
    const target = lastFocused.current;
    if (!target || !draft) return;
    const { key, el } = target;
    const token = `{${name}}`;
    const current = draft[key] ?? "";
    const start = el.selectionStart ?? current.length;
    const end = el.selectionEnd ?? start;
    set(key, current.slice(0, start) + token + current.slice(end));
    // Back into the field, with the caret after what was dropped in.
    requestAnimationFrame(() => {
      el.focus();
      const at = start + token.length;
      el.setSelectionRange(at, at);
    });
  }

  async function onSave() {
    if (!email || !draft || !valid || busy) return;
    setBusy(true);
    try {
      const updated = await saveAutomaticEmail(kind, draft);
      setEmails((list) => list?.map((e) => (e.kind === updated.kind ? updated : e)) ?? null);
      setDrafts((d) => ({ ...d, [kind]: textsOf(updated) }));
      setResult({ ok: true, text: "Saved. The next email of this kind goes out with this wording." });
    } catch (err) {
      setResult({ ok: false, text: (err as Error).message });
    } finally {
      setBusy(false);
    }
  }

  async function onResetAll() {
    if (!email) return;
    if (!(await confirmDialog(`Put every piece of "${email.title}" back to its default wording?`))) return;
    setDrafts((d) => ({
      ...d,
      [kind]: Object.fromEntries(email.fields.map((f) => [f.key, f.default])),
    }));
    setResult(null);
  }

  const atDefaults = email != null && draft != null && email.fields.every((f) => draft[f.key] === f.default);

  // Not on the legacy .po-main class: App.css is bundled after this module and its
  // .po-main stacks children in a column, which would put the preview under the form.
  return (
    <div className={s.wrap}>
      <div className={s.editor}>
        <h2 className={s.title}>Automatic Emails</h2>

        {/* Which email the form below is about: one radio per email, stacked. */}
        {emails && (
          <div className={s.pick} role="radiogroup" aria-label="Email">
            {emails.map((e) => (
              // When and to whom it goes, on hover: not worth a paragraph on the pane.
              <label key={e.kind} className={s.pickRow} title={e.description}>
                <input
                  type="radio"
                  name="ae-kind"
                  value={e.kind}
                  checked={e.kind === kind}
                  onChange={() => {
                    setKind(e.kind);
                    setResult(null);
                  }}
                />
                {e.title}
              </label>
            ))}
          </div>
        )}

        {!email || !draft ? (
          <p className={s.note}>{emails ? "" : "Loading…"}</p>
        ) : (
          <>
            <div className={s.chipsHead}>
              <span className={s.chipsTitle}>Variables</span>
              <span className={s.hint}>Click one to add it to the field you were typing in.</span>
            </div>
            <div className={s.chips}>
              {email.placeholders.map((p) => (
                <button
                  key={p.name}
                  type="button"
                  className={s.chip}
                  title={`${p.meaning}. Click to add it to the field you were typing in.`}
                  onClick={() => insertPlaceholder(p.name)}
                >
                  <span className={s.chipName}>{`{${p.name}}`}</span>
                  <span>{p.meaning}</span>
                </button>
              ))}
            </div>

            {email.fields.map((f) => {
              const value = draft[f.key] ?? "";
              const changed = value !== f.default;
              const remember = (el: HTMLInputElement | HTMLTextAreaElement) => {
                lastFocused.current = { key: f.key, el };
              };
              return (
                <div key={f.key} className={`form-field ${s.field}`}>
                  {/* One line above the box: the label, what the piece is for, and the way back
                      to the default once it has been changed. */}
                  <div className={s.fieldHead}>
                    <label className={`form-label ${s.label}`} htmlFor={`ae-${kind}-${f.key}`}>
                      {f.label}
                    </label>
                    {f.hint && (
                      <span className={s.hint} title={f.hint}>
                        {f.hint}
                      </span>
                    )}
                    {changed && (
                      <button
                        type="button"
                        className={s.reset}
                        title={`Default: ${f.default || "(nothing)"}`}
                        onClick={() => set(f.key, f.default)}
                      >
                        Reset to default
                      </button>
                    )}
                  </div>
                  {f.multiline ? (
                    <textarea
                      id={`ae-${kind}-${f.key}`}
                      className="form-input form-textarea"
                      rows={2}
                      value={value}
                      onFocus={(e) => remember(e.currentTarget)}
                      onChange={(e) => set(f.key, e.target.value)}
                    />
                  ) : (
                    <input
                      id={`ae-${kind}-${f.key}`}
                      className="form-input"
                      value={value}
                      onFocus={(e) => remember(e.currentTarget)}
                      onChange={(e) => set(f.key, e.target.value)}
                    />
                  )}
                </div>
              );
            })}

            {subjectBlank && <p className={s.problem}>The subject cannot be blank.</p>}

            <div className={s.buttons}>
              <button
                className="btn-blue"
                style={{ width: 120 }}
                disabled={!dirty || !valid || busy}
                onClick={onSave}
              >
                {busy ? "Saving…" : "Save"}
              </button>
              <button className="toolbar-btn" disabled={atDefaults || busy} onClick={onResetAll}>
                Reset all to default
              </button>
            </div>

            {result && <p className={result.ok ? s.ok : s.problem}>{result.text}</p>}
            {dirty && !result && (
              <p className={`${s.note} ${s.field}`}>
                Unsaved changes. The preview shows them; emails keep going out with the saved
                wording until you save.
              </p>
            )}
          </>
        )}
      </div>

      <div className={s.preview}>
        <div className={s.previewHead}>
          <h3 className={s.previewTitle}>Preview</h3>
          <span className={s.previewSample}>On a made-up load, with the wording as typed</span>
          <div className={s.view}>
            <button
              type="button"
              className={view === "html" ? `${s.viewBtn} ${s.viewBtnOn}` : s.viewBtn}
              onClick={() => setView("html")}
            >
              Email
            </button>
            <button
              type="button"
              className={view === "text" ? `${s.viewBtn} ${s.viewBtnOn}` : s.viewBtn}
              onClick={() => setView("text")}
              title="The plain-text version, for mail readers that do not show HTML"
            >
              Plain text
            </button>
          </div>
        </div>
        {preview && (
          <p className={s.subject}>
            <span className={s.subjectLabel}>Subject</span>
            <span>{preview.subject}</span>
          </p>
        )}
        {previewError ? (
          <div className={s.frameMsg}>{previewError}</div>
        ) : !preview ? (
          <div className={s.frameMsg}>Composing the preview…</div>
        ) : view === "html" ? (
          <iframe className={s.frame} title="Email preview" sandbox="" srcDoc={preview.html} />
        ) : (
          <pre className={s.plain}>{preview.text}</pre>
        )}
      </div>
    </div>
  );
}
