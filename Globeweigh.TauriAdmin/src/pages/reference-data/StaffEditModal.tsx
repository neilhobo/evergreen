import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import {
  ASSIGNABLE_ROLES,
  StaffInput,
  StaffRow,
  UserType,
  createStaff,
  updateStaff,
} from "../../api/staff";
import {
  LeaveCategory,
  LeaveProfile,
  fmtDays,
  getLeaveApprovers,
  getLeaveCategories,
  getLeaveProfile,
  saveLeaveProfile,
} from "../../api/annualLeave";
import styles from "../../widgets/DetailPanel.module.css";
import { cx } from "../../widgets/DetailPanel";
import "../dispatch/Dispatch.css";

interface Props {
  existing: StaffRow | null; // null = new
  onClose: (saved: boolean) => void;
  /** Whoever is signed in can manage Annual Leave: show (and save) the Annual Leave section. */
  canManageLeave?: boolean;
}

const NO_LEAVE: LeaveProfile = {
  enrolled: false,
  categoryId: null,
  startDate: null,
  endDate: null,
  email: null,
  mobile: null,
  approverUserId: null,
  notes: null,
};

const USER_TYPES: { id: number; label: string }[] = [
  { id: UserType.Office, label: "Office" },
  { id: UserType.Touch, label: "Touch" },
  { id: UserType.Both, label: "Both" },
  { id: UserType.NoLogin, label: "No login" },
];

