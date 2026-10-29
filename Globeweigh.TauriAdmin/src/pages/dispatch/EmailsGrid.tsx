import styles from "../../widgets/DetailPanel.module.css";

// One row of an Emails grid: a party and every email address it has, so the name is read
// once however many addresses it carries.
export interface EmailParty {
  key: string;
  name: string;
  emails: string[];
}

interface Props {
  parties: EmailParty[];
  // What the grid says while there is nobody to show ("Choose a haulier").
  emptyText: string;
}

// The Emails grid the Load and Order dialogs share — where the paperwork goes: the SpecGauge
// report grid (Density Comparison) a size down, ruled cells, no header row, the Emails column
// tinted blue the way its what-if columns are. A party with no address still gets its row, so
// a missing one is seen rather than silent. Fills its section and scrolls on its own. Styled
// in Dispatch.css (.ld-mail-*); a dialog with a narrow column for it resizes the name column.
export default function EmailsGrid({ parties, emptyText }: Props) {
  return (
    <div className="ld-mail-wrap">
      <table className="ld-mail">
        <tbody>
          {parties.length === 0 ? (
            <tr>
              <td colSpan={2} className="ld-mail-empty">
                {emptyText}
              </td>
            </tr>
          ) : (
            parties.map((p) => (
              <tr key={p.key}>
                <td className="ld-mail-name" title={p.name}>
                  {p.name}
                </td>
                <td className="ld-mail-tint">
                  {p.emails.length > 0 ? (
                    p.emails.map((e) => (
                      <div key={e} title={e}>
                        {e}
                      </div>
                    ))
                  ) : (
                    <span className={styles.muted}>—</span>
                  )}
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}
