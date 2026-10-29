import { ReactNode, RefObject } from "react";
import { FALLBACK_FONT_STACK, ProbeFont } from "./useSheetPagination";
import "./DeliveryNote.css";
import "./DeliveryNoteTemplates.css";

// The pieces the newer document designs ("Ledger" and "Green Band", picked on Admin > Delivery
// Note) are built from, shared by the delivery note (DeliveryNoteTemplateSheet) and the
// commercial invoice (CommercialInvoiceTemplateSheet) so both documents read as one set.
// Styles: DeliveryNoteTemplates.css.

export type DocTemplate = "ledger" | "greenband";

// Web fonts each design sets text in, for the pagination's font wait.
export const TEMPLATE_FONTS: Record<DocTemplate, ProbeFont[]> = {
  ledger: [
    { family: `"Public Sans", ${FALLBACK_FONT_STACK}`, weights: [400, 500, 600, 700] },
    { family: `"IBM Plex Mono", ${FALLBACK_FONT_STACK}`, weights: [400, 500] },
  ],
  greenband: [{ family: "var(--app-font)", weights: [400, 600] }],
};

// Pagination markers every design page uses: the items table, the first tfoot row (where the
// last-page footer starts) and the last block on the page.
export const TEMPLATE_PAGINATION = { table: ".dnt-items", footerStart: ".dnt-total", end: ".dnt-end" };

export const TERMS1 =
  "Overdue accounts are liable to a late payment surcharge and interest will be charged thereafter. " +
  "Goods are delivered in accordance with our terms and conditions.";
export const TERMS2 =
  "All complaints must be made in writing within 3 days of delivery. " +
  "Title of goods will remain with the seller until paid in full.";
export const TRADE =
  "Evergreen Horticulture is the trading name of McCuskey and Co. Ltd. " +
  "Greendays Horticulture is a sister brand of McCuskey and Co. Ltd.";
const OFFICE = "10a Ferry Road, Coalisland, Co. Tyrone, BT71 4QT";
const TEL = "(028) 8774 0950";
const FAX = "(028) 8774 8237";
const WEB = "www.evergreenpeat.com";
const MAIL = "sales@evergreenpeat.com";

/** Ledger page 1 top: the logo left, the document title and number right. */
export function LedgerTop(p: { title: string; no: string; date: string; pageCount: number }) {
  return (
    <div className="dnl-top">
      <img className="dnl-logo" src="images/EvergreenLogo.png" alt="Evergreen Horticulture" />
      <div className="dnl-title">
        <div className="dnt-lab">{p.title}</div>
        <div className="dnl-no">No. {p.no}</div>
        <div className="dnl-sub">
          {p.date}
          {p.date ? " · " : ""}Page 1 of {p.pageCount}
        </div>
      </div>
    </div>
  );
}

/** Ledger's label/value list (the Details column). */
export function LedgerDetails({ rows }: { rows: [string, ReactNode, boolean?][] }) {
  return (
    <div className="dnl-kv">
      {rows.flatMap(([label, value, mono]) => [
        <div key={`l${label}`}>{label}</div>,
        <div key={`v${label}`} className={mono ? "dnl-mono" : undefined}>
          {value}
        </div>,
      ])}
    </div>
  );
}

/** Green Band page 1 top: the green brand band, then one strip of labelled values. */
export function GreenBandTop(p: {
  title: string;
  no: string;
  strip: [string, ReactNode][];
  longTitle?: boolean | "longer";
}) {
  return (
    <div className="dnb-head">
      <div className="dnb-band">
        <div className="dnb-chip">
          <img className="dnb-logo" src="images/EvergreenLogo.png" alt="Evergreen Horticulture" />
        </div>
        <div className="dnb-title">
          <div
            className={
              p.longTitle === "longer" ? "dnb-h dnb-h--longer" : p.longTitle ? "dnb-h dnb-h--long" : "dnb-h"
            }
          >
            {p.title}
          </div>
          <div className="dnb-pill">No. {p.no}</div>
          <div className="dnb-tag">Manufacturers of Horticultural Products</div>
        </div>
      </div>
      <div className="dnb-strip" style={{ gridTemplateColumns: stripColumns(p.strip.length) }}>
        {p.strip.map(([label, value]) => (
          <div key={label}>
            <div className="dnt-lab">{label}</div>
            <div className="dnb-v">{value}</div>
          </div>
        ))}
      </div>
    </div>
  );
}

