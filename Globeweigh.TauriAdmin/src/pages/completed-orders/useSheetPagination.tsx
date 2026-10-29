import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";

// The A4 pagination behind the printed Evergreen documents (DeliveryNoteSheet,
// CommercialInvoiceSheet). Both render on the same .dn-page geometry (DeliveryNote.css),
// so both are split into pages by the same measuring pass: render everything on one
// auto-height .dn-measure page, read the real geometry, then split the .dn-items rows.
//
// The document's measuring page must contain {probes}, a table.dn-items with a thead,
// the element matching `footerStart` (top of the last-page footer blocks) and .dn-office
// (the head-office banner that ends the page), or the selectors `options` names instead.

// Web-font readiness probe (see the measuring pass). The fallback stack must be the
// app's font stack (--app-font in App.css and print-base.css) WITHOUT Poppins, so that
// until Poppins is applied both probe spans render in the very same fallback face.
export const FALLBACK_FONT_STACK =
  'ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';
// The weights the measured parts of the documents use: 400 item rows, 600 table header
// and details labels, 700 tagline and signature boxes.
const FONT_PROBE_WEIGHTS = [400, 600, 700] as const;
const FONT_PROBE_TEXT = "Delivery Note 0123456789 Per Pallet";
const fontProbeStyle = {
  position: "absolute",
  visibility: "hidden",
  whiteSpace: "nowrap",
  left: 0,
  top: 0,
} as const;
// How long the measuring pass will wait for Poppins before measuring regardless, and
// how often it re-checks meanwhile. Virtual time makes both free on the server.
const FONT_WAIT_MS = 3000;
const FONT_POLL_MS = 100;

// True once every probed weight renders differently in the app stack than in the
// fallback stack, i.e. Poppins is applied for all of them.
function webFontApplied(sheet: HTMLElement): boolean {
  const probes = Array.from(sheet.querySelectorAll<HTMLElement>(".dn-font-probe"));
  if (probes.length === 0) return true;
  return probes.every((p) => {
    const app = p.querySelector<HTMLElement>('[data-font="app"]');
    const fallback = p.querySelector<HTMLElement>('[data-font="fallback"]');
    return !app || !fallback || app.offsetWidth !== fallback.offsetWidth;
  });
}

// A web font the measuring pass must wait for: `family` is the full font-family value
// (web font first, then FALLBACK_FONT_STACK), `weights` the weights the document sets in it.
export interface ProbeFont {
  family: string;
  weights: readonly number[];
}

// The app font (Poppins) at the weights the original note and the invoice use.
const APP_FONT_PROBES: ProbeFont[] = [{ family: "var(--app-font)", weights: FONT_PROBE_WEIGHTS }];

// Invisible probes for the measuring page: the exact printable height (276mm) in px, and
// the same text in each probed stack and in the stack minus its web font, per weight the
// document uses — equal widths = that web font is not in yet.
function buildProbes(fonts: ProbeFont[]) {
  return (
    <>
      <div
        className="dn-cap-probe"
        style={{
          position: "absolute",
          visibility: "hidden",
          height: "276mm",
          width: 1,
          left: 0,
          top: 0,
        }}
      />
      {fonts.flatMap((f) =>
        f.weights.map((w) => (
          <span key={`${f.family}|${w}`} className="dn-font-probe" data-weight={w} style={fontProbeStyle}>
            <span data-font="app" style={{ fontFamily: f.family, fontWeight: w }}>
              {FONT_PROBE_TEXT}
            </span>
            <span data-font="fallback" style={{ fontFamily: FALLBACK_FONT_STACK, fontWeight: w }}>
              {FONT_PROBE_TEXT}
            </span>
          </span>
        )),
      )}
    </>
  );
}

