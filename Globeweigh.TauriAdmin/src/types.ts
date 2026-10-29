// Wire shapes from Globeweigh.Api (System.Text.Json camelCase).
// Mirrors Globeweigh.Api AuthController.AdminUserDto.

export interface AdminUserDto {
  id: number;
  userTypeId: number;
  firstName: string | null;
  surname: string | null;
  username: string | null;
  roleId: number;
  // Effective roles from the Globeweigh_Role junction table (RoleId can be 0).
  roleIds: number[];
  costingAccess: boolean;
  disabled: boolean;
  // Set while an admin is signed in AS this user (Switch user). Absent on sessions stored by
  // an older build.
  impersonatedByUserId?: number | null;
  impersonatedByName?: string | null;
  token: string;
  expiresUtc: string;
  // Populated only after a My-Account change (not returned by login).
  password?: string | null;
  passcode?: string | null;
}

// Mirrors Globeweigh.Model eUserRole.
export const UserRole = {
  GlobeweighAdmin: 1,
  OfficeAdmin: 2,
  OfficeUser: 3,
  Factory: 4,
  Weighbridge: 5, // retired
  Sales: 6,
} as const;

export function fullName(user: AdminUserDto): string {
  return `${user.firstName ?? ""} ${user.surname ?? ""}`.trim();
}

export function initials(user: AdminUserDto): string {
  const f = (user.firstName ?? "").trim();
  const s = (user.surname ?? "").trim();
  return `${f.charAt(0)}${s.charAt(0)}`.toUpperCase();
}

export function hasRole(user: AdminUserDto, ...roles: number[]): boolean {
  const ids = user.roleIds?.length ? user.roleIds : user.roleId ? [user.roleId] : [];
  return roles.some((r) => ids.includes(r));
}

// Whether this session is an admin acting as someone else (Switch user).
export function isSwitchedUser(user: AdminUserDto): boolean {
  return user.impersonatedByUserId != null;
}

// The Globeweigh super-admin is a single named user, not a role: it gates the
// Globeweigh-internal nav section and the internal-only grid columns (which are
// shaded lavender wherever they appear — see DataTable's Column.superAdmin).
export const SUPER_ADMIN_USERNAME = "neil.hobson@globeweigh.com";

export function isSuperAdmin(user: AdminUserDto): boolean {
  return (user.username ?? "").trim().toLowerCase() === SUPER_ADMIN_USERNAME;
}
