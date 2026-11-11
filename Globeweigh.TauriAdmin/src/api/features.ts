import { apiGet, apiPut } from "./client";

// Staged features: built and deployed, switched on separately from Super Admin > Staging.
//
// A feature is staged so the work can go out with everything else — one deploy, one build, no
// branch kept alive — and still be dark on the client's screens until they say otherwise. The
// server holds the switch (dbo.FeatureFlag), not the browser, so it is one answer for every PC
// rather than something that has to be set on each of them.

// The names the server files flags under, so a caller cannot mistype one.
export const FEATURE = {
  productionPlanning: "ProductionPlanning",
  sageLink: "SageLink",
  reports: "Reports",
  annualLeave: "AnnualLeave",
  // Annual Leave's tabs, each staged on its own under the AnnualLeave switch (which still
  // decides whether the page is in the menu at all, and what the API lets through).
  annualLeaveRequests: "AnnualLeave.Requests",
  annualLeaveCalendar: "AnnualLeave.Calendar",
  annualLeaveStaff: "AnnualLeave.Staff",
  annualLeaveOutstanding: "AnnualLeave.Outstanding",
  annualLeaveHolidays: "AnnualLeave.Holidays",
  annualLeaveCategories: "AnnualLeave.Categories",
} as const;

// Appearance switches, held in the same store: Super Admin > Scale/Relay Styles. Not staged
// work — both looks are finished and either can be chosen — so they are not in STAGED_FEATURES.
export const STYLE = {
  /** ON = the "Dark dock" Live Weight column on Deliveries/Incoming; OFF = the WPF-carried look. */
  scaleRelayDarkDock: "Style.ScaleRelayDarkDock",
  /** The delivery-note design, picked on Admin > Delivery Note (writable by any signed-in
   *  user, see FeaturesController.OfficeFlags). Two flags for three choices:
   *  the picker keeps at most one ON, neither ON = the original. The API reads the same two
   *  (DispatchController.NoteTemplate) and stamps the choice on every note it serves, so the
   *  preview, the auto-print and the emailed PDF always agree. Ledger wins if both are ON. */
  deliveryNoteLedger: "Style.DeliveryNoteLedger",
  deliveryNoteGreenBand: "Style.DeliveryNoteGreenBand",
  /** How Open Orders marks the orders whose load is already on the Pending list, picked on
   *  Super Admin > Open Orders Styles. ON = Hatched (teal hatch + edge, with an On Pending
   *  filter); OFF (the default) = Grouped (those orders in their own navy band at the top). */
  openOrdersPendingHatched: "Style.OpenOrdersPendingHatched",
} as const;

export interface StagedFeature {
  name: string;
  title: string;
  // What switching it ON does, and what stays in use while it is off.
  description: string;
  whenOff: string;
  /** A part of another staged feature (one of its tabs): listed inside that feature's card,
   *  and it only shows once that feature is on too. */
  parent?: string;
}

// What Super Admin > Staging lists. Held here rather than in the database because it describes
// what the CODE can do: a flag whose feature has been released is deleted from this list and the
// row left behind is simply ignored, and a name nobody has switched yet has no row at all.
export const STAGED_FEATURES: StagedFeature[] = [
  {
    name: FEATURE.productionPlanning,
    title: "Production Planning",
    description:
      "Adds a Production Planning tab to Production, beside Production Summary: every product on " +
      "the open order book totalled against what is in stock, worst cover first, with the orders " +
      "behind each product and a horizon picker for how far ahead to look.",
    whenOff: "Production shows its Production and Production Summary tabs only.",
  },
  {
    name: FEATURE.sageLink,
    title: "Sage Link",
    description:
      "Adds a Sage Link page to the menu (in sage green, after Hauliers): the customer ledger as " +
      "Sage 50 holds it — every account with its balance, tax code and address count, and each " +
      "account's Sage address book — kept fresh by the Sage sidecar on the server, with a Refresh " +
      "from Sage button. The first piece of the Sage integration; shown to office admins.",
    whenOff: "No Sage Link item in the menu. Nothing else changes.",
  },
  {
    name: FEATURE.reports,
    title: "Reports",
    description:
      "Adds a Reports page to the menu (before Admin) with the Allied Merchant Buying Group report: " +
      "purchase turnover by member for any date range, monthly sales by member, and the product " +
      "lines by value and by quantity split per member — all from Sage's posted invoices and credit " +
      "notes, which the Sage sidecar on the server mirrors every few hours and on request. The " +
      "members and their Sage accounts are edited on the page. Exports to Excel. Shown to office admins.",
    whenOff: "No Reports item in the menu. The sidecar still keeps the sales mirror fresh.",
  },
  {
    name: FEATURE.annualLeave,
    title: "Annual Leave",
    description:
      "Adds an Annual Leave page to the menu for office admins: staff and their leave balances " +
      "(28 days, set company days + floating days, pro-rata for starters), leave requests with " +
      "approve / decline and the policy checks, a team calendar, and the company days and " +
      "categories behind it all. Replaces the Annual Leave Request Log spreadsheet.",
    whenOff: "Only the super-admin sees Annual Leave (in the lavender restricted style); the API refuses everyone else.",
  },
  {
    name: FEATURE.annualLeaveRequests,
    parent: FEATURE.annualLeave,
    title: "Requests",
    description: "Leave requests waiting, approved and declined, with approve / decline and the policy checks.",
    whenOff: "No Requests tab.",
  },
  {
    name: FEATURE.annualLeaveCalendar,
    parent: FEATURE.annualLeave,
    title: "Calendar",
    description: "The team calendar: who is off, day by day.",
    whenOff: "No Calendar tab.",
  },
  {
    name: FEATURE.annualLeaveStaff,
    parent: FEATURE.annualLeave,
    title: "Staff",
    description: "Everyone in Annual Leave with their category and balances for the year.",
    whenOff: "No Staff tab.",
  },
  {
    name: FEATURE.annualLeaveOutstanding,
    parent: FEATURE.annualLeave,
    title: "Outstanding",
    description:
      "Everyone with floating days still unbooked, most first, with a reminder email to each " +
      "chosen one (their days and the deadline).",
    whenOff: "No Outstanding tab.",
  },
  {
    name: FEATURE.annualLeaveHolidays,
    parent: FEATURE.annualLeave,
    title: "Company Holidays",
    description: "The set company days for each category.",
    whenOff: "No Company Holidays tab.",
  },
  {
    name: FEATURE.annualLeaveCategories,
    parent: FEATURE.annualLeave,
    title: "Categories",
    description: "The leave categories: days a year, set days and floating days.",
    whenOff: "No Categories tab.",
  },
];

// Name → on/off, for every flag that has ever been set. A name that is missing has never been
// switched and is OFF.
export type FeatureFlags = Record<string, boolean>;

export const getFeatures = () => apiGet<FeatureFlags>("/features");

// Super-admin only on the server; returns the whole map back so the app stays in step.
export const setFeature = (name: string, enabled: boolean) =>
  apiPut<FeatureFlags>(`/features/${encodeURIComponent(name)}`, { enabled });
