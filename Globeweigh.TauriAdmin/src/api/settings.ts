import { apiGet, apiPost, apiPut } from "./client";

// System-wide standard bulk density (kg/m³) on dbo.Setting — used by the Touch Manual
// Stock module to convert a weighed pallet's kg into sold volume (m³).
export const getBulkDensity = () => apiGet<{ bulkDensity: number }>("/manual-stock/config");
export const setBulkDensity = (bulkDensity: number) =>
  apiPut<{ bulkDensity: number }>("/manual-stock/config", { bulkDensity });

// Order numbering — carries on the incremental order number from the system they ran
// before this one. nextOrderNo is the number the next order takes; enabled is the
// switch-over. Off = orders are numbered highest-so-far + 1, as they always were.
// highestOrderNo is what orders have actually reached here, which nextOrderNo must clear.
export interface OrderNumbering {
  nextOrderNo: number | null;
  enabled: boolean;
  highestOrderNo: number;
}

export const getOrderNumbering = () => apiGet<OrderNumbering>("/settings/order-numbering");
export const setOrderNumbering = (nextOrderNo: number | null, enabled: boolean) =>
  apiPut<OrderNumbering>("/settings/order-numbering", { nextOrderNo, enabled });

// Outbound email — the SMTP account set up in Super Admin > Email Setup, which automatic
// emails go out through. The password is write-only: hasPassword says one is saved (and
// readable on this server), and a blank password on save keeps it. Test and send-test
// take the details as typed, so they can be tried before they are saved.
export type SmtpSecurity = "None" | "StartTls" | "Ssl";

export interface EmailSettings {
  host: string | null;
  port: number | null;
  security: SmtpSecurity;
  username: string | null;
  hasPassword: boolean;
  fromAddress: string | null;
  fromName: string | null;
}

export interface EmailSettingsInput {
  host: string;
  port: number | null;
  security: SmtpSecurity;
  username: string;
  password: string;
  fromAddress: string;
  fromName: string;
}

export const getEmailSettings = () => apiGet<EmailSettings>("/settings/email");
export const saveEmailSettings = (input: EmailSettingsInput) =>
  apiPut<EmailSettings>("/settings/email", input);
export const testEmailSettings = (input: EmailSettingsInput) =>
  apiPost<{ message: string }>("/settings/email/test", input);
export const sendTestEmail = (input: EmailSettingsInput, to: string) =>
  apiPost<{ message: string }>("/settings/email/send-test", { ...input, to });
