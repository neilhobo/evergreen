import { useState } from "react";
import styles from "./SwitchUser.module.css";
import { switchBack } from "../api/auth";
import { ApiError } from "../api/client";
import { useSession } from "../session";
import { fullName, isSwitchedUser } from "../types";

// Shown only while an admin is switched to another user: makes that obvious, and offers a
// one-click return to the admin's own account.
export default function SwitchUserBanner() {
  const { user, switchUser } = useSession();
  const [returning, setReturning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!isSwitchedUser(user)) return null;

  async function handleReturn() {
    setReturning(true);
    setError(null);
    try {
      switchUser(await switchBack());
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Could not return to your account.");
      // Never leave the button stuck — the bar stays and the user can retry.
      setReturning(false);
    }
  }

  return (
    <div className={styles.bar} role="status">
      <span className={styles.barIcon} aria-hidden>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
          <circle cx="12" cy="7" r="4" />
        </svg>
      </span>
      <span className={styles.barText}>
        Viewing as <strong>{fullName(user) || user.username}</strong>
        {user.impersonatedByName ? ` — switched by ${user.impersonatedByName}` : ""}
      </span>
      {error && <span className={styles.barError}>{error}</span>}
      <button type="button" className={styles.returnBtn} onClick={() => void handleReturn()} disabled={returning}>
        {returning ? "Returning…" : "Return to my account"}
      </button>
    </div>
  );
}
