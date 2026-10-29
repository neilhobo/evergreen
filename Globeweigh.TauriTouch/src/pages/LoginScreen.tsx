import { useEffect, useState } from "react";
import { exit } from "@tauri-apps/plugin-process";
import { AdminUserDto } from "../types";
import { getTouchUsers, touchLogin, TouchUser } from "../api/auth";
import { DEV_AUTO_LOGIN } from "../config";
import TopBar from "../components/touch/TopBar";

interface Props {
  onLoggedIn: (user: AdminUserDto) => void;
}

// Dev auto-login fires ONCE per app launch. Sign-out just sets user=null (no page reload),
// so this module-level flag survives the LoginScreen remount and the picker is shown instead —
// letting you log in as someone else. A full relaunch resets it, auto-logging in again.
let autoLoginConsumed = false;

// Touch login: pick a factory user, enter the numeric passcode on the on-screen keypad.
// Ports the WPF LoginViewModel (user list + masked passcode + "Invalid passcode").
export default function LoginScreen({ onLoggedIn }: Props) {
  const [users, setUsers] = useState<TouchUser[]>([]);
  const [selected, setSelected] = useState<TouchUser | null>(null);
  const [passcode, setPasscode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const list = await getTouchUsers();
        setUsers(list);
        // Dev-only auto-login; a failure (user missing, wrong passcode) just
        // falls through to the normal picker.
        const auto = DEV_AUTO_LOGIN;
        if (auto && !autoLoginConsumed) {
          autoLoginConsumed = true; // fire at most once per launch, even if it fails below
          const match = list.find(
            (u) => u.fullName?.trim().toLowerCase() === auto.fullName.toLowerCase()
          );
          if (match) {
            try {
              onLoggedIn(await touchLogin(match.id, auto.passcode));
              return;
            } catch {
              /* fall through to manual login */
            }
          }
        }
      } catch (err) {
        setError("Could not load users.\n" + (err as Error).message);
      }
    })();
  }, []);

  function pick(u: TouchUser) {
    setSelected(u);
    setPasscode("");
    setError(null);
  }

  // Auto-submit once a full 4-digit passcode is entered — no need to press Sign In.
  // A wrong code is rejected by submit() (clears passcode + shows error) so the user re-enters.
  useEffect(() => {
    if (selected && passcode.length === 4 && !busy) {
      submit();
    }
  }, [passcode, selected, busy]);

  // Secret login: Ctrl+Enter with a user selected signs them in without the passcode.
  // Not shown anywhere on screen; the API only honours it on the LAN (not via the tunnel).
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.ctrlKey && e.key === "Enter" && selected && !busy) {
        e.preventDefault();
        submit(true);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [selected, busy, passcode]);

  async function submit(skipPasscode = false) {
    if (!selected) return;
    setBusy(true);
    setError(null);
    try {
      const user = await touchLogin(selected.id, passcode, skipPasscode);
      onLoggedIn(user);
    } catch (err) {
      setError((err as Error).message || "Invalid passcode");
      setPasscode("");
      setBusy(false);
    }
  }

  const masked = "•".repeat(passcode.length);
  const padKeys = ["1", "2", "3", "4", "5", "6", "7", "8", "9"];

  return (
    <div className="tt-app">
      <TopBar title="Sign In" onExit={() => exit(0)} />
      <div className="tt-body">
        <div style={{ display: "grid", gridTemplateColumns: "1fr 460px", gap: 26, height: "100%" }}>
          {/* User list */}
          <div className="tt-panel" style={{ overflow: "auto" }}>
            <div className="tt-col-title">Select User</div>
            <div className="tt-list">
              {users.map((u) => (
                <div
                  key={u.id}
                  className={`tt-list-item${selected?.id === u.id ? " tt-selected" : ""}`}
                  onClick={() => pick(u)}
                >
                  {u.fullName}
                </div>
              ))}
              {users.length === 0 && <div className="tt-note">Loading users…</div>}
            </div>
          </div>

          {/* Passcode keypad — only once a user is chosen; otherwise prompt to select one */}
          <div className="tt-panel" style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            {!selected ? (
              <div className="tt-center" style={{ height: "100%", gap: 16 }}>
                <div className="tt-select-user-ico">👤</div>
                <div className="tt-note" style={{ fontSize: 18 }}>Select a user to continue</div>
              </div>
            ) : (
              <>
                <div className="tt-col-title">Passcode · {selected.fullName}</div>
                <div className="tt-pad-display">{masked}</div>
                <div className="tt-pad-error">{error}</div>
                <div className="tt-pad-grid">
                  {padKeys.map((k) => (
                    <button key={k} className="tt-key" disabled={busy} onClick={() => setPasscode((v) => (v.length >= 4 ? v : v + k))}>
                      {k}
                    </button>
                  ))}
                  <button className="tt-key" disabled={busy} onClick={() => setPasscode((v) => v.slice(0, -1))}>⌫</button>
                  <button className="tt-key" disabled={busy} onClick={() => setPasscode((v) => (v.length >= 4 ? v : v + "0"))}>0</button>
                  <button className="tt-key" disabled={busy} onClick={() => setPasscode("")}>C</button>
                  <button className="tt-key tt-ok" style={{ gridColumn: "span 3" }} disabled={busy} onClick={() => submit()}>
                    {busy ? "…" : "Sign In"}
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
