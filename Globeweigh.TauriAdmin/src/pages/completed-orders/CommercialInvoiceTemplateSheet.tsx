import { ReactNode } from "react";
import { CommercialInvoice, CommercialInvoiceItem, invoiceNumber } from "../../api/dispatch";
import { fmtDate } from "../../format";
import { useSheetPagination } from "./useSheetPagination";
import CommercialInvoiceWeights, { kgNett } from "./CommercialInvoiceWeights";
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

// The commercial invoice in the delivery note's newer designs ("Ledger" / "Green Band"), so
// that whichever design is picked on Admin > Delivery Note, the two documents match. Same
// fields as the original invoice (CommercialInvoiceSheet): invoice no (= LoadNo-OrderNo),
// invoice / deliver-to addresses with the load's weights, HS tariff codes, prices, and the
// Nett / V.A.T. / Total — here as the foot of the items table. No bank details, by request.

interface Props {
  invoice: CommercialInvoice;
  // Called once pagination has settled (the server print page's capture signal).
  onReady?: () => void;
  template: DocTemplate;
}

const money = (n: number) =>
  n.toLocaleString("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const kg = (n: number) => n.toLocaleString("en-GB", { maximumFractionDigits: 0 });

const VAT_EORI = "V.A.T. Reg. No. GB 701 5625 67 · EORI XI 701562567000";

// "10 × 60L Multi Purpose Compost @ 70 per pallet" — the original invoice's wording, tidied.
const itemText = (d: CommercialInvoiceItem) =>
  `${d.pallets} × ${d.size != null ? `${d.size}L ` : ""}${d.description ?? ""} @ ${d.upp} per pallet`;

export default function CommercialInvoiceTemplateSheet({ invoice, template, onReady }: Props) {
  const ledger = template === "ledger";
  // The totals rows and the weights per HS tariff code follow the items but are not footer:
  // they go under the last rows when they fit, else at the top of the next page (midPage);
  // the footer (from the pinned small print down) comes after, on that page or its own.
  const { pages, midPage, measureRef, probes } = useSheetPagination(invoice, invoice.items, ".dnt-pin", onReady, {
    table: TEMPLATE_PAGINATION.table,
    end: TEMPLATE_PAGINATION.end,
    fonts: TEMPLATE_FONTS[template],
    afterItems: `${TEMPLATE_PAGINATION.table} tfoot, .ciw`,
  });

  const date = invoice.date ? fmtDate(invoice.date) : "";
  const no = invoiceNumber(invoice);

  const invoiceTo = (
    <div className="dnt-pre">
      <span className="dnt-cust">{invoice.invoiceName ?? ""}</span>
      {invoice.invoiceLines.join("\n")}
    </div>
  );
  const deliverLines = invoice.deliverLines.length > 0 ? invoice.deliverLines : [invoice.deliverFull];
  const weights = [
    invoice.totalGross != null ? `Total gross ${kg(invoice.totalGross)} kg` : null,
    invoice.totalNet != null ? `Total nett ${kgNett(invoice.totalNet)} kg` : null,
  ].filter(Boolean);
  const deliverTo = (
    <>
      <div className="dnt-pre">
        <span className="dnt-cust">{invoice.deliverName ?? ""}</span>
        {deliverLines.filter(Boolean).join("\n")}
      </div>
      {weights.length > 0 && <div className="dnt-tel dnt-pre">{weights.join("\n")}</div>}
    </>
  );

  // ── header (page 1) ──────────────────────────────────────────────────────────────
  const header: ReactNode = ledger ? (
    <>
      <LedgerTop title={invoice.estimate ? "Pre-commercial invoice" : "Commercial invoice"} no={no} date={date} pageCount={pages?.length ?? 1} />
      <div className="dnl-head">
        <div>
          <div className="dnt-lab">Invoice to</div>
          {invoiceTo}
        </div>
        <div>
          <div className="dnt-lab">Deliver to</div>
          {deliverTo}
        </div>
        <div>
          <div className="dnt-lab">Details</div>
          <LedgerDetails
            rows={[
              ["Order no.", invoice.orderNo, true],
              ["Account ref.", invoice.accountRef ?? "", true],
              ["Payment terms", "30 days"],
              ["Country of origin", "NI"],
              ["Incoterms", "DDP as per consignee"],
            ]}
          />
        </div>
      </div>
    </>
  ) : (
    <>
      <GreenBandTop
        title={invoice.estimate ? "Pre-Commercial Invoice" : "Commercial Invoice"}
        longTitle={invoice.estimate ? "longer" : true}
        no={no}
        strip={[
          ["Date", date],
          ["Order no.", invoice.orderNo],
          ["Acc. ref.", invoice.accountRef ?? ""],
          ["Payment terms", "30 days"],
          ["Origin", "NI"],
          ["Incoterms", "DDP as per consignee"],
        ]}
      />
      <div className="dnb-two">
        <div className="dnb-card">
          <div className="dnt-lab">Invoice to</div>
          {invoiceTo}
        </div>
        <div className="dnb-card">
          <div className="dnt-lab">Deliver to</div>
          {deliverTo}
        </div>
      </div>
    </>
  );

  // ── items; Nett / V.A.T. / Total foot on the last page ─────────────────────────────
  const totalRow = (label: string, value: number, className?: string) => (
    <tr className={className}>
      <td colSpan={5} className="dnt-n">
        {label}
      </td>
      <td className="dnt-n">£{money(value)}</td>
    </tr>
  );
  const itemsTable = (rows: CommercialInvoiceItem[], withTotals: boolean) => (
    <table className="dnt-items">
      <colgroup>
        <col />
        <col className="dnt-c-hs" />
        <col className="dnt-c-weight" />
        <col className="dnt-c-unit" />
        <col className="dnt-c-rate" />
        <col className="dnt-c-nett" />
      </colgroup>
      <thead>
        <tr>
          <th>Description</th>
          <th className="dnt-c">HS tariff code</th>
          <th className="dnt-n">Weight (kg)</th>
          <th className="dnt-n">Units</th>
          <th className="dnt-n">Rate</th>
          <th className="dnt-n">Nett</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((d, i) => (
          <tr key={i}>
            <td>{itemText(d)}</td>
            <td className={`dnt-c${ledger ? " dnl-mono" : ""}`}>{d.hsCode ?? ""}</td>
            <td className="dnt-n">{d.weight != null ? kg(d.weight) : ""}</td>
            <td className="dnt-n">{money(d.units)}</td>
            <td className="dnt-n">{d.rate != null ? money(d.rate) : ""}</td>
            <td className="dnt-n">{money(d.nett)}</td>
          </tr>
        ))}
      </tbody>
      {withTotals && (
        <tfoot>
          {totalRow("Nett", invoice.nett, "dnt-total")}
          {totalRow("V.A.T.", invoice.vat)}
          {totalRow("Total", invoice.total, "dnt-grand")}
        </tfoot>
      )}
    </table>
  );

  // ── footer (last page): the small print, pinned to the page foot ───────────────────
  const footer = (
    <TemplateEnd
      template={template}
      pin
      terms={
        ledger ? (
          <>
            {TERMS1} {TERMS2}
            <br />
            {TRADE} {VAT_EORI}. All major credit cards accepted.
          </>
        ) : (
          <>
            <b>{TERMS1}</b> {TERMS2} {TRADE} {VAT_EORI}. All major credit cards accepted.
          </>
        )
      }
    />
  );

  return (
    <TemplatePages
      template={template}
      pages={pages}
      measureRef={measureRef}
      probes={probes}
      items={invoice.items}
      header={header}
      table={itemsTable}
      mid={<CommercialInvoiceWeights invoice={invoice} />}
      midPage={midPage}
      footer={footer}
    />
  );
}
