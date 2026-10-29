import { ReactNode } from "react";
import { DeliveryNote, DeliveryNoteItem, isCubicMeterNote } from "../../api/dispatch";
import { fmtDate } from "../../format";
import { useSheetPagination } from "./useSheetPagination";
import {
  DocTemplate,
  GreenBandTop,
  LedgerDetails,
  LedgerTop,
  TEMPLATE_FONTS,
  TEMPLATE_PAGINATION,
  TERMS1,
  TERMS2,
  TRADE,
  TemplateEnd,
  TemplatePages,
} from "./templateParts";

// The two newer delivery-note designs, chosen on Admin > Delivery Note (the original is
// DeliveryNoteSheet). Both are laid out on the same frozen .dn-page box and paginated by the
// same measuring pass as the original — only what sits inside the page differs:
//
//   ledger     "Ledger"     — hairline rules, small-caps labels, monospaced figures; green only
//                             in the logos. Set in Public Sans + IBM Plex Mono (vendored).
//   greenband  "Green Band" — a solid brand header with the note number in a lime pill, the
//                             load details in one strip under it. Set in Poppins.
//
// Both add a totals row and give each signature a printed name and date (no m³ column, by
// request). The commercial invoice is drawn from the same parts (templateParts.tsx) in
// CommercialInvoiceTemplateSheet.
export type NoteTemplate = DocTemplate;

interface Props {
  note: DeliveryNote;
  template: NoteTemplate;
  bestFit?: boolean;
  onReady?: () => void;
}

const VAT = "V.A.T. Reg. No. GB 701 5625 67";

const itemName = (d: DeliveryNoteItem) =>
  `${d.size != null ? `${d.size}L ` : ""}${d.description ?? ""}`;

