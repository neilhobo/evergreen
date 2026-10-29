import { apiGet, apiPost, apiPut } from "./client";

// The wording of the automatic emails — Admin > Automatic Emails.
//
// Each email is a fixed layout with a handful of pieces of prose in it (the subject, the
// greeting, the intro, the footer, ...). The API lists the pieces with their default wording and
// what they currently say, and the placeholders the wording may carry ({Ref}, {Haulier}, ...).
// The office changes the words; the layout stays as it is. Saving sends every piece back; a
// piece back at its default is simply the default again.

// The same names the Send Email dialog uses for the emails (dispatch.ts LoadEmailKind; "dispatched-customer" is the customer's copy of "dispatched").
// "order-confirmation" is the dialog's "confirmation", sent to the customer from Open Orders.
export type AutomaticEmailKind = "ready" | "dispatched" | "dispatched-customer" | "pre-commercial" | "order-confirmation";

export interface AutomaticEmailPlaceholder {
  name: string; // without the braces: "Ref"
  meaning: string;
}

export interface AutomaticEmailField {
  key: string;
  label: string;
  hint: string;
  multiline: boolean;
  default: string;
  text: string; // what it says now: the default, or what was saved
}

export interface AutomaticEmail {
  kind: AutomaticEmailKind;
  title: string;
  description: string;
  placeholders: AutomaticEmailPlaceholder[];
  fields: AutomaticEmailField[];
}

// The email as it would go out, on a made-up load.
export interface AutomaticEmailPreview {
  subject: string;
  html: string;
  text: string;
}

// Field key → wording.
export type AutomaticEmailTexts = Record<string, string>;

export const getAutomaticEmails = () => apiGet<AutomaticEmail[]>("/automatic-emails");

export const saveAutomaticEmail = (kind: AutomaticEmailKind, fields: AutomaticEmailTexts) =>
  apiPut<AutomaticEmail>(`/automatic-emails/${kind}`, { fields });

// Nothing is saved: the wording as typed, composed on a sample load.
export const previewAutomaticEmail = (kind: AutomaticEmailKind, fields: AutomaticEmailTexts) =>
  apiPost<AutomaticEmailPreview>(`/automatic-emails/${kind}/preview`, { fields });
