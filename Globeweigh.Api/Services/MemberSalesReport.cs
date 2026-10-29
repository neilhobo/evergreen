using System.Globalization;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Services;

// A member-sales report: what each named group of Sage accounts (ReportMemberGroup) bought
// from Evergreen in a date window, from the Sage sales mirror. Built for the Allied Merchant
// Buying Group's request (2026-10-08): purchase turnover YTD by member with the total, the
// product lines by value and by quantity split per member, and monthly sales by member.
//
// Two sources on purpose, both from Sage:
//   - turnover, monthly figures and the totals come from the AUDIT TRAIL (SageSalesTransaction:
//     posted SI less SC, net of VAT) — what Sage's own customer activity shows, and the only
//     record of an invoice deleted from the invoice list after posting;
//   - the product split comes from the INVOICE LINES (SageInvoiceLine). An audit-trail entry
//     with no invoice record left behind it is reported in Unsplit so the two can be reconciled
//     on the page and in the export.
public static class MemberSalesReport
{
    public record AccountDto(string AccountRef, string Name, decimal Turnover, int Invoices, int Credits);
    public record MemberDto(string Name, List<AccountDto> Accounts, decimal Turnover, int Invoices, int Credits, decimal Share);
    public record TotalsDto(decimal Turnover, int Invoices, int Credits);
    public record MonthDto(string Month, string Label, Dictionary<string, decimal> ByMember, decimal Total);
    public record ProductMemberDto(decimal Value, decimal Qty);
    public record ProductDto(string StockCode, string Description, decimal Value, decimal Qty, Dictionary<string, ProductMemberDto> ByMember);
    public record UnsplitDto(DateTime Date, string AccountRef, string Member, string Type, string? InvRef, string? Details, decimal Net);
    public record TransactionDto(DateTime Date, string Month, string AccountRef, string Member, string Type, string? InvRef, string? Details, decimal Net, decimal Vat);
    public record InvoiceDto(DateTime Date, int Number, string Type, string AccountRef, string Member, string? Name, string? CustOrderNumber, decimal Net, decimal Vat, decimal Carriage, bool Posted);
    public record LineDto(DateTime Date, int Number, string Type, string AccountRef, string Member, string StockCode, string? Description, decimal Qty, decimal UnitPrice, decimal Net);
    public record SyncDto(DateTime? LastRunUtc, DateTime? LastSuccessUtc, string? LastError, int RowCount, int DetailCount, string? Machine, DateTime? RequestedUtc, DateTime? WindowFrom);

    public record Report(
        string ReportKey,
        DateTime From,
        DateTime To,
        SyncDto Sync,
        List<MemberDto> Members,
        TotalsDto Totals,
        List<MonthDto> Months,
        List<ProductDto> Products,
        List<UnsplitDto> Unsplit,
        List<TransactionDto> Transactions,
        List<InvoiceDto> Invoices,
        List<LineDto> Lines);