export default function DeliveryNoteTemplateSheet({ note, template, bestFit = false, onReady }: Props) {
  const ledger = template === "ledger";
  const { pages, measureRef, probes } = useSheetPagination(
    note,
    note.items,
    TEMPLATE_PAGINATION.footerStart,
    onReady,
    { table: TEMPLATE_PAGINATION.table, end: TEMPLATE_PAGINATION.end, fonts: TEMPLATE_FONTS[template] },
  );

  const address =
    note.addressLines.length > 0 ? note.addressLines.join("\n") : note.addressFull ?? "";
  const date = note.date ? fmtDate(note.date) : "";
  const no = String(note.noteNo);

  // Peat lines (sold by the cubic meter) carry m³, never pallets or bags.
  const cubicNote = isCubicMeterNote(note.items);
  const totals = note.items.reduce(
    (t, d) =>
      d.soldBy === "CubicMeter"
        ? { ...t, m3: t.m3 + (d.cubicMeters ?? 0) }
        : { ...t, pallets: t.pallets + d.pallets, bags: t.bags + d.totalBags },
    { pallets: 0, bags: 0, m3: 0 },
  );

  const deliverTo = (
    <div className="dnt-pre">
      <span className="dnt-cust">{note.customerName ?? ""}</span>
      {address}
    </div>
  );
  const instructions = (
    <>
      <div className="dnt-pre">{note.comments ?? ""}</div>
      {note.telephone && <div className="dnt-tel">Tel. {note.telephone}</div>}
    </>
  );

  // ── header (page 1) ──────────────────────────────────────────────────────────────
  const header: ReactNode = ledger ? (
    <>
      <LedgerTop title={note.confirmation ? "Order confirmation" : "Delivery note"} no={no} date={date} pageCount={pages?.length ?? 1} />
      {/* The order confirmation says so across the page, not only over the number. (Green
          Band needs no banner: its header band already carries the title, large.) */}
      {note.confirmation && <div className="dnl-doc-title">Order Confirmation</div>}
      <div className="dnl-head">
        <div>
          <div className="dnt-lab">Deliver to</div>
          {deliverTo}
        </div>
        <div>
          <div className="dnt-lab">Delivery instructions</div>
          {instructions}
        </div>
        <div>
          <div className="dnt-lab">Details</div>
          <LedgerDetails
            rows={[
              ["Order no.", note.poNumber ?? "", true],
              ["Account ref.", note.accountRef ?? "", true],
              ["Haulier", note.haulierName ?? ""],
              ["Vehicle reg.", note.vehicleReg ?? "", true],
              ["Trailer no.", note.trailerNo ?? "", true],
            ]}
          />
        </div>
      </div>
    </>
  ) : (
    <>
      <GreenBandTop
        title={note.confirmation ? "Order Confirmation" : "Delivery Note"}
        no={no}
        strip={[
          ["Date", date],
          ["Order no.", note.poNumber ?? ""],
          ["Acc. ref.", note.accountRef ?? ""],
          ["Haulier / reg.", [note.haulierName, note.vehicleReg].filter(Boolean).join(" · ")],
          ["Trailer", note.trailerNo ?? ""],
        ]}
      />
      <div className="dnb-two">
        <div className="dnb-card">
          <div className="dnt-lab">Deliver to</div>
          {deliverTo}
        </div>
        <div className="dnb-card">
          <div className="dnt-lab">Delivery instructions</div>
          {instructions}
        </div>
      </div>
    </>
  );

  // ── items (every page with rows; the totals only on the last) ──────────────────────
  const itemsTable = (rows: DeliveryNoteItem[], withTotals: boolean) => (
    <table className="dnt-items">
      {/* An all-peat note drops the pallet columns and totals in m³; a peat line on a mixed
          note leaves its pallet cells blank and shows its m³ in the total column. */}
      <colgroup>
        <col />
        {!cubicNote && <col className="dnt-c-pal" />}
        {!cubicNote && <col className="dnt-c-bpp" />}
        <col className="dnt-c-bags" />
      </colgroup>
      <thead>
        <tr>
          <th>Product</th>
          {!cubicNote && <th className="dnt-n">Pallets</th>}
          {!cubicNote && <th className="dnt-n">Bags / pallet</th>}
          <th className="dnt-n">{cubicNote ? "Total (m³)" : "Total bags"}</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((d, i) => {
          const peat = d.soldBy === "CubicMeter";
          return (
            <tr key={i}>
              <td>{peat ? d.description ?? "" : itemName(d)}</td>
              {!cubicNote && <td className="dnt-n">{peat ? "" : d.pallets.toLocaleString()}</td>}
              {!cubicNote && <td className="dnt-n">{peat ? "" : d.upp.toLocaleString()}</td>}
              <td className="dnt-n">
                {peat
                  ? `${(d.cubicMeters ?? 0).toLocaleString()}${cubicNote ? "" : " m³"}`
                  : d.totalBags.toLocaleString()}
              </td>
            </tr>
          );
        })}
      </tbody>
      {withTotals && (
        <tfoot>
          <tr className="dnt-total">
            <td>Total</td>
            {cubicNote ? (
              <td className="dnt-n">{totals.m3.toLocaleString()}</td>
            ) : (
              <>
                <td className="dnt-n">{totals.pallets.toLocaleString()}</td>
                <td />
                <td className="dnt-n">
                  {totals.bags.toLocaleString()}
                  {totals.m3 > 0 && ` + ${totals.m3.toLocaleString()} m³`}
                </td>
              </>
            )}
          </tr>
        </tfoot>
      )}
    </table>
  );

  // ── footer (last page): signatures pin it to the page foot ─────────────────────────
  const footer: ReactNode = (
    <>
      {ledger ? (
        <div className="dnt-sign">
          {["Driver signature", "Received in good condition"].map((label) => (
            <div key={label}>
              <div className="dnl-line" />
              <div className="dnt-lab dnl-sign-lab">
                <span>{label}</span>
                <span>Print name · Date</span>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="dnt-sign">
          {["Driver signature", "Received in good condition"].map((label) => (
            <div key={label} className="dnb-sbox">
              <div className="dnt-lab">{label}</div>
              <div className="dnb-small">Print name ______________________ &nbsp; Date __________</div>
            </div>
          ))}
        </div>
      )}
      <TemplateEnd
        template={template}
        terms={
          ledger ? (
            <>
              {TERMS1} {TERMS2}
              <br />
              {TRADE} {VAT}. All major credit cards accepted.
            </>
          ) : (
            <>
              <b>{TERMS1}</b> {TERMS2} {TRADE} {VAT}. All major credit cards accepted.
            </>
          )
        }
      />
    </>
  );

  return (
    <TemplatePages
      template={template}
      bestFit={bestFit}
      pages={pages}
      measureRef={measureRef}
      probes={probes}
      items={note.items}
      header={header}
      table={itemsTable}
      footer={footer}
    />
  );
}
