import { apiGet, apiPost, setToken } from "./client";
import { AdminUserDto } from "../types";

// Session survives a page refresh (the UI refresh pill reloads the page) but not
// closing the app — sessionStorage is scoped to the browsing session.
const SESSION_KEY = "session";

function startSession(user: AdminUserDto): AdminUserDto {
  setToken(user.token);
  sessionStorage.setItem(SESSION_KEY, JSON.stringify(user));
  return user;
}

// POST /api/auth/admin-login — same endpoint the WPF Admin app uses.
export async function adminLogin(username: string, password: string): Promise<AdminUserDto> {
  return startSession(await apiPost<AdminUserDto>("/auth/admin-login", { username, password }));
}

// ---- Switch user (admins only; the API enforces it) ----

export interface SwitchUserRow {
  id: number;
  firstName: string | null;
  surname: string | null;
  username: string | null;
  roleIds: number[];
  disabled: boolean;
}

export const getSwitchUsers = () => apiGet<SwitchUserRow[]>("/auth/switch-users");

// Sign in AS another user, no password. The returned session replaces the admin's.
export async function switchToUser(targetUserId: number): Promise<AdminUserDto> {
  return startSession(await apiPost<AdminUserDto>("/auth/switch-user", { targetUserId }));
}

// Back to the admin's own session (the API reads who that is from the signed token).
export async function switchBack(): Promise<AdminUserDto> {
  return startSession(await apiPost<AdminUserDto>("/auth/switch-back", {}));
}

// Restores the signed-in user after a refresh; null if absent.
// Sessions don't time out — the API issues tokens with no expiry — so a stored session is
// only rejected if it has no token, or carries a real expiry that has already passed (a
// token issued by an older API build, before non-expiring tokens).
export function restoreSession(): AdminUserDto | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    const user = JSON.parse(raw) as AdminUserDto;
    if (!user?.token || hasExpired(user.expiresUtc)) {
      sessionStorage.removeItem(SESSION_KEY);
      return null;
    }
    setToken(user.token);
    return user;
  } catch {
    return null;
  }
}

function hasExpired(expiresUtc: string | undefined | null): boolean {
  if (!expiresUtc) return false; // no expiry recorded = never expires
  const when = new Date(expiresUtc).getTime();
  return !Number.isNaN(when) && when <= Date.now();
}

export function clearSession() {
  setToken(null);
  sessionStorage.removeItem(SESSION_KEY);
}

// Merge changes into the stored session user (e.g. after a password/passcode change)
// and return the updated user so React state can be refreshed too.
export function updateSessionUser(changes: Partial<AdminUserDto>): AdminUserDto | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    const user = { ...(JSON.parse(raw) as AdminUserDto), ...changes };
    sessionStorage.setItem(SESSION_KEY, JSON.stringify(user));
    return user;
  } catch {
    return null;
  }
}

// POST /api/auth/change-password — self-service for the signed-in user (WPF "My Account").
// Returns the now-current password/passcode the server stored.
export async function changePassword(
  oldPassword: string,
  newPassword: string,
  passcode: string,
): Promise<{ password: string | null; passcode: string | null }> {
  return apiPost("/auth/change-password", { oldPassword, newPassword, passcode });
}
