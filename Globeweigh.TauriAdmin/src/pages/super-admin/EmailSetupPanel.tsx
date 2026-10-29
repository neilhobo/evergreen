import { useEffect, useState } from "react";
import { messageDialog } from "../../components/confirmDialog";
import {
  EmailSettings,
  EmailSettingsInput,
  SmtpSecurity,
  getEmailSettings,
  saveEmailSettings,
  sendTestEmail,
  testEmailSettings,
} from "../../api/settings";
import SendTestEmailModal from "./SendTestEmailModal";

// Super Admin > Email Setup: the SMTP account automatic emails go out through. Test and
// Send Test Email try the details as typed — a blank password means the saved one — so a
// change can be proved before it is saved.

// The form as typed. Port stays a string until it is sent, like the order number does.
interface Form {
  host: string;
  port: string;
  security: SmtpSecurity;
  username: string;
  password: string; // only ever what has been typed this visit — never the saved one
  fromAddress: string;
  fromName: string;
}

const SECURITY_OPTIONS: { value: SmtpSecurity; label: string }[] = [
  { value: "None", label: "None" },
  { value: "StartTls", label: "TLS (STARTTLS)" },
  { value: "Ssl", label: "SSL" },
];

function formFrom(s: EmailSettings): Form {
  return {
    host: s.host ?? "",
    port: s.port === null ? "" : String(s.port),
    security: s.security,
    username: s.username ?? "",
    password: "",
    fromAddress: s.fromAddress ?? "",
    fromName: s.fromName ?? "",
  };
}

const EMPTY_FORM: Form = formFrom({
  host: null,
  port: null,
  security: "StartTls",
  username: null,
  hasPassword: false,
  fromAddress: null,
  fromName: null,
});

