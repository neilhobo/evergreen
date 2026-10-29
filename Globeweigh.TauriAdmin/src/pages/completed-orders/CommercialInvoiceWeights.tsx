import { CommercialInvoice } from "../../api/dispatch";

// Whole kg with a thousands comma: 24,500.
export const kgGross = (n: number) => n.toLocaleString("en-GB", { maximumFractionDigits: 0 });
// Nett is gross less the packaging (pallet + bags), so it can carry a fraction: shown to
// 2 places when it does (19,009.50) and as whole kg when it does not (2,247).
export const kgNett = (n: number) =>
  Number.isInteger(n)
    ? n.toLocaleString("en-GB", { maximumFractionDigits: 0 })
    : n.toLocaleString("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// The invoice's weights per HS tariff code, side by side under the items: Combined Gross
// Weight (the lines' calculated pallet weights) and Combined Nett Weight (less each pallet's
// empty pallet and bags), each with its total. Nothing when nothing is picked yet.
//
// Classed ciw-*, never the items table's class: the pagination counts the items table's
// tbody rows, and these must not be mistaken for item lines. They follow the items as a
// block of their own (useSheetPagination afterItems): under the last rows when they fit,
// else at the top of the next page; the footer is not tied to them.
export default function CommercialInvoiceWeights({ invoice }: { invoice: CommercialInvoice }) {
  const groups = invoice.weightsByHsCode ?? [];
  if (groups.length === 0) return null;
  const table = (title: string, value: (g: (typeof groups)[number]) => string, total: string) => (
    <table className="ciw-table">
      <thead>
        <tr className="ciw-title">
          <th colSpan={2}>{title}</th>
        </tr>
        <tr className="ciw-head">
          <th>HS Tariff Code</th>
          <th className="ciw-n">Weight (kg)</th>
        </tr>
      </thead>
      <tbody>
        {groups.map((g) => (
          <tr key={g.hsCode}>
            <td>{g.hsCode || "No code"}</td>
            <td className="ciw-n">{value(g)}</td>
          </tr>
        ))}
        <tr className="ciw-total">
          <td>TOTAL</td>
          <td className="ciw-n">{total}</td>
        </tr>
      </tbody>
    </table>
  );
  return (
    <div className="ciw">
      {table(
        "Combined Gross Weight by HS Tariff Code",
        (g) => kgGross(g.gross),
        kgGross(invoice.totalGross ?? groups.reduce((s, g) => s + g.gross, 0)),
      )}
      {table(
        "Combined Nett Weight by HS Tariff Code",
        (g) => kgNett(g.nett),
        kgNett(invoice.totalNet ?? groups.reduce((s, g) => s + g.nett, 0)),
      )}
    </div>
  );
}