    public static async Task<Report> BuildAsync(GbEvergreenContext db, string reportKey, DateTime from, DateTime to, CancellationToken ct)
    {
        from = from.Date;
        to = to.Date;
        var end = to.AddDays(1);

        var groups = await db.Set<ReportMemberGroup>().AsNoTracking()
            .Where(g => g.ReportKey == reportKey)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .ToListAsync(ct);
        var memberOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
            foreach (var a in g.Accounts)
                memberOf.TryAdd(a, g.Name);
        var accounts = memberOf.Keys.ToList();

        var sync = await db.Set<SageSyncState>().AsNoTracking().FirstOrDefaultAsync(s => s.Entity == SageSyncState.Sales, ct);

        var trans = await db.Set<SageSalesTransaction>().AsNoTracking()
            .Where(t => accounts.Contains(t.AccountRef) && t.Date >= from && t.Date < end)
            .OrderBy(t => t.Date).ThenBy(t => t.UniqueRef)
            .ToListAsync(ct);
        var invoices = await db.Set<SageInvoice>().AsNoTracking()
            .Where(i => accounts.Contains(i.AccountRef) && i.Date >= from && i.Date < end)
            .OrderBy(i => i.Date).ThenBy(i => i.Number)
            .ToListAsync(ct);
        var numbers = invoices.Select(i => i.Number).ToList();
        var lines = numbers.Count == 0
            ? []
            : await db.Set<SageInvoiceLine>().AsNoTracking()
                .Where(l => numbers.Contains(l.InvoiceNumber))
                .OrderBy(l => l.InvoiceNumber).ThenBy(l => l.ItemNumber)
                .ToListAsync(ct);
        var names = await db.Set<SageCustomer>().AsNoTracking()
            .Where(c => accounts.Contains(c.AccountRef))
            .ToDictionaryAsync(c => c.AccountRef, c => c.Name ?? "", StringComparer.OrdinalIgnoreCase, ct);
        foreach (var i in invoices)
            if (!names.ContainsKey(i.AccountRef) && !string.IsNullOrWhiteSpace(i.Name)) names[i.AccountRef] = i.Name!;

        string Member(string acc) => memberOf.TryGetValue(acc, out var m) ? m : acc;
        string Name(string acc) => names.TryGetValue(acc, out var n) && n.Length > 0 ? n : acc;

        // ---- members and totals (audit trail) ----
        var grand = trans.Sum(t => t.SignedNet);
        var members = new List<MemberDto>();
        foreach (var g in groups)
        {
            var accs = new List<AccountDto>();
            foreach (var a in g.Accounts.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var at = trans.Where(t => t.AccountRef.Equals(a, StringComparison.OrdinalIgnoreCase)).ToList();
                accs.Add(new AccountDto(a.ToUpperInvariant(), Name(a),
                    at.Sum(t => t.SignedNet),
                    at.Count(t => t.Type == SageSalesTransaction.Invoice),
                    at.Count(t => t.Type == SageSalesTransaction.Credit)));
            }
            var turnover = accs.Sum(x => x.Turnover);
            members.Add(new MemberDto(g.Name, accs, turnover, accs.Sum(x => x.Invoices), accs.Sum(x => x.Credits),
                grand == 0 ? 0 : Math.Round(turnover / grand, 4)));
        }
        var totals = new TotalsDto(grand,
            trans.Count(t => t.Type == SageSalesTransaction.Invoice),
            trans.Count(t => t.Type == SageSalesTransaction.Credit));

        // ---- months (audit trail) ----
        var months = new List<MonthDto>();
        for (var m = new DateTime(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            var key = m.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var inMonth = trans.Where(t => t.Date.Year == m.Year && t.Date.Month == m.Month).ToList();
            var byMember = groups.ToDictionary(g => g.Name,
                g => inMonth.Where(t => Member(t.AccountRef) == g.Name).Sum(t => t.SignedNet));
            months.Add(new MonthDto(key, m.ToString("MMM yyyy", CultureInfo.GetCultureInfo("en-GB")), byMember, inMonth.Sum(t => t.SignedNet)));
        }

        // ---- products (invoice lines) ----
        var invByNo = invoices.ToDictionary(i => i.Number);
        var products = new Dictionary<string, (string Desc, decimal Value, decimal Qty, Dictionary<string, (decimal V, decimal Q)> By)>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in lines)
        {
            if (!invByNo.TryGetValue(l.InvoiceNumber, out var inv)) continue;
            var sign = inv.IsCredit ? -1 : 1;
            var code = string.IsNullOrWhiteSpace(l.StockCode) ? "(no stock code)" : l.StockCode!.Trim();
            var member = Member(inv.AccountRef);
            if (!products.TryGetValue(code, out var p))
                p = (l.Description ?? "", 0, 0, new Dictionary<string, (decimal, decimal)>());
            if (p.Desc.Length == 0 && !string.IsNullOrWhiteSpace(l.Description)) p.Desc = l.Description!;
            p.Value += sign * l.NetAmount;
            p.Qty += sign * l.Qty;
            p.By.TryGetValue(member, out var mb);
            p.By[member] = (mb.V + sign * l.NetAmount, mb.Q + sign * l.Qty);
            products[code] = p;
        }
        var productList = products
            .Select(kv => new ProductDto(kv.Key, kv.Value.Desc, kv.Value.Value, kv.Value.Qty,
                kv.Value.By.ToDictionary(b => b.Key, b => new ProductMemberDto(b.Value.V, b.Value.Q))))
            .OrderByDescending(p => p.Value).ThenBy(p => p.StockCode)
            .ToList();

        // ---- audit-trail entries with no invoice record (deleted after posting) ----
        var invRefs = new HashSet<string>(invoices.Select(i => i.Number.ToString(CultureInfo.InvariantCulture)));
        var unsplit = trans
            .Where(t => !invRefs.Contains((t.InvRef ?? "").Trim()))
            .Select(t => new UnsplitDto(t.Date, t.AccountRef, Member(t.AccountRef), TypeName(t.Type), t.InvRef, t.Details, t.SignedNet))
            .ToList();

        // ---- raw rows for the export ----
        var transactions = trans.Select(t => new TransactionDto(
            t.Date, t.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture), t.AccountRef, Member(t.AccountRef), TypeName(t.Type),
            t.InvRef, t.Details, t.SignedNet, t.Type == SageSalesTransaction.Credit ? -t.TaxAmount : t.TaxAmount)).ToList();
        var invoiceRows = invoices.Select(i => new InvoiceDto(
            i.Date, i.Number, i.IsCredit ? "Credit" : "Invoice", i.AccountRef, Member(i.AccountRef), i.Name, i.CustOrderNumber,
            i.SignedNet, i.IsCredit ? -i.TaxAmount : i.TaxAmount, i.IsCredit ? -i.CarriageNet : i.CarriageNet, i.Posted)).ToList();
        var lineRows = new List<LineDto>();
        foreach (var l in lines)
        {
            if (!invByNo.TryGetValue(l.InvoiceNumber, out var inv)) continue;
            var sign = inv.IsCredit ? -1 : 1;
            lineRows.Add(new LineDto(inv.Date, inv.Number, inv.IsCredit ? "Credit" : "Invoice", inv.AccountRef, Member(inv.AccountRef),
                string.IsNullOrWhiteSpace(l.StockCode) ? "(no stock code)" : l.StockCode!.Trim(), l.Description,
                sign * l.Qty, l.UnitPrice, sign * l.NetAmount));
        }

        return new Report(reportKey, from, to,
            new SyncDto(Utc(sync?.LastRunUtc), Utc(sync?.LastSuccessUtc), sync?.LastError, sync?.RowCount ?? 0, sync?.DetailCount ?? 0,
                sync?.Machine, Utc(sync?.RequestedUtc), sync?.WindowFrom),
            members, totals, months, productList, unsplit, transactions, invoiceRows, lineRows);
    }

    private static string TypeName(short type) => type == SageSalesTransaction.Credit ? "Credit" : "Invoice";

    private static DateTime? Utc(DateTime? d) => d == null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc);
}