export default function EmailSetupPanel() {
  const [loaded, setLoaded] = useState<EmailSettings | null>(null);
  const [form, setForm] = useState<Form>(EMPTY_FORM);
  const [busy, setBusy] = useState<"save" | "test" | null>(null);
  // The outcome of the last save / test / send, under the buttons.
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null);
  const [sendOpen, setSendOpen] = useState(false);

  function apply(s: EmailSettings) {
    setLoaded(s);
    setForm(formFrom(s));
  }

  useEffect(() => {
    getEmailSettings()
      .then(apply)
      .catch((err) => void messageDialog("Failed to load email settings.\n" + (err as Error).message));
  }, []);

  function set<K extends keyof Form>(key: K, value: Form[K]) {
    setForm((f) => ({ ...f, [key]: value }));
    setResult(null);
  }

  const portText = form.port.trim();
  const port = /^\d+$/.test(portText) ? parseInt(portText, 10) : NaN;
  const portValid = port >= 1 && port <= 65535;
  const hostValid = form.host.trim() !== "";
  const fromValid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.fromAddress.trim());
  const valid = hostValid && portValid && fromValid;

  const dirty =
    loaded !== null &&
    (form.password !== "" ||
      JSON.stringify({ ...form, password: "" }) !== JSON.stringify(formFrom(loaded)));

  // A username with no password anywhere is the one thing the server can't try.
  const needsPassword = form.username.trim() !== "" && form.password === "" && !loaded?.hasPassword;

  const input = (): EmailSettingsInput => ({
    host: form.host.trim(),
    port: portValid ? port : null,
    security: form.security,
    username: form.username.trim(),
    password: form.password,
    fromAddress: form.fromAddress.trim(),
    fromName: form.fromName.trim(),
  });

  async function onSave() {
    if (!valid || busy) return;
    setBusy("save");
    try {
      apply(await saveEmailSettings(input()));
      setResult({ ok: true, text: "Saved." });
    } catch (err) {
      setResult({ ok: false, text: (err as Error).message });
    } finally {
      setBusy(null);
    }
  }

  async function onTest() {
    if (!valid || busy) return;
    setBusy("test");
    try {
      const { message } = await testEmailSettings(input());
      setResult({ ok: true, text: message });
    } catch (err) {
      setResult({ ok: false, text: (err as Error).message });
    } finally {
      setBusy(null);
    }
  }

  async function onSendTest(to: string) {
    const { message } = await sendTestEmail(input(), to);
    setSendOpen(false);
    setResult({ ok: true, text: message });
  }

  const note = { color: "#64748b", fontSize: 13, margin: "0 0 16px", lineHeight: 1.5 };
  const problem = { color: "#dc2626", fontSize: 13, margin: "10px 0 0" };

  return (
    <div className="po-main">
      <div style={{ padding: 18, maxWidth: 620 }}>
        <h2 style={{ fontSize: 15, fontWeight: 600, margin: "0 0 4px" }}>Email Setup</h2>
        <p style={note}>
          The account automatic emails are sent from. Test tries the details below without
          sending anything; Send Test Email sends a short message to an address you choose.
        </p>

        <div className="form-field">
          <label className="form-label">SMTP server</label>
          <input
            className="form-input"
            value={form.host}
            placeholder="e.g. mail-eu.smtp2go.com"
            onChange={(e) => set("host", e.target.value)}
          />
        </div>

        <div style={{ display: "flex", gap: 18, marginTop: 8 }}>
          <div className="form-field" style={{ width: 120 }}>
            <label className="form-label">Port</label>
            <input
              className="form-input"
              value={form.port}
              inputMode="numeric"
              placeholder="e.g. 2525"
              onChange={(e) => set("port", e.target.value)}
            />
          </div>
          <div className="form-field" style={{ width: 200 }}>
            <label className="form-label">Security</label>
            <select
              className="form-input"
              value={form.security}
              onChange={(e) => set("security", e.target.value as SmtpSecurity)}
            >
              {SECURITY_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="form-field" style={{ marginTop: 8 }}>
          <label className="form-label">Username</label>
          <input
            className="form-input"
            value={form.username}
            autoComplete="off"
            placeholder="Leave blank if the server doesn't need a sign-in"
            onChange={(e) => set("username", e.target.value)}
          />
        </div>

        <div className="form-field" style={{ marginTop: 8 }}>
          <label className="form-label">Password</label>
          <input
            className="form-input"
            type="password"
            value={form.password}
            autoComplete="new-password"
            placeholder={
              loaded?.hasPassword ? "•••••••• (saved — leave blank to keep it)" : "Not saved"
            }
            onChange={(e) => set("password", e.target.value)}
          />
        </div>

        <div style={{ display: "flex", gap: 18, marginTop: 8 }}>
          <div className="form-field" style={{ flex: 1 }}>
            <label className="form-label">From address</label>
            <input
              className="form-input"
              type="email"
              value={form.fromAddress}
              placeholder="e.g. noreply@example.co.uk"
              onChange={(e) => set("fromAddress", e.target.value)}
            />
          </div>
          <div className="form-field" style={{ flex: 1 }}>
            <label className="form-label">From name</label>
            <input
              className="form-input"
              value={form.fromName}
              placeholder="Shown as the sender, optional"
              onChange={(e) => set("fromName", e.target.value)}
            />
          </div>
        </div>

        {form.host !== "" && !hostValid && <p style={problem}>Enter the SMTP server.</p>}
        {portText !== "" && !portValid && <p style={problem}>Enter a port between 1 and 65535.</p>}
        {form.fromAddress !== "" && !fromValid && <p style={problem}>Enter a valid From address.</p>}
        {needsPassword && (
          <p style={problem}>No password is saved on this server — enter it before testing.</p>
        )}

        <div style={{ display: "flex", gap: 8, marginTop: 16 }}>
          <button
            className="btn-blue"
            style={{ width: 120 }}
            disabled={!dirty || !valid || busy !== null}
            onClick={onSave}
          >
            {busy === "save" ? "Saving…" : "Save"}
          </button>
          <button
            className="toolbar-btn"
            disabled={!valid || needsPassword || busy !== null}
            onClick={onTest}
            title="Connect and sign in with the details above, without sending anything"
          >
            {busy === "test" ? "Testing…" : "Test"}
          </button>
          <button
            className="toolbar-btn"
            disabled={!valid || needsPassword || busy !== null}
            onClick={() => setSendOpen(true)}
          >
            Send Test Email…
          </button>
        </div>

        {result && (
          <p
            style={{
              color: result.ok ? "#15803d" : "#dc2626",
              fontSize: 13,
              marginTop: 12,
              lineHeight: 1.5,
            }}
          >
            {result.text}
          </p>
        )}

        {dirty && valid && (
          <p style={{ ...note, marginTop: 12 }}>
            Unsaved changes — Test and Send Test Email use what is on the form, so a change
            can be tried before it is saved.
          </p>
        )}
      </div>

      {sendOpen && <SendTestEmailModal onSend={onSendTest} onClose={() => setSendOpen(false)} />}
    </div>
  );
}
