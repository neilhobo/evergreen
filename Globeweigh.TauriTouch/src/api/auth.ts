import { apiGet, apiPost, setToken } from "./client";
import { AdminUserDto } from "../types";

// Session survives a page refresh (the UI refresh pill reloads the page) but not
// closing the app — sessionStorage is scoped to the browsing session.
const SESSION_KEY = "session";

export interface TouchUser {
  id: number;
  fullName: string | null;
}

// GET /api/auth/touch-users — the users shown on the login picker (User Type Touch or Both).
export const getTouchUsers = () => apiGet<TouchUser[]>("/auth/touch-users");

// POST /api/auth/touch-login — log in a factory user by id + numeric passcode.
// skipPasscode = the Ctrl+Enter secret login; the API only honours it on the LAN.
export async function touchLogin(userId: number, passcode: string, skipPasscode = false): Promise<AdminUserDto> {
  const user = await apiPost<AdminUserDto>("/auth/touch-login", { userId, passcode, skipPasscode });
  setToken(user.token);
  sessionStorage.setItem(SESSION_KEY, JSON.stringify(user));
  return user;
}

// Restores the signed-in user after a refresh; null if absent or the JWT has expired.
export function restoreSession(): AdminUserDto | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    const user = JSON.parse(raw) as AdminUserDto;
    if (!user?.token || !user.expiresUtc || new Date(user.expiresUtc) <= new Date()) {
      sessionStorage.removeItem(SESSION_KEY);
      return null;
    }
    setToken(user.token);
    return user;
  } catch {
    return null;
  }
}

export function clearSession() {
  setToken(null);
  sessionStorage.removeItem(SESSION_KEY);
}
