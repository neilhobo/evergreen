using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Services;

// Active Customers (Reports > Other): every Sage customer account that has bought at least once
// since a date (1 Jan 2025 unless the page says otherwise), from the Sage sales mirror, since
// Evergreen's own order history does not reach that far back.
//
// "Bought" = at least one posted sales invoice (SI) on the Sage AUDIT TRAIL in the window, the
// same turnover truth MemberSalesReport uses (it survives an invoice deleted from the invoice
// list after posting). Turnover is net of VAT, invoices less credit notes. Orders counts the
// distinct SOP order numbers on the account's invoices in the window (SageInvoice), so an order
// invoiced in two parts counts once. Name, address and contact come from the Sage customer
// mirror (SageCustomer); an account it no longer holds keeps the name off its last invoice.
public static class ActiveCustomersReport
{
    public record CustomerDto(
        string AccountRef,
        string Name,
        string? Address,
        string? Postcode,
        string? Contact,
        string? Telephone,
        string? Email,
        DateTime FirstInvoice,
        DateTime LastInvoice,
        int Invoices,
        int Credits,
        int Orders,
        decimal Turnover,
        decimal Balance,
        bool OnHold,
        bool Inactive,
        bool InCustomerMirror);

    public record Report(
        DateTime From,
        DateTime To,
        MemberSalesReport.SyncDto Sync,
        int SageCustomers,                 // accounts in the customer mirror, for "x of y"
        List<CustomerDto> Customers);

    public static async Task<Report> BuildAsync(GbEvergreenContext db, DateTime from, DateTime to, CancellationToken ct)
    {
        from = from.Date;
        to = to.Date;
        var end = to.AddDays(1);

        var sync = await db.Set<SageSyncState>().AsNoTracking().FirstOrDefaultAsync(s => s.Entity == SageSyncState.Sales, ct);

        var byAccount = await db.Set<SageSalesTransaction>().AsNoTracking()
            .Where(t => t.Date >= from && t.Date < end)
            .GroupBy(t => t.AccountRef)
            .Select(g => new
            {
                AccountRef = g.Key,
                Invoices = g.Count(t => t.Type == SageSalesTransaction.Invoice),
                Credits = g.Count(t => t.Type == SageSalesTransaction.Credit),
                Turnover = g.Sum(t => t.Type == SageSalesTransaction.Credit ? -t.NetAmount : t.NetAmount),
                First = g.Where(t => t.Type == SageSalesTransaction.Invoice).Min(t => (DateTime?)t.Date),
                Last = g.Where(t => t.Type == SageSalesTransaction.Invoice).Max(t => (DateTime?)t.Date),
            })
            .Where(a => a.Invoices > 0)
            .ToListAsync(ct);

        var refs = byAccount.Select(a => a.AccountRef).ToList();

        var orders = (await db.Set<SageInvoice>().AsNoTracking()
                .Where(i => !i.IsCredit && i.Date >= from && i.Date < end && i.OrderNumber != null && i.OrderNumber != "")
                .Select(i => new { i.AccountRef, i.OrderNumber })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(o => o.AccountRef, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var customers = (await db.Set<SageCustomer>().AsNoTracking().ToListAsync(ct))
            .GroupBy(c => c.AccountRef, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // The name off the newest invoice, for an account the customer mirror does not hold.
        var missing = refs.Where(r => !customers.ContainsKey(r)).ToList();
        var invoiceNames = missing.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : (await db.Set<SageInvoice>().AsNoTracking()
                    .Where(i => missing.Contains(i.AccountRef) && i.Name != null)
                    .OrderByDescending(i => i.Date)
                    .Select(i => new { i.AccountRef, i.Name })
                    .ToListAsync(ct))
                .GroupBy(i => i.AccountRef, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Name!, StringComparer.OrdinalIgnoreCase);

        var rows = byAccount.Select(a =>
            {
                customers.TryGetValue(a.AccountRef, out var c);
                var (address, postcode) = c == null ? (null, null) : Address(c);
                return new CustomerDto(
                    a.AccountRef,
                    c?.Name?.Trim() is { Length: > 0 } n ? n : invoiceNames.GetValueOrDefault(a.AccountRef) ?? a.AccountRef,
                    address,
                    postcode,
                    Blank(c?.Contact),
                    Blank(c?.Telephone),
                    Blank(c?.Email),
                    a.First!.Value,
                    a.Last!.Value,
                    a.Invoices,
                    a.Credits,
                    orders.GetValueOrDefault(a.AccountRef),
                    a.Turnover,
                    c?.Balance ?? 0,
                    c?.OnHold ?? false,
                    c?.Inactive ?? false,
                    c != null);
            })
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.AccountRef)
            .ToList();

        return new Report(from, to,
            new MemberSalesReport.SyncDto(Utc(sync?.LastRunUtc), Utc(sync?.LastSuccessUtc), sync?.LastError, sync?.RowCount ?? 0,
                sync?.DetailCount ?? 0, sync?.Machine, Utc(sync?.RequestedUtc), sync?.WindowFrom),
            customers.Count,
            rows);
    }

    // Sage's five address lines as one line, the last non-blank one taken as the postcode when it
    // looks like one (Evergreen's customers are UK and Irish: "BT62 1AB", "D24 X2Y3", "Co. Cork").
    private static (string? Address, string? Postcode) Address(SageCustomer c)
    {
        var lines = new[] { c.Address1, c.Address2, c.Address3, c.Address4, c.Address5 }
            .Select(Blank).Where(l => l != null).Select(l => l!).ToList();
        string? postcode = null;
        if (lines.Count > 1 && LooksLikePostcode(lines[^1]))
        {
            postcode = lines[^1];
            lines.RemoveAt(lines.Count - 1);
        }
        return (lines.Count == 0 ? null : string.Join(", ", lines), postcode);
    }

    private static bool LooksLikePostcode(string s)
    {
        var t = s.Replace(" ", "");
        return t.Length is >= 5 and <= 8 && t.Any(char.IsDigit) && t.Any(char.IsLetter) && t.All(char.IsLetterOrDigit);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateTime? Utc(DateTime? d) => d == null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc);
}
