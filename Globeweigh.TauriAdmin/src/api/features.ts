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
} as const;

export interface StagedFeature {
  name: string;
  title: string;
  // What switching it ON does, and what stays in use while it is off.
  description: string;
  whenOff: string;
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
];

// Name → on/off, for every flag that has ever been set. A name that is missing has never been
// switched and is OFF.
export type FeatureFlags = Record<string, boolean>;

export const getFeatures = () => apiGet<FeatureFlags>("/features");

// Super-admin only on the server; returns the whole map back so the app stays in step.
export const setFeature = (name: string, enabled: boolean) =>
  apiPut<FeatureFlags>(`/features/${encodeURIComponent(name)}`, { enabled });
