import { apiGet } from "./client";

// Super Admin > Login History (LoginHistoryController, dbo.LoginEvent): every sign-in attempt to
// TauriAdmin or the mobile app, and every Switch user / Return to my account. Dates are UK local.
export interface LoginEventRow {
  id: number;
  date: string;
  kind: "SignIn" | "Failed" | "SwitchUser" | "SwitchBack" | string;
  userId: number | null;
  fullName: string | null; // the account's full name
  username: string | null; // as typed, or the account's login
  byName: string | null; // Switch user: the admin who switched in
  detail: string | null;
  client: string | null; // TauriAdmin | Mobile | Unknown
  via: string | null; // Tunnel (off-site, through Cloudflare) | Site
  ipAddress: string | null;
  userAgent: string | null;
}

export const getLoginHistory = (days: number) => apiGet<LoginEventRow[]>(`/login-history?days=${days}`);
