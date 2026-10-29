import { useEffect, useState } from "react";
import Modal from "./Modal";
import styles from "./SwitchUser.module.css";
import { SwitchUserRow, getSwitchUsers, switchToUser } from "../api/auth";
import { ApiError } from "../api/client";
import { roleLabel } from "../api/staff";
import { useSession } from "../session";

interface Props {
  onClose: () => void;
}

const displayName = (u: SwitchUserRow) => [u.firstName, u.surname].filter(Boolean).join(" ").trim();

// Admin "Switch user" picker (ported from ClonakiltyFoods' Impersonate user). Choosing a user
// signs in as them, no password; the orange bar then offers the way back.
export default function SwitchUserDialog({ onClose }: Props) {
  const { switchUser } = useSession();
  const [users, setUsers] = useState<SwitchUserRow[] | null>(null);
  const [filter, setFilter] = useState("");
  const [switchingId, setSwitchingId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    getSwitchUsers()
      .then((u) => !cancelled && setUsers(u))
      .catch((e) => {
        if (cancelled) return;
        setUsers([]);
        setError(e instanceof ApiError ? e.message : "Could not load users.");
      });
    return () => {
      cancelled = true;
    };
  }, []);

  async function pick(u: SwitchUserRow) {
    setSwitchingId(u.id);
    setError(null);
    try {
      const session = await switchToUser(u.id);
      onClose();
      switchUser(session);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Could not switch to this user.");
      setSwitchingId(null);
    }
  }

  const q = filter.trim().toLowerCase();
  const shown = (users ?? []).filter(
    (u) =>
      !q ||
      (u.username ?? "").toLowerCase().includes(q) ||
      displayName(u).toLowerCase().includes(q),
  );

  return (
    <Modal title="Switch user" width={520} height={620} onClose={onClose}>
      <div className={styles.dialog}>
        <p className={styles.subtitle}>Sign in as another user — no password required.</p>
        <input
          className={styles.filter}
          placeholder="Filter users…"
          value={filter}
          autoFocus
          onChange={(e) => setFilter(e.target.value)}
        />
        {error && <p className={styles.error}>{error}</p>}
        <div className={styles.list}>
          {users === null && <div className={styles.muted}>Loading…</div>}
          {users !== null && shown.length === 0 && <div className={styles.muted}>No users.</div>}
          {shown.map((u) => {
            const name = displayName(u);
            const role = u.roleIds.length ? u.roleIds.map(roleLabel).join(", ") : null;
            return (
              <button
                key={u.id}
                type="button"
                className={styles.userRow}
                disabled={u.disabled || switchingId != null}
                onClick={() => void pick(u)}
                title={u.disabled ? "Disabled — can't sign in" : `Sign in as ${name || u.username}`}
              >
                <span className={styles.userMeta}>
                  <span className={styles.userName}>{name || u.username}</span>
                  {name && u.username && <span className={styles.userEmail}>{u.username}</span>}
                </span>
                <span className={styles.userActions}>
                  {role && <span className={styles.roleTag}>{role}</span>}
                  {u.disabled ? (
                    <span className={styles.disabledTag}>Disabled</span>
                  ) : (
                    <span className={styles.switchTag}>{switchingId === u.id ? "Switching…" : "Switch"}</span>
                  )}
                </span>
              </button>
            );
          })}
        </div>
      </div>
    </Modal>
  );
}