// The delivery note's five-field strip keeps the mock-up's proportions (a wide Haulier / reg.).
const stripColumns = (n: number) => (n === 5 ? "0.8fr 0.9fr 0.8fr 1.6fr 0.7fr" : `repeat(${n}, 1fr)`);

/** The last block on the page: small print and head office, in the design's style.
 *  `terms` is the document's own small print. `pin` pushes it to the page foot (for a
 *  document with nothing above it to do that, i.e. no signatures). */
export function TemplateEnd({ template, terms, pin }: { template: DocTemplate; terms: ReactNode; pin?: boolean }) {
  if (template === "ledger")
    return (
      <div className={`dnt-end${pin ? " dnt-pin" : ""}`}>
        <div className="dnl-foot">
          <div>{terms}</div>
          <img className="dnl-gd" src="images/GreendaysLogo.png" alt="Greendays Horticulture" />
        </div>
        <div className="dnl-office">
          <span>
            <i>a.</i> {OFFICE}
          </span>
          <span>
            <i>t.</i> {TEL}
          </span>
          <span>
            <i>f.</i> {FAX}
          </span>
          <span>
            <i>w.</i> {WEB}
          </span>
          <span>
            <i>e.</i> {MAIL}
          </span>
        </div>
      </div>
    );
  return (
    <>
      <div className={`dnb-terms${pin ? " dnt-pin" : ""}`}>{terms}</div>
      <div className="dnt-end dnb-band2">
        <div>
          <b>Head Office</b> {OFFICE}
          <br />
          t. <b>{TEL}</b> &nbsp; f. {FAX} &nbsp; {WEB} &nbsp; {MAIL}
        </div>
        <div className="dnb-gchip">
          <img className="dnb-gd" src="images/GreendaysLogo.png" alt="Greendays Horticulture" />
        </div>
      </div>
    </>
  );
}

/** The A4 pages: a measuring pass first (everything on one auto-height page), then page 1
 *  with the header, a table on every page with rows, and the footer on the last. A footer
 *  that does not fit under the last rows gets a page of its own, which still carries the
 *  table's totals under a bare header row. */
export function TemplatePages<T>(p: {
  template: DocTemplate;
  bestFit?: boolean;
  pages: T[][] | null;
  measureRef: RefObject<HTMLDivElement | null>;
  probes: ReactNode;
  items: T[];
  header: ReactNode;
  table: (rows: T[], withTotals: boolean) => ReactNode;
  footer: ReactNode;
  docClass?: string;
  // A block after the items that is not footer (the invoice's weights by HS tariff code),
  // and the page the pagination put it on (useSheetPagination's afterItems / midPage). The
  // table's totals go on that page too, so they stay with the items; the footer is still
  // the last page's. Without it, the totals go on the last page as before.
  mid?: ReactNode;
  midPage?: number;
}) {
  const pageClass = `dn-page dnt-page dnt-${p.template}${p.bestFit ? " dnt-best-fit" : ""}`;
  return (
    <div className={`dn-doc${p.docClass ? ` ${p.docClass}` : ""}`}>
      {p.pages == null ? (
        <div className={`${pageClass} dn-measure`} ref={p.measureRef}>
          {p.probes}
          {p.header}
          {p.table(p.items, true)}
          {p.mid}
          {p.footer}
        </div>
      ) : (
        p.pages.map((rows, pi) => {
          const last = pi === p.pages!.length - 1;
          const totals = p.mid != null && p.midPage != null ? pi === p.midPage : last;
          return (
            <div className={pageClass} key={pi}>
              {pi === 0 && p.header}
              {(rows.length > 0 || totals) && p.table(rows, totals)}
              {p.mid != null && pi === p.midPage && p.mid}
              {last && p.footer}
            </div>
          );
        })
      )}
    </div>
  );
}