// Mirrors AddEditGlobeweighUserView — add/edit a staff/login user.
// The person is the Staff user; Annual Leave keeps only their leave profile, set here (the
// section shows only to whoever can manage Annual Leave). "No login" is for staff who are in
// Annual Leave but never sign in to anything: drivers, the factory floor.
export default function StaffEditModal({ existing, onClose, canManageLeave = false }: Props) {
  const isNew = existing === null;
  const [userTypeId, setUserTypeId] = useState(existing?.userTypeId ?? UserType.Both);
  const [firstName, setFirstName] = useState(existing?.firstName ?? "");
  const [surname, setSurname] = useState(existing?.surname ?? "");
  const [username, setUsername] = useState(existing?.username ?? "");
  const [password, setPassword] = useState(existing?.password ?? "");
  const [passcode, setPasscode] = useState(existing?.passcode ?? "");
  const [disabled, setDisabled] = useState(existing?.disabled ?? false);
  // Not shown or edited (nothing reads it); the stored value is sent back unchanged.
  const costingAccess = existing?.costingAccess ?? false;
  // One role per user. Retired roles (Factory User, Weighbridge User) are dropped on load, so a
  // user who only held one of those has to be given a current role before saving.
  const [roleId, setRoleId] = useState<number>(
    (existing?.roleIds ?? []).find((id) => ASSIGNABLE_ROLES.some((r) => r.id === id)) ?? 0
  );
  const [busy, setBusy] = useState(false);
  // Annual Leave: the profile as loaded (null until it is), what is on screen, and the lists.
  const [leaveLoaded, setLeaveLoaded] = useState<LeaveProfile | null>(null);
  const [leave, setLeave] = useState<LeaveProfile>(NO_LEAVE);
  const [leaveCats, setLeaveCats] = useState<LeaveCategory[]>([]);
  const [approvers, setApprovers] = useState<{ id: number; name: string }[]>([]);

  useEffect(() => {
    if (!canManageLeave) return;
    getLeaveCategories(new Date().getFullYear()).then(setLeaveCats).catch(() => {});
    getLeaveApprovers().then(setApprovers).catch(() => {});
    if (existing) {
      getLeaveProfile(existing.id)
        .then((p) => {
          setLeaveLoaded(p);
          setLeave(p);
        })
        .catch(() => {});
    } else {
      setLeaveLoaded(NO_LEAVE);
    }
  }, [canManageLeave, existing]);

  const setL = (patch: Partial<LeaveProfile>) => setLeave((l) => ({ ...l, ...patch }));

  const isNoLogin = userTypeId === UserType.NoLogin;
  const isTouchOnly = userTypeId === UserType.Touch;
  const showOfficeFields = userTypeId === UserType.Office || userTypeId === UserType.Both;
  const showPasscode = userTypeId === UserType.Touch || userTypeId === UserType.Both;

  // Mirrors AddEditGlobeweighUserViewModel.CanSave.
  function validate(): string | null {
    if (!firstName.trim()) return "First Name is required.";
    if (!surname.trim()) return "Surname is required.";
    if (showOfficeFields) {
      if (!username.trim()) return "Username is required.";
      if (!password.trim()) return "Password is required.";
      if (roleId === 0) return "Select a role.";
    } else if (isTouchOnly) {
      if (!passcode.trim()) return "Passcode is required.";
    }
    if (showPasscode && passcode.trim() && !/^\d{4}$/.test(passcode.trim())) return "Passcode must be 4 digits.";
    if (leave.enrolled && !leave.categoryId) return "Choose an Annual Leave category.";
    if (leave.enrolled && leave.startDate && leave.endDate && leave.endDate < leave.startDate)
      return "The leaving date is before the start date.";
    return null;
  }

  const canSave = validate() === null;

  async function onSave() {
    const error = validate();
    if (error) {
      void messageDialog(error);
      return;
    }
    const input: StaffInput = {
      userTypeId,
      firstName: firstName.trim(),
      surname: surname.trim(),
      username: showOfficeFields ? username.trim() : null,
      password: showOfficeFields ? password : null,
      passcode: showPasscode ? passcode.trim() || null : null,
      disabled,
      costingAccess,
      roleIds: showOfficeFields ? [roleId] : [],
    };
    setBusy(true);
    try {
      const saved = isNew ? await createStaff(input) : await updateStaff(existing!.id, input);
      // Only once it has loaded (so nothing is overwritten unseen), and when they are in or were.
      if (canManageLeave && leaveLoaded && (leave.enrolled || leaveLoaded.enrolled)) {
        try {
          await saveLeaveProfile(saved.id, leave);
        } catch (err) {
          void messageDialog("The user was saved, but their Annual Leave was not.\n" + (err as Error).message);
        }
      }
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  // Rides the USER DETAILS title, as the status does ORDER DETAILS; follows the Disabled tick.
  const status = disabled ? "Disabled" : isNew ? "New" : "Active";

  return (
    <Modal
      title={`${isNew ? "Add New" : "Edit"} User`}
      width={canManageLeave ? 840 : 760}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || !canSave} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      {/* The Add/Edit Order dialog's white panel (Dispatch.css ld-*): who the user is down the
          left (with the Disabled tick at its foot), the role on the right. */}
      <div className="pe-form">
        <div className="ld-panel">
          <div className="pe-body us-body">
            <section className={styles.section}>
              <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
                <span>User Details</span>
                <span className={cx(styles.statusTag, disabled && styles.statusDeleted)}>{status}</span>
              </h3>
              <div className={cx(styles.field, "ld-row")}>
                <span className={styles.fieldLabel}>User Type</span>
                <div className="us-choices">
                  {USER_TYPES.map((t) => (
                    <label key={t.id} className="ld-check">
                      <input
                        type="radio"
                        name="user-type"
                        checked={userTypeId === t.id}
                        onChange={() => setUserTypeId(t.id)}
                      />
                      <span>{t.label}</span>
                    </label>
                  ))}
                </div>
              </div>
              <label className={cx(styles.field, "ld-row")}>
                <span className={styles.fieldLabel}>First Name *</span>
                <input
                  className="ld-input"
                  value={firstName}
                  autoFocus
                  onChange={(e) => setFirstName(e.target.value)}
                />
              </label>
              <label className={cx(styles.field, "ld-row")}>
                <span className={styles.fieldLabel}>Surname *</span>
                <input className="ld-input" value={surname} onChange={(e) => setSurname(e.target.value)} />
              </label>
              {showOfficeFields && (
                <>
                  <label className={cx(styles.field, "ld-row")}>
                    <span className={styles.fieldLabel} title="Login name for the office apps">
                      Username *
                    </span>
                    <input className="ld-input" value={username} onChange={(e) => setUsername(e.target.value)} />
                  </label>
                  <label className={cx(styles.field, "ld-row")}>
                    <span className={styles.fieldLabel}>Password *</span>
                    <input className="ld-input" value={password} onChange={(e) => setPassword(e.target.value)} />
                  </label>
                </>
              )}
              {showPasscode && (
                <label className={cx(styles.field, "ld-row")}>
                  <span className={styles.fieldLabel} title="4-digit login for the Touch screens">
                    Passcode{isTouchOnly ? " *" : ""}
                  </span>
                  <input
                    className="ld-input us-passcode"
                    value={passcode}
                    inputMode="numeric"
                    maxLength={4}
                    placeholder="4 digits"
                    onChange={(e) => setPasscode(e.target.value.replace(/\D/g, "").slice(0, 4))}
                  />
                </label>
              )}
              <div className={cx(styles.field, "ld-row")}>
                <span className={styles.fieldLabel}>Disabled</span>
                <label className="ld-check">
                  <input type="checkbox" checked={disabled} onChange={(e) => setDisabled(e.target.checked)} />
                  <span>{disabled ? "Yes — cannot log in" : "No"}</span>
                </label>
              </div>
            </section>

            <div className="us-right">
            <section className={cx(styles.section, "us-access")}>
              {/* Held to the status tag's height, so the rule falls level with USER DETAILS'. */}
              <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
                <span>Access</span>
                <span className="us-title-strut" />
              </h3>
              <div className={cx(styles.field, "ld-row", showOfficeFields && "ld-row-top")}>
                <span className={styles.fieldLabel}>Role{showOfficeFields ? " *" : ""}</span>
                {showOfficeFields ? (
                  <div className="us-roles">
                    {ASSIGNABLE_ROLES.map((r) => (
                      <label key={r.id} className="ld-check">
                        <input
                          type="radio"
                          name="user-role"
                          checked={roleId === r.id}
                          onChange={() => setRoleId(r.id)}
                        />
                        <span>{r.label}</span>
                      </label>
                    ))}
                  </div>
                ) : (
                  <span className="ld-static">
                    <span className={styles.muted}>
                      {isNoLogin ? "Cannot sign in to anything" : "Touch users have no office role"}
                    </span>
                  </span>
                )}
              </div>
            </section>
            {canManageLeave && (
              <section className={cx(styles.section, "us-leave")}>
                <h3 className={cx(styles.sectionTitle, styles.sectionTitleRow)}>
                  <span>Annual Leave</span>
                  <span className="us-title-strut" />
                </h3>
                <div className={cx(styles.field, "ld-row")}>
                  <span className={styles.fieldLabel}>In Annual Leave</span>
                  <label className="ld-check">
                    <input
                      type="checkbox"
                      checked={leave.enrolled}
                      disabled={!leaveLoaded}
                      onChange={(e) => setL({ enrolled: e.target.checked })}
                    />
                    <span>{leave.enrolled ? "Yes" : "No"}</span>
                  </label>
                </div>
                {leave.enrolled && (
                  <>
                    <label className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel}>Category *</span>
                      <select
                        className="ld-input"
                        value={leave.categoryId ?? ""}
                        onChange={(e) => setL({ categoryId: e.target.value ? Number(e.target.value) : null })}
                      >
                        <option value="">Choose…</option>
                        {leaveCats.map((c) => (
                          <option key={c.id} value={c.id}>
                            {c.name} · {fmtDays(c.setDays)} set + {fmtDays(c.annualDays - c.setDays)} float
                          </option>
                        ))}
                      </select>
                    </label>
                    <label className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel} title="Blank = started before this year">
                        Started
                      </span>
                      <input
                        className="ld-input"
                        type="date"
                        value={leave.startDate ?? ""}
                        onChange={(e) => setL({ startDate: e.target.value || null })}
                      />
                    </label>
                    <label className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel}>Leaving</span>
                      <input
                        className="ld-input"
                        type="date"
                        value={leave.endDate ?? ""}
                        onChange={(e) => setL({ endDate: e.target.value || null })}
                      />
                    </label>
                    <label className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel}>Approver</span>
                      <select
                        className="ld-input"
                        value={leave.approverUserId ?? ""}
                        onChange={(e) => setL({ approverUserId: e.target.value ? Number(e.target.value) : null })}
                      >
                        <option value="">—</option>
                        {approvers.map((a) => (
                          <option key={a.id} value={a.id}>
                            {a.name}
                          </option>
                        ))}
                      </select>
                    </label>
                    <label className={cx(styles.field, "ld-row")}>
                      <span className={styles.fieldLabel} title="Where approve / decline emails go">
                        Email
                      </span>
                      <input
                        className="ld-input"
                        type="email"
                        value={leave.email ?? ""}
                        placeholder={leave.loginEmail ? `Their login: ${leave.loginEmail}` : "For approval emails"}
                        onChange={(e) => setL({ email: e.target.value || null })}
                      />
                    </label>
                  </>
                )}
              </section>
            )}
            </div>
          </div>
        </div>
      </div>
    </Modal>
  );
}
