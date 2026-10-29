import { CommercialInvoice, CommercialInvoiceItem, invoiceNumber } from "../../api/dispatch";
import { fmtDate } from "../../format";
import { useSheetPagination } from "./useSheetPagination";
import CommercialInvoiceTemplateSheet from "./CommercialInvoiceTemplateSheet";
import CommercialInvoiceWeights, { kgNett } from "./CommercialInvoiceWeights";
import "./DeliveryNote.css";
import "./CommercialInvoice.css";

interface Props {
  invoice: CommercialInvoice;
  // Called once pagination has settled (the server print page's capture signal).
  onReady?: () => void;
}

const money = (n: number) =>
  n.toLocaleString("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// Whole kg with a thousands comma: 24,500.
const kg = (n: number) => n.toLocaleString("en-GB", { maximumFractionDigits: 0 });

// The commercial invoice — the delivery note's A4 template (same .dn-page geometry,
// logo, head-office banner and pagination) with the client's invoice fields: invoice no
// (= LoadNo-OrderNo),invoice / deliver-to addresses with the order's weights, HS tariff codes,
// prices, and the Nett / V.A.T. / Total box in the footer. No bank details, by request.
// The invoice follows the delivery note's design (Admin > Delivery Note; the API stamps it on
// the invoice): the newer designs are CommercialInvoiceTemplateSheet, the original is below.
export default function CommercialInvoiceSheet({ invoice, onReady }: Props) {
  const t = invoice.template ?? "classic";
  if (t === "ledger" || t === "greenband")
    return <CommercialInvoiceTemplateSheet key={t} invoice={invoice} template={t} onReady={onReady} />;
  return <ClassicCommercialInvoiceSheet key="classic" invoice={invoice} onReady={onReady} />;
}

function ClassicCommercialInvoiceSheet({ invoice, onReady }: Props) {
  // The weights per HS tariff code follow the items but are not footer: under the last rows
  // when they fit, else at the top of the next page (midPage); the footer comes after.
  const { pages, midPage, measureRef, probes } = useSheetPagination(invoice, invoice.items, ".ci-foot", onReady, {
    afterItems: ".ciw",
  });
  const weights = <CommercialInvoiceWeights invoice={invoice} />;

  // "25 X 50L Multi Purpose Compost @ 90 Per Pallet" — the delivery note's wording.
  const itemText = (d: CommercialInvoiceItem) =>
    `${d.pallets} X ${d.size != null ? `${d.size}L ` : ""}${d.description ?? ""} @ ${d.upp} Per Pallet`;

  const invoiceTo = [invoice.invoiceName, ...invoice.invoiceLines].filter(Boolean).join("\n");
  const deliverTo = [
    invoice.deliverName,
    ...(invoice.deliverLines.length > 0 ? invoice.deliverLines : [invoice.deliverFull]),
    invoice.totalGross != null ? `TOTAL GROSS ${kg(invoice.totalGross)}KG` : null,
    invoice.totalNet != null ? `TOTAL NETT ${kgNett(invoice.totalNet)}KG` : null,
  ]
    .filter(Boolean)
    .join("\n");

  const header = (
    <>
      <div className="dn-note-no">
        <span className="ci-no-label">{invoice.estimate ? "Pre-Commercial Invoice No:" : "Invoice No:"}</span>
        <span className="dn-note-box">{invoiceNumber(invoice)}</span>
      </div>

      <div className="dn-top">
        <div className="dn-logo-wrap">
          <img src="images/EvergreenLogo.png" alt="Evergreen Horticulture" />
          <div className="dn-tagline">Manufacturers of Horticultural Products</div>
        </div>
      </div>

      <div className="dn-head">
        <div>
          <div className="dn-box-label">Invoice :</div>
          <div className="dn-box">{invoiceTo}</div>
        </div>
        <div>
          <div className="dn-box-label">Deliver to :</div>
          <div className="dn-box">{deliverTo}</div>
        </div>
        <div style={{ position: "relative" }}>
          <img
            className="dn-greendays"
            src="images/GreendaysLogo.png"
            alt="Greendays Horticulture"
          />
          <div className="dn-box-label">&nbsp;</div>
          <table className="dn-details">
            <tbody>
              <tr>
                <td className="dn-detail-label">Date :</td>
                <td>{invoice.date ? fmtDate(invoice.date) : ""}</td>
              </tr>
              <tr>
                <td className="dn-detail-label">Order No.</td>
                <td>{invoice.orderNo}</td>
              </tr>
              <tr>
                <td className="dn-detail-label">Acc. Ref.</td>
                <td>{invoice.accountRef ?? ""}</td>
              </tr>
              <tr>
                <td className="dn-detail-label">Payment Terms</td>
                <td>30 DAYS</td>
              </tr>
              <tr>
                <td className="dn-detail-label">Country of Origin</td>
                <td>NI</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </>
  );

  const itemsTable = (rows: CommercialInvoiceItem[]) => (
    <table className="dn-items ci-items">
      <thead>
        <tr>
          <th>Description</th>
          <th className="ci-hs">HS Tariff Code</th>
          <th className="dn-num ci-weight">Weight (kg)</th>
          <th className="dn-num ci-unit">Unit</th>
          <th className="dn-num ci-rate">Rate</th>
          <th className="dn-num ci-nett">Nett</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((d, i) => (
          <tr key={i}>
            <td>{itemText(d)}</td>
            <td className="ci-hs">{d.hsCode ?? ""}</td>
            <td className="dn-num">{d.weight != null ? kg(d.weight) : ""}</td>
            <td className="dn-num">{money(d.units)}</td>
            <td className="dn-num">{d.rate != null ? money(d.rate) : ""}</td>
            <td className="dn-num">{money(d.nett)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );

  const footerBlocks = (
    <>
      <div className="ci-foot">
        <div className="dn-footer ci-footer">
          <div className="ci-incoterms">INCOTERMS DDP AS PER CONSIGNEE</div>
          <div className="dn-vat">V.A.T. Reg. No. GB 701 5625 67 / EORI: XI 701562567000</div>
          <div className="dn-green">
            Overdue accounts are liable to a late payment surcharge and interest will be charged
            thereafter. Goods are delivered in accordance with our terms and conditions.
          </div>
          <div>
            All complaints must be made in writing within 3 days of delivery. Title of goods will
            remain with the seller until paid in full.
          </div>
          <div>Evergreen Horticulture is the trading name of McCuskey and Co. Ltd</div>
          <div>Greendays Horticulture is a sister brand of McCuskey and Co. Ltd</div>
          <div className="dn-cards">ALL MAJOR CREDIT CARDS ACCEPTED</div>
        </div>
        <table className="ci-totals">
          <tbody>
            <tr>
              <td className="ci-total-label">Nett</td>
              <td className="ci-total-cur">£</td>
              <td className="ci-total-val">{money(invoice.nett)}</td>
            </tr>
            <tr>
              <td className="ci-total-label">V.A.T.</td>
              <td className="ci-total-cur">£</td>
              <td className="ci-total-val">{money(invoice.vat)}</td>
            </tr>
            <tr>
              <td className="ci-total-label">Total</td>
              <td className="ci-total-cur">£</td>
              <td className="ci-total-val ci-grand">{money(invoice.total)}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div className="dn-office">
        <div className="dn-office-band">
          <div>
            <strong>Head Office : </strong>
            <span className="dn-office-key">a. </span>10a Ferry Road, Coalisland, Co.Tyrone,
            BT71 4QT&nbsp;&nbsp; <span className="dn-office-key">t. </span>
            <strong>(028) 8774 0950</strong>&nbsp;&nbsp; <span className="dn-office-key">f. </span>
            <strong>(028) 8774 8237</strong>
          </div>
          <div>
            <span className="dn-office-key">w. </span>www.evergreenpeat.com&nbsp;&nbsp;{" "}
            <span className="dn-office-key">e. </span>sales@evergreenpeat.com
          </div>
        </div>
      </div>
    </>
  );

  return (
    <div className="dn-doc ci-doc">
      {pages == null ? (
        // Measuring pass: everything on one auto-height page, then split.
        <div className="dn-page dn-measure" ref={measureRef}>
          {probes}
          {header}
          {itemsTable(invoice.items)}
          {weights}
          {footerBlocks}
        </div>
      ) : (
        pages.map((rows, pi) => (
          <div className="dn-page" key={pi}>
            {pi === 0 && header}
            {rows.length > 0 && itemsTable(rows)}
            {pi === midPage && weights}
            {pi === pages.length - 1 && footerBlocks}
          </div>
        ))
      )}
    </div>
  );
}
