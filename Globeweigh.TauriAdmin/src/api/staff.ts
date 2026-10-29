import { apiGet, apiPost, apiPut, request } from "./client";

// Mirrors Globeweigh.Api StaffController StaffRow (camelCase JSON).
export interface StaffRow {
  id: number;
  userTypeId: number;
  firstName: string | null;
  surname: string | null;
  username: string | null;
  password: string | null;
  passcode: string | null;
  disabled: boolean;
  costingAccess: boolean;
  roleIds: number[];
}

// Writable fields (mirrors StaffInput).
export interface StaffInput {
  userTypeId: number;
  firstName: string | null;
  surname: string | null;
  username: string | null;
  password: string | null;
  passcode: string | null;
  disabled: boolean;
  costingAccess: boolean;
  roleIds: number[];
}

// Mirrors Globeweigh.Model eUserType.
export const UserType = {
  Office: 1,
  Touch: 2,
  Both: 3,
} as const;

export function userTypeLabel(id: number): string {
  switch (id) {
    case UserType.Office:
      return "Office";
    case UserType.Touch:
      return "Touch";
    default:
      return "Both";
  }
}

// Assignable roles (mirrors eUserRole). GlobeweighAdmin (1) is excluded — not user-assignable
// — and so is Factory User (4): the Touch login picker is driven by User Type (Touch/Both),
// not by a role, so that role has been retired. Weighbridge User (5) is retired too (nothing
// was gated on it). A user holds exactly one role.
export const ASSIGNABLE_ROLES: { id: number; label: string }[] = [
  { id: 2, label: "Office Admin" },
  { id: 3, label: "Office User" },
  { id: 6, label: "Sales" },
];

// Labels for display, including retired roles that may still linger on existing users.
const ROLE_LABELS: Record<number, string> = {
  1: "Globeweigh Admin",
  4: "Factory User",
  5: "Weighbridge User",
  ...Object.fromEntries(ASSIGNABLE_ROLES.map((r) => [r.id, r.label])),
};

export function roleLabel(id: number): string {
  return ROLE_LABELS[id] ?? `Role ${id}`;
}

export const getStaff = () => apiGet<StaffRow[]>("/staff");
export const createStaff = (input: StaffInput) => apiPost<StaffRow>("/staff", input);
export const updateStaff = (id: number, input: StaffInput) =>
  apiPut<StaffRow>(`/staff/${id}`, input);
export const deleteStaff = (id: number) => request<void>("DELETE", `/staff/${id}`);
