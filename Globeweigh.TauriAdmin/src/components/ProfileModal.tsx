import { useState } from "react";
import Modal from "./Modal";
import { AdminUserDto, fullName, initials } from "../types";
import { changePassword, updateSessionUser } from "../api/auth";
import { ApiError } from "../api/client";

interface Props {
  user: AdminUserDto;
  onClose: () => void;
  onUserUpdated: (user: AdminUserDto) => void;
}

// Self-service profile dialog — the WPF "My Account" screen reached by clicking the
// logged-in user name. Lets the user change their password and Touch passcode.
export default function ProfileModal({ user, onClose, onUserUpdated }: Props) {
  const [newPassword, setNewPassword] = useState("");
  const [oldPassword, setOldPassword] = useState("");
  const [passcode, setPasscode] = useState(user.passcode ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const changingPassword = newPassword.length > 0;
  const passcodeChanged = (passcode ?? "") !== (user.passcode ?? "");

  // Mirrors MyAccountViewModel.CanSavePassword: something must change, a new
  // password can't contain spaces and needs the old one confirmed, and a
  // passcode (when present) must be exactly 4 digits.
  const canSave =
    !busy &&
    (changingPassword || passcodeChanged) &&
    (!changingPassword || (!newPassword.includes(" ") && oldPassword.length > 0)) &&
    (passcode.length === 0 || /^\d{4}$/.test(passcode));

  async function onSave() {
    if (!canSave) return;
    setBusy(true);
    setError(null);
    try {
      const result = await changePassword(oldPassword, newPassword, passcode);
      const updated =
        updateSessionUser({ password: result.password, passcode: result.passcode }) ??
        { ...user, password: result.password, passcode: result.passcode };
      onUserUpdated(updated);
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to save changes.");
      setBusy(false);
    }
  }

  return (
    <Modal
      title="My Account"
      width={560}
      onClose={onClose}
      footer={
        saved ? (
          <button className="btn-blue" onClick={onClose}>
            Close
          </button>
        ) : (
          <>
            <button className="toolbar-btn" onClick={onClose}>
              Close
            </button>
            <button className="btn-blue" disabled={!canSave} onClick={onSave}>
              {busy ? "Saving…" : "Save Changes"}
            </button>
          </>
        )
      }
    >
      <div className="profile-header">
        <span className="profile-avatar">{initials(user)}</span>
        <div className="profile-identity">
          <span className="profile-name">{fullName(user)}</span>
          <span className="profile-email">{user.username}</span>
        </div>
      </div>

      {saved ? (
        <p className="profile-saved">Saved successfully.</p>
      ) : (
        <>
          <h3 className="po-side-title" style={{ marginTop: 16 }}>
            Security Settings
          </h3>

          <div className="form-field form-field-inline" style={{ marginTop: 8 }}>
            <label className="form-label">Email (Login)</label>
            <span className="form-static">{user.username}</span>
          </div>

          <div className="form-field form-field-inline" style={{ marginTop: 8 }}>
            <label className="form-label">New Password</label>
            <input
              className="form-input"
              type="password"
              autoComplete="new-password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
            />
          </div>

          <div className="form-field form-field-inline" style={{ marginTop: 8 }}>
            <label className="form-label">Old Password (Confirm)</label>
            <input
              className="form-input"
              type="password"
              autoComplete="current-password"
              value={oldPassword}
              onChange={(e) => setOldPassword(e.target.value)}
            />
          </div>

          <div className="form-field form-field-inline" style={{ marginTop: 8 }}>
            <label className="form-label">Passcode (Touch)</label>
            <input
              className="form-input"
              inputMode="numeric"
              maxLength={4}
              value={passcode}
              onChange={(e) => setPasscode(e.target.value.replace(/\D/g, ""))}
            />
            <span style={{ marginLeft: 8, opacity: 0.7, fontSize: 12 }}>(4 digits)</span>
          </div>

          {error && (
            <p style={{ color: "var(--danger, #c0392b)", marginTop: 12 }}>{error}</p>
          )}
        </>
      )}
    </Modal>
  );
}
