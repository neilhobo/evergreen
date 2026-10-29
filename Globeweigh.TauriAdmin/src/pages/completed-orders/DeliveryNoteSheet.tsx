import { DeliveryNote, DeliveryNoteItem, DeliveryNoteTemplate, isCubicMeterNote } from "../../api/dispatch";
import { fmtDate } from "../../format";
import { useSheetPagination } from "./useSheetPagination";
import DeliveryNoteTemplateSheet from "./DeliveryNoteTemplateSheet";
import "./DeliveryNote.css";

interface Props {
  note: DeliveryNote;
  // Best Fit Footer: the signature/footer blocks sit directly under the order items
  // instead of pinning to the bottom of the page.
  bestFit?: boolean;
  // Fired once pagination has settled and the real pages are on screen. The headless
  // print page uses it to signal "safe to capture" (see print/main.tsx); the modal
  // has no use for it.
  onReady?: () => void;
  // Which design to draw. Normally left out: the note carries the one chosen on Admin >
  // Delivery Note (the API stamps it), so the preview, the auto-print and the emailed PDF
  // always agree. That tab's own previews pass it to show every design side by side.
  template?: DeliveryNoteTemplate;
}

// The delivery note — the A4 page divs and nothing else. Shared by the on-screen preview
// (DeliveryNoteModal) and the server-side print page, so what the Weighbridge Printer
// produces is rendered by exactly the same code as the preview. Anything added here must
// therefore stay free of modal/app chrome.
export default function DeliveryNoteSheet({ template, ...props }: Props) {
  const t = template ?? props.note.template ?? "classic";
  // Keyed on the design, so switching one starts a fresh measuring pass.
  if (t === "ledger" || t === "greenband")
    return <DeliveryNoteTemplateSheet key={t} template={t} {...props} />;
  return <ClassicDeliveryNoteSheet key="classic" {...props} />;
}

// The original design, carried over from the client's printed Evergreen note.
function ClassicDeliveryNoteSheet({ note, bestFit = false, onReady }: Omit<Props, "template">) {
  // Pages measured from real rendered geometry — see useSheetPagination (shared with the
  // commercial invoice, which prints on the same page template).
  const { pages, measureRef, probes } = useSheetPagination(note, note.items, ".dn-sign", onReady);

  // "10 X 60 litre MPC @ 70 Per Pallet" — same wording the client's notes use.
  // Raw peat (sold by the cubic meter) has no pallets to describe: just its name.
  const itemText = (d: DeliveryNoteItem) =>
    d.soldBy === "CubicMeter"
      ? d.description ?? ""
      : `${d.pallets} X ${d.size != null ? `${d.size}L ` : ""}${d.description ?? ""} @ ${d.upp} Per Pallet`;
  const cubicNote = isCubicMeterNote(note.items);

  const address =
    note.addressLines.length > 0 ? note.addressLines.join("\n") : note.addressFull ?? "";

  const header = (
    <>
      <div className="dn-note-no">
        <span>{note.confirmation ? "Order Confirmation" : "Delivery Note"}</span>
        <span className="dn-note-box">{note.noteNo}</span>
      </div>

      <div className="dn-top">
        <div className="dn-logo-wrap">
          <img src="images/EvergreenLogo.png" alt="Evergreen Horticulture" />
          <div className="dn-tagline">Manufacturers of Horticultural Products</div>
        </div>
      </div>

      {/* The order confirmation says so across the page, not only beside the number. */}
      {note.confirmation && <div className="dn-doc-title">Order Confirmation</div>}

      <div className="dn-head">
        <div>
          <div className="dn-box-label">To:</div>
          <div className="dn-box">{[note.customerName, address].filter(Boolean).join("\n")}</div>
        </div>
        <div>
          <div className="dn-box-label">Delivery Instructions</div>
          <div className="dn-box">
            {[note.comments, note.telephone].filter(Boolean).join("\n\n")}
          </div>
        </div>
        <div style={{ position: "relative" }}>
          <img
            className="dn-greendays"
            src="images/GreendaysLogo.png"
            alt="Greendays Horticulture"
          />
          {/* Spacer matching the To/Instructions labels so the table tops align. */}
          <div className="dn-box-label">&nbsp;</div>
          <table className="dn-details">
            <tbody>
              <tr>
                <td className="dn-detail-label">Date:</td>
                <td>{note.date ? fmtDate(note.date) : ""}</td>
              </tr>
              <tr>
                <td className="dn-detail-label">Order No.</td>
                <td>{note.poNumber ?? ""}</td>
              </tr>
              <tr>
                <td className="dn-detail-label">Acc. Ref.</td>
                <td>{note.accountRef ?? ""}</td>
              </tr>
              <tr>
                <td className="dn-detail-label">Haulier:</td>
                <td>{[note.haulierName, note.vehicleReg].filter(Boolean).join(" REG ")}</td>
              </tr>
              <tr>
                <td className="dn-detail-label">Trailer No:</td>
                <td>{note.trailerNo ?? ""}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </>
  );

  // An all-peat note drops the pallet columns and totals in m³. A peat line on a mixed note
  // leaves its pallet cells blank and shows its m³ in the total column.
  const itemsTable = (rows: DeliveryNoteItem[]) => (
    <table className="dn-items">
      <thead>
        <tr>
          <th>Description</th>
          {!cubicNote && (
            <th className="dn-num dn-pallets">
              No.
              <br />
              Pallets
            </th>
          )}
          {!cubicNote && <th className="dn-num dn-bpp">Bags per Pallet</th>}
          <th className="dn-num dn-bags">{cubicNote ? "Total (m³)" : "Total Bags"}</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((d, i) => {
          const peat = d.soldBy === "CubicMeter";
          return (
            <tr key={i}>
              <td>{itemText(d)}</td>
              {!cubicNote && <td className="dn-num">{peat ? "" : d.pallets.toLocaleString()}</td>}
              {!cubicNote && <td className="dn-num">{peat ? "" : d.upp.toLocaleString()}</td>}
              <td className="dn-num">
                {peat
                  ? `${(d.cubicMeters ?? 0).toLocaleString()}${cubicNote ? "" : " m³"}`
                  : d.totalBags.toLocaleString()}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );

  const footerBlocks = (
    <>
      <div className="dn-sign">
        <div className="dn-sign-box">Driver Signature:</div>
        <div className="dn-sign-box">Received in Good Condition:</div>
      </div>

      <div className="dn-footer">
        <div className="dn-vat">V.A.T. Reg. No. GB 701 5625 67</div>
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
    <div className="dn-doc">
      {pages == null ? (
        // Measuring pass: everything on one auto-height page, then split.
        <div className="dn-page dn-measure" ref={measureRef}>
          {probes}
          {header}
          {itemsTable(note.items)}
          {footerBlocks}
        </div>
      ) : (
        pages.map((rows, pi) => (
          <div className={`dn-page${bestFit ? " dn-best-fit" : ""}`} key={pi}>
            {pi === 0 && header}
            {rows.length > 0 && itemsTable(rows)}
            {pi === pages.length - 1 && footerBlocks}
          </div>
        ))
      )}
    </div>
  );
}
