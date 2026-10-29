import { useState } from "react";
import Modal from "../../components/Modal";

interface Props {
  onSend: (to: string) => Promise<void>; // rejects with the reason it didn't go
  onClose: () => void;
}

// Super Admin > Email Setup > Send Test Email: who to send it to. Stays open on a failure
// so the address can be corrected, or the settings behind it.
export default function SendTestEmailModal({ onSend, onClose }: Props) {
  const [to, setTo] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const valid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(to.trim());

  async function send() {
    if (!valid || busy) return;
    setBusy(true);
    setError(null);
    try {
      await onSend(to.trim());
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title="Send Test Email"
      width={440}
      onClose={onClose}
      footer={
        <>
          <button className="toolbar-btn" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn-blue" disabled={!valid || busy} onClick={send}>
            {busy ? "Sending…" : "Send"}
          </button>
        </>
      }
    >
      <div className="form-field">
        <label className="form-label">Send to</label>
        <input
          className="form-input"
          type="email"
          value={to}
          autoFocus
          placeholder="name@example.com"
          onChange={(e) => setTo(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") void send();
          }}
        />
      </div>
      {error && (
        <p style={{ color: "#dc2626", fontSize: 13, margin: "10px 0 0", lineHeight: 1.5 }}>{error}</p>
      )}
    </Modal>
  );
}
