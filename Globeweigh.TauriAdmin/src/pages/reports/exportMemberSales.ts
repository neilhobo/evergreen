import type { MemberSalesReport, MemberSalesProduct } from "../../api/reports";

// The member-sales report as a workbook — the same figures as the page, laid out for the
// person it gets emailed to: Summary (turnover by member, with the branch split for a member
// with several accounts), Monthly, Top 20 by Value, Top 20 by Quantity, All Products, then the
// data sheets (Ledger / Invoices / Invoice Lines) and Notes. Every summary cell is a formula
// over the data sheets with its value cached, so the file reads right in any viewer and still
// recalculates if a data row is corrected. exceljs is lazy-loaded (it is ~1 MB).
export async function buildMemberSalesWorkbook(report: MemberSalesReport, title: string): Promise<Blob> {
  const ExcelJS = (await import("exceljs")).default;
  const wb = new ExcelJS.Workbook();
  wb.creator = "Globeweigh (Sage mirror)";
  wb.created = new Date();

  const members = report.members;
  const months = report.months;
  const from = new Date(report.from);
  const to = new Date(report.to);
  const asOf = report.sync.lastSuccessUtc ? new Date(report.sync.lastSuccessUtc) : null;
  const ledger = report.transactions;
  const invoices = report.invoices;
  const lines = report.lines;
  const products = report.products;
  const topByValue = products.slice(0, 20);
  const topByQty = [...products].sort((a, b) => b.qty - a.qty || a.stockCode.localeCompare(b.stockCode)).slice(0, 20);
  const unsplit = report.unsplit;

  const FONT = { name: "Arial", size: 10 };
  const BOLD = { name: "Arial", size: 10, bold: true };
  const TITLE = { name: "Arial", size: 14, bold: true };
  const MUTED = { name: "Arial", size: 9, color: { argb: "FF64748B" } };
  const HEAD_FILL = { type: "pattern" as const, pattern: "solid" as const, fgColor: { argb: "FFE8EDF4" } };
  const TOTAL_FILL = { type: "pattern" as const, pattern: "solid" as const, fgColor: { argb: "FFF1F5F9" } };
  const GBP = '£#,##0.00;(£#,##0.00);"-"';
  const QTY = '#,##0;(#,##0);"-"';
  const PCT = '0.0%;(0.0%);"-"';
  const thin = { style: "thin" as const, color: { argb: "FFBCC6D3" } };
  const border = { top: thin, left: thin, bottom: thin, right: thin };
  const date = (d: Date) => d.toLocaleDateString("en-GB");
  const r2 = (n: number) => Math.round(n * 100) / 100;
  const Q = (s: string) => `"${String(s).replace(/"/g, '""')}"`;
  const F = (formula: string, result: number) => ({ formula, result });
  type Row = InstanceType<typeof ExcelJS.Workbook>["worksheets"][number]["lastRow"];

  function styleHeader(row: NonNullable<Row>) {
    row.eachCell((c) => {
      c.font = BOLD;
      c.fill = HEAD_FILL;
      c.border = border;
      c.alignment = { vertical: "middle", wrapText: true };
    });
    row.height = 30;
  }
  function styleBody(row: NonNullable<Row>) {
    row.eachCell((c) => {
      c.font = FONT;
      c.border = border;
    });
  }
  function styleTotal(row: NonNullable<Row>) {
    row.eachCell((c) => {
      c.font = BOLD;
      c.fill = TOTAL_FILL;
      c.border = border;
    });
  }

  const sumMember = (m: string) => r2(ledger.filter((l) => l.member === m).reduce((s, l) => s + l.net, 0));
  const sumAccount = (a: string) => r2(ledger.filter((l) => l.accountRef === a).reduce((s, l) => s + l.net, 0));
  const grand = r2(ledger.reduce((s, l) => s + l.net, 0));
  const countInv = (pred: (i: (typeof invoices)[number]) => boolean, type: string) =>
    invoices.filter((i) => pred(i) && i.type === type).length;

  // ---- Summary ----
  {
    const ws = wb.addWorksheet("Summary", { views: [{ showGridLines: false }] });
    ws.columns = [{ width: 26 }, { width: 34 }, { width: 11 }, { width: 11 }, { width: 22 }, { width: 11 }];
    ws.getCell("A1").value = `${title} — purchases from Evergreen Horticulture`;
    ws.getCell("A1").font = TITLE;
    ws.getCell("A2").value = `Purchase turnover ${date(from)} to ${date(to)} · net of VAT · GBP · source: Sage 50 (posted invoices less credit notes)`;
    ws.getCell("A2").font = MUTED;
    ws.getCell("A3").value = `${asOf ? `Sage figures as read ${asOf.toLocaleString("en-GB")}.` : "Sage figures not yet read."} Definitions and reconciliation on the Notes sheet.`;
    ws.getCell("A3").font = MUTED;

    let r = 5;
    ws.getRow(r).values = ["Member", "Sage account(s)", "Invoices", "Credit notes", "Purchase turnover (£)", "Share"];
    styleHeader(ws.getRow(r));
    const first = r + 1;
    const totalRow = first + members.length;
    for (const m of members) {
      r++;
      const row = ws.getRow(r);
      row.values = [
        m.name,
        m.accounts.map((a) => a.accountRef).join(", "),
        F(`COUNTIFS(Invoices!$E:$E,${Q(m.name)},Invoices!$C:$C,"Invoice")`, countInv((i) => i.member === m.name, "Invoice")),
        F(`COUNTIFS(Invoices!$E:$E,${Q(m.name)},Invoices!$C:$C,"Credit")`, countInv((i) => i.member === m.name, "Credit")),
        F(`SUMIFS(Ledger!$H:$H,Ledger!$D:$D,${Q(m.name)})`, sumMember(m.name)),
        F(`IF($E$${totalRow}=0,0,E${r}/$E$${totalRow})`, grand ? Math.round((sumMember(m.name) / grand) * 10000) / 10000 : 0),
      ];
      styleBody(row);
      row.getCell(5).numFmt = GBP;
      row.getCell(6).numFmt = PCT;
      row.getCell(3).numFmt = QTY;
      row.getCell(4).numFmt = QTY;
    }
    r++;
    const tot = ws.getRow(r);
    tot.values = [
      "All members",
      "",
      F(`SUM(C${first}:C${r - 1})`, countInv(() => true, "Invoice")),
      F(`SUM(D${first}:D${r - 1})`, countInv(() => true, "Credit")),
      F(`SUM(E${first}:E${r - 1})`, grand),
      F(`SUM(F${first}:F${r - 1})`, grand ? 1 : 0),
    ];
    styleTotal(tot);
    tot.getCell(5).numFmt = GBP;
    tot.getCell(6).numFmt = PCT;

    for (const m of members.filter((x) => x.accounts.length > 1)) {
      r += 2;
      ws.getCell(`A${r}`).value = `${m.name} — consolidated above, by account:`;
      ws.getCell(`A${r}`).font = BOLD;
      r++;
      ws.getRow(r).values = ["Account name (Sage)", "Sage account", "Invoices", "Credit notes", "Purchase turnover (£)", `Share of ${m.name}`];
      styleHeader(ws.getRow(r));
      const dFirst = r + 1;
      const dTotal = dFirst + m.accounts.length;
      const mTotal = sumMember(m.name);
      for (const a of m.accounts) {
        r++;
        const row = ws.getRow(r);
        row.values = [
          a.name,
          a.accountRef,
          F(`COUNTIFS(Invoices!$D:$D,${Q(a.accountRef)},Invoices!$C:$C,"Invoice")`, countInv((i) => i.accountRef === a.accountRef, "Invoice")),
          F(`COUNTIFS(Invoices!$D:$D,${Q(a.accountRef)},Invoices!$C:$C,"Credit")`, countInv((i) => i.accountRef === a.accountRef, "Credit")),
          F(`SUMIFS(Ledger!$H:$H,Ledger!$C:$C,${Q(a.accountRef)})`, sumAccount(a.accountRef)),
          F(`IF($E$${dTotal}=0,0,E${r}/$E$${dTotal})`, mTotal ? Math.round((sumAccount(a.accountRef) / mTotal) * 10000) / 10000 : 0),
        ];
        styleBody(row);
        row.getCell(5).numFmt = GBP;
        row.getCell(6).numFmt = PCT;
      }
      r++;
      const dt = ws.getRow(r);
      dt.values = [
        `${m.name} total`,
        "",
        F(`SUM(C${dFirst}:C${r - 1})`, countInv((i) => i.member === m.name, "Invoice")),
        F(`SUM(D${dFirst}:D${r - 1})`, countInv((i) => i.member === m.name, "Credit")),
        F(`SUM(E${dFirst}:E${r - 1})`, mTotal),
        F(`SUM(F${dFirst}:F${r - 1})`, mTotal ? 1 : 0),
      ];
      styleTotal(dt);
      dt.getCell(5).numFmt = GBP;
      dt.getCell(6).numFmt = PCT;
    }
  }

  // ---- Monthly ----
  {
    const ws = wb.addWorksheet("Monthly", { views: [{ showGridLines: false, state: "frozen", xSplit: 1, ySplit: 4 }] });
    ws.getCell("A1").value = "Monthly purchases by member (£, net of VAT) — Sage posted invoices less credit notes";
    ws.getCell("A1").font = TITLE;
    ws.getCell("A2").value = `${date(from)} to ${date(to)} by transaction date`;
    ws.getCell("A2").font = MUTED;
    ws.columns = [{ width: 28 }, ...months.map(() => ({ width: 13 })), { width: 15 }];
    const col = (i: number) => ws.getColumn(i).letter;
    const head = ws.getRow(4);
    head.values = ["Member", ...months.map((m) => m.label), "Total"];
    styleHeader(head);

    const block = (
      items: { key: string; label: string }[],
      keyCol: string,
      keyOf: (l: (typeof ledger)[number]) => string,
      startRow: number,
    ) => {
      let r = startRow;
      for (const it of items) {
        r++;
        const row = ws.getRow(r);
        const vals: unknown[] = [it.label];
        for (const m of months) {
          const v = ledger.filter((l) => keyOf(l) === it.key && l.month === m.month).reduce((s, l) => s + l.net, 0);
          vals.push(F(`SUMIFS(Ledger!$H:$H,Ledger!$${keyCol}:$${keyCol},${Q(it.key)},Ledger!$B:$B,${Q(m.month)})`, r2(v)));
        }
        vals.push(F(`SUM(B${r}:${col(months.length + 1)}${r})`, r2(ledger.filter((l) => keyOf(l) === it.key).reduce((s, l) => s + l.net, 0))));
        row.values = vals as never;
        styleBody(row);
        for (let c = 2; c <= months.length + 2; c++) row.getCell(c).numFmt = GBP;
      }
      r++;
      const tot = ws.getRow(r);
      const tv: unknown[] = ["Total"];
      for (let c = 2; c <= months.length + 2; c++) {
        const L = col(c);
        const inBlock = (l: (typeof ledger)[number]) => items.some((it) => keyOf(l) === it.key);
        const v =
          c <= months.length + 1
            ? ledger.filter((l) => inBlock(l) && l.month === months[c - 2].month).reduce((s, l) => s + l.net, 0)
            : ledger.filter(inBlock).reduce((s, l) => s + l.net, 0);
        tv.push(F(`SUM(${L}${startRow + 1}:${L}${r - 1})`, r2(v)));
      }
      tot.values = tv as never;
      styleTotal(tot);
      for (let c = 2; c <= months.length + 2; c++) tot.getCell(c).numFmt = GBP;
      return r;
    };

    let r = block(members.map((m) => ({ key: m.name, label: m.name })), "D", (l) => l.member, 4);
    for (const m of members.filter((x) => x.accounts.length > 1)) {
      r += 2;
      ws.getCell(`A${r}`).value = `${m.name} by account`;
      ws.getCell(`A${r}`).font = BOLD;
      r++;
      const h2 = ws.getRow(r);
      h2.values = ["Account", ...months.map((x) => x.label), "Total"];
      styleHeader(h2);
      r = block(m.accounts.map((a) => ({ key: a.accountRef, label: `${a.name} (${a.accountRef})` })), "C", (l) => l.accountRef, r);
    }
  }

  // ---- product sheets ----
  function productSheet(name: string, subtitle: string, list: MemberSalesProduct[], measure: "value" | "qty") {
    const ws = wb.addWorksheet(name, { views: [{ showGridLines: false, state: "frozen", xSplit: 3, ySplit: 4 }] });
    ws.getCell("A1").value = subtitle;
    ws.getCell("A1").font = TITLE;
    ws.getCell("A2").value = `${date(from)} to ${date(to)} · from the item lines of posted invoices and credit notes · quantity in the unit Sage invoices (bags/units) · ${measure === "value" ? "£ net of VAT" : "units"}`;
    ws.getCell("A2").font = MUTED;
    ws.columns = [{ width: 6 }, { width: 16 }, { width: 50 }, { width: 15 }, { width: 13 }, ...members.map(() => ({ width: 15 }))];
    const head = ws.getRow(4);
    head.values = ["Rank", "Stock code", "Description", "Total value (£)", "Total quantity", ...members.map((m) => `${m.name}${measure === "value" ? " (£)" : " (qty)"}`)];
    styleHeader(head);
    const colL = measure === "value" ? "J" : "H"; // Invoice Lines: H = qty, J = net
    let r = 4;
    list.forEach((p, i) => {
      r++;
      const row = ws.getRow(r);
      const vals: unknown[] = [
        i + 1,
        p.stockCode,
        p.description,
        F(`SUMIFS('Invoice Lines'!$J:$J,'Invoice Lines'!$F:$F,${Q(p.stockCode)})`, r2(p.value)),
        F(`SUMIFS('Invoice Lines'!$H:$H,'Invoice Lines'!$F:$F,${Q(p.stockCode)})`, r2(p.qty)),
      ];
      for (const m of members) {
        const by = p.byMember[m.name];
        const v = by ? (measure === "value" ? by.value : by.qty) : 0;
        vals.push(F(`SUMIFS('Invoice Lines'!$${colL}:$${colL},'Invoice Lines'!$F:$F,${Q(p.stockCode)},'Invoice Lines'!$E:$E,${Q(m.name)})`, r2(v)));
      }
      row.values = vals as never;
      styleBody(row);
      row.getCell(4).numFmt = GBP;
      row.getCell(5).numFmt = QTY;
      for (let c = 6; c < 6 + members.length; c++) row.getCell(c).numFmt = measure === "value" ? GBP : QTY;
    });
    r++;
    const tot = ws.getRow(r);
    const tv: unknown[] = [
      "",
      "Total",
      `${list.length} product lines listed`,
      F(`SUM(D5:D${r - 1})`, r2(list.reduce((s, p) => s + p.value, 0))),
      F(`SUM(E5:E${r - 1})`, r2(list.reduce((s, p) => s + p.qty, 0))),
    ];
    members.forEach((m, i) => {
      const L = ws.getColumn(6 + i).letter;
      const v = list.reduce((s, p) => {
        const by = p.byMember[m.name];
        return s + (by ? (measure === "value" ? by.value : by.qty) : 0);
      }, 0);
      tv.push(F(`SUM(${L}5:${L}${r - 1})`, r2(v)));
    });
    tot.values = tv as never;
    styleTotal(tot);
    tot.getCell(4).numFmt = GBP;
    tot.getCell(5).numFmt = QTY;
    for (let c = 6; c < 6 + members.length; c++) tot.getCell(c).numFmt = measure === "value" ? GBP : QTY;
    if (list.length < products.length) {
      r += 2;
      ws.getCell(`B${r}`).value = `Top ${list.length} of ${products.length} product lines supplied to these members in the period; the full list is on the All Products sheet.`;
      ws.getCell(`B${r}`).font = MUTED;
    }
    if (unsplit.length) {
      r += 1;
      const v = unsplit.reduce((s, l) => s + l.net, 0);
      ws.getCell(`B${r}`).value = `Excludes £${v.toLocaleString("en-GB", { minimumFractionDigits: 2 })} from ${unsplit.length} invoice(s) deleted from Sage's invoice list after posting (no item lines left to read) — they are in the turnover figures; see Notes.`;
      ws.getCell(`B${r}`).font = MUTED;
    }
  }
  productSheet("Top 20 by Value", "Top 20 product lines by turnover value", topByValue, "value");
  productSheet("Top 20 by Quantity", "Top 20 product lines by quantity sold", topByQty, "qty");
  productSheet("All Products", "Every product line supplied to the members in the period, by value", products, "value");

  // ---- data sheets ----
  function dataSheet(
    name: string,
    columns: { header: string; key: string; width: number }[],
    rows: Record<string, unknown>[],
    fmts: Record<string, string>,
    note: string,
  ) {
    const ws = wb.addWorksheet(name, { views: [{ state: "frozen", ySplit: 1 }] });
    ws.columns = columns.map((c) => ({ header: c.header, key: c.key, width: c.width }));
    styleHeader(ws.getRow(1));
    ws.getRow(1).height = 20;
    for (const row of rows) ws.addRow(row);
    ws.eachRow((row, n) => {
      if (n > 1) {
        row.eachCell((c) => {
          c.font = FONT;
        });
        for (const [k, f] of Object.entries(fmts)) row.getCell(k).numFmt = f;
      }
    });
    ws.autoFilter = { from: { row: 1, column: 1 }, to: { row: 1, column: columns.length } };
    const r = rows.length + 3;
    ws.getCell(`A${r}`).value = note;
    ws.getCell(`A${r}`).font = MUTED;
  }
  const d = (iso: string) => new Date(iso);
  dataSheet(
    "Ledger",
    [
      { header: "Date", key: "date", width: 11 },
      { header: "Month", key: "month", width: 9 },
      { header: "Account", key: "account", width: 10 },
      { header: "Member", key: "member", width: 18 },
      { header: "Type", key: "type", width: 9 },
      { header: "Invoice / credit ref", key: "ref", width: 12 },
      { header: "Details (first line)", key: "details", width: 52 },
      { header: "Net (£)", key: "net", width: 13 },
      { header: "VAT (£)", key: "vat", width: 11 },
    ],
    ledger.map((l) => ({ date: d(l.date), month: l.month, account: l.accountRef, member: l.member, type: l.type, ref: l.invRef ?? "", details: l.details ?? "", net: l.net, vat: l.vat })),
    { date: "dd/mm/yyyy", net: GBP, vat: GBP },
    "Sage audit trail: SI (sales invoice) and SC (sales credit) transactions for the member accounts, credits shown negative. This is the sheet the Summary and Monthly figures are built from.",
  );
  dataSheet(
    "Invoices",
    [
      { header: "Date", key: "date", width: 11 },
      { header: "Invoice no", key: "number", width: 11 },
      { header: "Type", key: "type", width: 9 },
      { header: "Account", key: "account", width: 10 },
      { header: "Member", key: "member", width: 18 },
      { header: "Customer (Sage)", key: "name", width: 32 },
      { header: "Customer order no", key: "custOrder", width: 16 },
      { header: "Goods net (£)", key: "net", width: 13 },
      { header: "VAT (£)", key: "vat", width: 11 },
      { header: "Carriage (£)", key: "carriage", width: 11 },
      { header: "Posted", key: "posted", width: 8 },
    ],
    invoices.map((i) => ({ date: d(i.date), number: i.number, type: i.type, account: i.accountRef, member: i.member, name: i.name ?? "", custOrder: i.custOrderNumber ?? "", net: i.net, vat: i.vat, carriage: i.carriage, posted: i.posted ? "Yes" : "No" })),
    { date: "dd/mm/yyyy", net: GBP, vat: GBP, carriage: GBP },
    "Sage invoice and credit-note records for the member accounts (credits negative).",
  );
  dataSheet(
    "Invoice Lines",
    [
      { header: "Date", key: "date", width: 11 },
      { header: "Invoice no", key: "number", width: 11 },
      { header: "Type", key: "type", width: 9 },
      { header: "Account", key: "account", width: 10 },
      { header: "Member", key: "member", width: 18 },
      { header: "Stock code", key: "stock", width: 14 },
      { header: "Description", key: "desc", width: 52 },
      { header: "Quantity", key: "qty", width: 10 },
      { header: "Unit price (£)", key: "price", width: 12 },
      { header: "Net (£)", key: "net", width: 13 },
    ],
    lines.map((l) => ({ date: d(l.date), number: l.number, type: l.type, account: l.accountRef, member: l.member, stock: l.stockCode, desc: l.description ?? "", qty: l.qty, price: l.unitPrice, net: l.net })),
    { date: "dd/mm/yyyy", qty: QTY, price: GBP, net: GBP },
    "One row per invoice / credit-note line (credit lines negative). The product sheets are built from this sheet.",
  );

  // ---- Notes ----
  {
    const ws = wb.addWorksheet("Notes", { views: [{ showGridLines: false }] });
    ws.columns = [{ width: 110 }];
    const put = (text: string, font: Partial<typeof BOLD> = FONT) => {
      const row = ws.addRow([text]);
      row.getCell(1).font = font;
      row.getCell(1).alignment = { wrapText: true, vertical: "top" };
    };
    put("Notes and definitions", TITLE);
    put("");
    put(`Period: ${date(from)} to ${date(to)} (inclusive). Figures from Evergreen Horticulture's Sage 50 Accounts as read ${asOf ? asOf.toLocaleString("en-GB") : "(not yet)"}; anything invoiced after that is not included.`);
    put("");
    put("Purchase turnover", BOLD);
    put("What each member bought from Evergreen: sales invoices posted to Sage less credit notes, net of VAT, in GBP. Taken from the Sage audit trail (SI less SC transactions by transaction date), which is the same basis as Sage's own customer activity reports.");
    put("");
    put("Product lines", BOLD);
    put("The item lines of those invoices and credit notes, grouped by Sage stock code. Value is the line's net amount after any line discount; quantity is in the unit Sage invoices in (bags/units, as on the invoice), credit-note lines counted negative. Rankings use the period totals across all members; the per-member columns split the same lines.");
    put("");
    put("Members and their Sage accounts", BOLD);
    for (const m of members) put(`${m.name} — ${m.accounts.map((a) => `${a.accountRef} ${a.name}`).join("; ")}`);
    put("");
    put("Reconciliation — invoice lines against the ledger", BOLD);
    for (const m of members)
      for (const a of m.accounts) {
        const led = sumAccount(a.accountRef);
        const inv = r2(invoices.filter((i) => i.accountRef === a.accountRef).reduce((s, i) => s + i.net, 0));
        put(
          `${a.accountRef} ${a.name}: ledger £${led.toLocaleString("en-GB", { minimumFractionDigits: 2 })} · invoice records £${inv.toLocaleString("en-GB", { minimumFractionDigits: 2 })}${
            Math.abs(led - inv) > 0.005 ? ` · difference £${(led - inv).toLocaleString("en-GB", { minimumFractionDigits: 2 })}` : " · agrees"
          }`,
        );
      }
    if (unsplit.length) {
      put("");
      put("Invoices deleted from Sage's invoice list after posting", BOLD);
      put("The audit trail still carries these transactions (so they are in the turnover and monthly figures), but the invoice record has been deleted in Sage, so their item lines cannot be read and they are not in the product sheets. The audit-trail detail gives only the first line of each:");
      for (const l of unsplit) put(`${date(d(l.date))} ${l.accountRef} ${l.type} ref ${l.invRef ?? ""} £${l.net.toLocaleString("en-GB", { minimumFractionDigits: 2 })} — "${l.details ?? ""}"`);
      put(`Together £${unsplit.reduce((s, l) => s + l.net, 0).toLocaleString("en-GB", { minimumFractionDigits: 2 })} of turnover has no product split.`);
    }
    put("");
    put(`Record counts: ${ledger.length} ledger transactions, ${invoices.length} invoice/credit records, ${lines.length} invoice lines, ${products.length} distinct product lines.`, MUTED);
  }

  const buffer = await wb.xlsx.writeBuffer();
  return new Blob([buffer], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
}
