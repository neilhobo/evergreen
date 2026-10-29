import { apiGet } from "./client";

// The audit trail (AuditController). Generic by entity name and id — this module knows nothing
// about dispatch orders, and any other screen that wants a record's history asks the same way.
//
// Read-only by design: nothing writes a trail through the API. The entries are written by the
// server inside the same save as the change itself (GbEvergreenContext.SaveChanges), so a change
// cannot happen without one, and one cannot be written for a change that did not.

// The entity names the server files trails under, so a caller cannot mistype one.
export const AUDIT_ENTITY = {
  // An order AND its lines: a line's change is filed under the order it belongs to, because that
  // is the record the office asks the question of.
  dispatchOrder: "DispatchOrder",
  weighbridgeRecord: "WeighbridgeRecord",
} as const;

export interface AuditEntry {
  id: number;
  entityName: string;
  entityId: number;
  // The property as the model names it — what was actually recorded.
  fieldName: string;
  // How that field is named on screen today. Worked out on read, so renaming a heading does not
  // mean rewriting history.
  fieldLabel: string;
  // Which line of the record it was about (an order item's product); null for the record itself.
  fieldScope: string | null;
  // Null is a genuinely empty value: a null oldValue is "set for the first time", a null newValue
  // is "cleared". Neither is a missing record.
  oldValue: string | null;
  newValue: string | null;
  // "Updated" for a field that changed; "Added" / "Removed" for a whole line; "Created" for the
  // record itself — read off the record rather than stored, so its id is 0 and it closes every
  // trail, however old the record.
  action: string;
  dateChanged: string;
  // Null when the change came from a screen with no login — the touch and picking apps post
  // anonymously. source then says where it came from.
  userId: number | null;
  userName: string | null;
  source: string | null;
  // Who the RECORD names as having done it (picking completed BY Aidan McCann), when it names
  // anyone. Shown as the row's who where the change itself was posted with no login.
  actor: string | null;
  // The change set a date-time to the moment of the change, with nothing there before: an action
  // recorded (picking complete, dispatched), not a value edited. Drawn without the from → to line,
  // since the time on the row already says it.
  stamp: boolean;
  // A stamp taken back: the date-time cleared with nothing in its place (picking reopened). Drawn
  // as the action and "cleared", with who and when.
  stampCleared: boolean;
}

export const getAuditTrail = (entityName: string, entityId: number) =>
  apiGet<AuditEntry[]>(`/audit/${entityName}/${entityId}`);