// Where the measuring pass looks. The defaults are the original note's (and the invoice's)
// classes; the newer note templates (DeliveryNoteTemplateSheet) name their own.
export interface SheetPaginationOptions {
  /** The items table: a thead, and one tbody tr per item. */
  table?: string;
  /** The last block of the last-page footer, i.e. the bottom of the page's content. */
  end?: string;
  /**
   * A block that follows the items but is NOT footer (the commercial invoice's weights by
   * HS tariff code): everything from the last item row down to the bottom of the LAST
   * element matching this selector list. It goes under the last rows when it fits there,
   * else at the top of the next page; the footer then follows on that page or the next.
   * The page it lands on is returned as `midPage`. Unset = no such block.
   */
  afterItems?: string;
  /** Web fonts to wait for before measuring. Default: the app font. */
  fonts?: ProbeFont[];
}

// `doc` is the document object (note / invoice) whose identity starts a new measure;
// `items` are its rows, one per tbody tr of the measuring page's .dn-items table.
// Returns null pages while measuring (render the .dn-measure page with measureRef and
// probes), then the rows split per page.
export function useSheetPagination<T>(
  doc: object,
  items: T[],
  footerStart: string,
  onReady?: () => void,
  options: SheetPaginationOptions = {},
) {
  const tableSel = options.table ?? ".dn-items";
  const endSel = options.end ?? ".dn-office";
  const afterItemsSel = options.afterItems ?? "";
  const fonts = options.fonts ?? APP_FONT_PROBES;
  // Keyed on the list's content, so a caller's inline array does not rebuild the probes.
  const fontsKey = JSON.stringify(fonts);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const probes = useMemo(() => buildProbes(fonts), [fontsKey]);
  // The pagination result, tagged with the document it was measured for. `pages` is null
  // (= measuring pass: everything on one auto-height page) until the current document has
  // been measured, so a different or reloaded document starts over by itself.
  //
  // Deliberately derived from the document's identity rather than reset by an effect
  // (useEffect(() => setPages(null), [note])). That reset ran on mount too, AFTER the
  // layout effect below had already measured: both updates landed in one re-render
  // (chunks, then null), which left the layout effect's deps reading [note, null]
  // exactly as before, so it never ran again. The sheet then stayed in the measuring
  // layout for good — auto-height page, footer directly under the items, Best Fit
  // checkbox doing nothing — and the server print captured that same layout.
  const [measured, setMeasured] = useState<{ doc: object; pages: T[][]; midPage: number } | null>(null);
  const pages = measured != null && measured.doc === doc ? measured.pages : null;
  // The page the afterItems block goes on (the last page when there is none).
  const midPage = pages != null ? measured!.midPage : 0;
  const measureRef = useRef<HTMLDivElement>(null);
  // Bumped to re-run the measuring pass while it waits for the web font (see below).
  const [fontTick, setFontTick] = useState(0);
  // performance.now() when the wait for the web font began; null = not waiting.
  const fontWaitStart = useRef<number | null>(null);

  // Measuring pass → split the item rows into pages, using the real rendered
  // geometry (bounding rects include every margin, so pages pack tight). Page 1
  // loses height to the header blocks; the last page must also fit the
  // footer blocks.
  useLayoutEffect(() => {
    if (pages != null) return;
    const sheet = measureRef.current;
    if (!sheet) return;
    const q = (sel: string) => {
      const el = sheet.querySelector(sel);
      return el instanceof HTMLElement ? el : null;
    };
    const probe = q(".dn-cap-probe");
    const table = q(tableSel);
    const thead = q(`${tableSel} thead`);
    const footer = q(footerStart);
    const office = q(endSel);
    if (!probe || !table || !thead) return;

    // Exact printable height (276mm) in px, straight from the browser.
    const capPx = probe.offsetHeight - 4;

    // Measure with the real font or not at all. Poppins is a web font declared with
    // font-display: swap, so the very first layout runs on the fallback face and only
    // later swaps Poppins in — whose rows are taller. Paginating on the fallback
    // numbers packs each page too tight: the last rows of a full page land beyond the
    // 276mm box and overflow: hidden silently clips them off the docket (observed: a
    // 45-line note printed lines 1–26 then 29–45).
    //
    // "Is Poppins in yet?" is answered by MEASURING, not by document.fonts: each weight
    // the document uses is rendered twice, once in the app font stack and once in the
    // same stack minus Poppins, and the widths differ only once the web font is applied.
    // document.fonts.ready is NOT trusted: on the server's Chrome 109, running headless
    // with a warm HTTP cache (the print worker's steady state), fonts.status flips to
    // "loading" the moment this layout is forced and never flips back, and the ready
    // promise never resolves — so a pass that waited on it never paginated, and every
    // auto-printed note came out in this measuring layout (client report, 2026-09-15).
    // Instead the pass re-runs on a short timer (fonts.ready is also hooked, as an early
    // wake-up) and gives up waiting after FONT_WAIT_MS, measuring with whatever is on
    // screen — a slightly loose page beats no pagination at all. The app has Poppins
    // loaded long before a document is previewed, so the wait only ever bites on the
    // standalone print page.
    if (!webFontApplied(sheet)) {
      if (fontWaitStart.current == null) fontWaitStart.current = performance.now();
      if (performance.now() - fontWaitStart.current < FONT_WAIT_MS) {
        let stale = false;
        const wake = () => {
          if (stale) return;
          stale = true;
          setFontTick((n) => n + 1);
        };
        const timer = window.setTimeout(wake, FONT_POLL_MS);
        if (document.fonts) void document.fonts.ready.then(wake);
        return () => {
          stale = true;
          window.clearTimeout(timer);
        };
      }
    }
    fontWaitStart.current = null;
    const sheetTop = sheet.getBoundingClientRect().top;
    // Everything above the first item row on page 1, margins included.
    const headerConsumed = table.getBoundingClientRect().top - sheetTop + thead.offsetHeight;
    const theadConsumed = thead.offsetHeight;
    // Footer blocks through to the office banner, plus the gap above them.
    const footerH =
      footer && office
        ? office.getBoundingClientRect().bottom - footer.getBoundingClientRect().top + 30
        : 280;

    const rowEls = Array.from(sheet.querySelectorAll(`${tableSel} tbody tr`));
    const chunks: T[][] = [];
    let current: T[] = [];
    let used = 0;
    let avail = capPx - headerConsumed;
    rowEls.forEach((el, i) => {
      const rowH = el.getBoundingClientRect().height;
      if (used + rowH > avail && current.length > 0) {
        chunks.push(current);
        current = [];
        used = 0;
        avail = capPx - theadConsumed;
      }
      current.push(items[i]);
      used += rowH;
    });
    chunks.push(current);
    // The block after the items (not footer): from under the last row (or the header row,
    // with no items) to the bottom of its last part, so the gap above it and anything in
    // between (the templates' totals rows) come with it. Under the last rows if it fits,
    // else it starts the next page, which has the table's header row over it at most.
    const midEnds = afterItemsSel ? sheet.querySelectorAll(afterItemsSel) : null;
    const midEnd = midEnds && midEnds.length > 0 ? midEnds[midEnds.length - 1] : null;
    let midPage = -1;
    if (midEnd) {
      const above = rowEls.length > 0 ? rowEls[rowEls.length - 1] : thead;
      const midH = midEnd.getBoundingClientRect().bottom - above.getBoundingClientRect().bottom + 8;
      if (used + midH > avail) {
        chunks.push([]);
        used = 0;
        avail = capPx - theadConsumed;
      }
      used += midH;
      midPage = chunks.length - 1;
    }
    // Footer blocks don't fit under the last rows — give them their own page.
    if (used + footerH > avail) chunks.push([]);
    setMeasured({ doc, pages: chunks, midPage: midPage >= 0 ? midPage : chunks.length - 1 });
  }, [doc, items, footerStart, tableSel, endSel, afterItemsSel, pages, fontTick]);

  // Pagination has settled — the pages on screen are the pages that will print.
  // Signalled once per document: callers commonly pass an inline lambda, and re-firing
  // on every later render would have the print page capture more than once.
  const readySignalled = useRef(false);
  useEffect(() => {
    readySignalled.current = false;
  }, [doc]);
  useEffect(() => {
    if (pages == null || readySignalled.current) return;
    readySignalled.current = true;
    onReady?.();
  }, [pages, onReady]);

  return { pages, midPage, measureRef, probes };
}
