import { ReactNode, useEffect, useRef, useState } from "react";
import styles from "./PageHeader.module.css";
import { useSession } from "../session";
import { UserRole, fullName, hasRole, initials, isSwitchedUser } from "../types";
import ProfileModal from "../components/ProfileModal";
import SwitchUserDialog from "../components/SwitchUserDialog";
import { switchBack } from "../api/auth";

interface Props {
  title: string;
  meta?: ReactNode;
  center?: ReactNode;
  centerAlign?: "left" | "right";
}

// Joins the class names that are set (the one thing clsx would otherwise be pulled in for).
function cx(...names: Array<string | false | null | undefined>): string {
  return names.filter(Boolean).join(" ");
}

// The header of a content card: accent rail + title
// + meta on the left, an optional centre slot (page tabs / read-outs), and the signed-in
// profile chip with its account menu (My Account / Switch user / Log out) on the right.
export default function PageHeader({ title, meta, center, centerAlign = "right" }: Props) {
  const { user, signOut, updateUser, switchUser } = useSession();
  const [open, setOpen] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const [switchOpen, setSwitchOpen] = useState(false);
  const switched = isSwitchedUser(user);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  const name = fullName(user) || "Administrator";
  const email = user.username ?? "";
  const alignLeft = centerAlign === "left";

  return (
    <header className={cx(styles.header, alignLeft && styles.leftMode)}>
      <div className={styles.titleRegion}>
        <span className={styles.title}>{title}</span>
        {meta != null && <span className={styles.meta}>{meta}</span>}
      </div>

      {center && <div className={cx(styles.center, alignLeft && styles.centerLeft)}>{center}</div>}

      <div className={styles.profile} ref={ref}>
        <button
          type="button"
          className={styles.chip}
          onClick={() => setOpen((v) => !v)}
          aria-haspopup="menu"
          aria-expanded={open}
          title={name}
        >
          <span className={styles.initials}>{initials(user)}</span>
          <span className={styles.userText}>
            <span className={styles.userName}>{name}</span>
            <span className={styles.userSub}>{email}</span>
          </span>
          <svg
            className={styles.caret}
            width="13"
            height="13"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.5"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden
          >
            <path d="M6 9l6 6 6-6" />
          </svg>
        </button>

        {open && (
          <div className={styles.menu} role="menu">
            <div className={styles.menuHead}>
              <span className={styles.menuName}>{name}</span>
              <span className={styles.menuEmail}>{email}</span>
            </div>
            {/* Not while switched: that would be someone else's password. */}
            {!switched && (
              <button
                type="button"
                role="menuitem"
                className={styles.menuItem}
                onClick={() => {
                  setOpen(false);
                  setProfileOpen(true);
                }}
              >
                My Account
              </button>
            )}
            {/* Admins only (the API re-checks). No switching from inside a switch —
                return to your own account first, from here or the orange bar. */}
            {!switched && hasRole(user, UserRole.GlobeweighAdmin, UserRole.OfficeAdmin) && (
              <button
                type="button"
                role="menuitem"
                className={styles.menuItem}
                onClick={() => {
                  setOpen(false);
                  setSwitchOpen(true);
                }}
              >
                Switch user
              </button>
            )}
            {switched && (
              <button
                type="button"
                role="menuitem"
                className={styles.menuItem}
                onClick={() => {
                  setOpen(false);
                  void switchBack().then(switchUser, () => undefined);
                }}
              >
                Return to my account
              </button>
            )}
            <button
              type="button"
              role="menuitem"
              className={cx(styles.menuItem, styles.menuItemDanger)}
              onClick={() => {
                setOpen(false);
                signOut();
              }}
            >
              Log out
            </button>
          </div>
        )}
      </div>

      {profileOpen && (
        <ProfileModal user={user} onClose={() => setProfileOpen(false)} onUserUpdated={updateUser} />
      )}
      {switchOpen && <SwitchUserDialog onClose={() => setSwitchOpen(false)} />}
    </header>
  );
}
