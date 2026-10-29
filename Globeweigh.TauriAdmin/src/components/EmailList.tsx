import { useState } from "react";
import DataTable, { Column } from "./DataTable";
import { messageDialog } from "./confirmDialog";

// A record's email addresses — any number of them, held as a plain list of strings.
// Shared by Hauliers and Customers/Suppliers: the Emails tab of each editor, and the
// read-only Emails panel beside each page's list.

// Looks like an address: something, an @, then a domain with a dot in it.
export const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// One row of an email list.
export interface EmailRow {
  email: string;
}
export const EMAIL_COLUMNS: Column<EmailRow>[] = [{ key: "email", header: "Email address" }];
export const toEmailRows = (emails: string[]): EmailRow[] => emails.map((email) => ({ email }));

const alreadyListed = (emails: string[], e: string) =>
  emails.some((x) => x.toLowerCase() === e.toLowerCase());

// What to save when there's an address typed into the editor but not yet added: it still
// counts — nobody expects Save to throw it away. Returns the list to save, or null after
// saying why the typed text can't be saved (the caller then stays on the Emails tab).
export function withPendingEmail(emails: string[], typed: string): string[] | null {
  const e = typed.trim();
  if (!e) return emails;
  if (!EMAIL_RE.test(e)) {
    void messageDialog(`"${e}" doesn't look like an email address.`);
    return null;
  }
  return alreadyListed(emails, e) ? emails : [...emails, e];
}

interface Props {
  emails: string[];
  onChange: (emails: string[]) => void;
  // The address being typed, owned by the editor so Save can pick it up (withPendingEmail).
  typed: string;
  onTypedChange: (typed: string) => void;
  autoFocus?: boolean;
}

// The Emails tab body: add/remove over the list. The list itself belongs to the editor
// and is saved with the record, so a brand-new record can have addresses before it
// exists and Cancel drops every change.
export default function EmailListEditor({ emails, onChange, typed, onTypedChange, autoFocus }: Props) {
  // The highlighted row — what Remove takes off.
  const [selected, setSelected] = useState<string | null>(null);

  function add() {
    const e = typed.trim();
    if (!e) return;
    if (!EMAIL_RE.test(e)) {
      void messageDialog(`"${e}" doesn't look like an email address.`);
      return;
    }
    if (alreadyListed(emails, e)) {
      void messageDialog(`${e} is already on the list.`);
      return;
    }
    onChange([...emails, e]);
    setSelected(e);
    onTypedChange("");
  }

  function remove() {
    if (selected === null) return;
    onChange(emails.filter((x) => x !== selected));
    setSelected(null);
  }

  return (
    <>
      <div className="toolbar">
        <input
          className="form-input toolbar-search"
          placeholder="Add an email address…"
          value={typed}
          autoFocus={autoFocus}
          onChange={(e) => onTypedChange(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              add();
            }
          }}
        />
        <button className="btn-primary" disabled={!typed.trim()} onClick={add}>
          Add
        </button>
        <div className="toolbar-right">
          <button className="toolbar-btn" disabled={selected === null} onClick={remove}>
            Remove
          </button>
        </div>
      </div>
      <DataTable
        columns={EMAIL_COLUMNS}
        rows={toEmailRows(emails)}
        rowKey={(r) => r.email}
        selectedKey={selected}
        onSelect={(r) => setSelected(r.email)}
        emptyText="No email addresses yet"
      />
    </>
  );
}
