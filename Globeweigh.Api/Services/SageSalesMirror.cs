using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Services;

// Replaces the Sage sales mirror (SageSalesTransaction / SageInvoice / SageInvoiceLine) with
// the snapshot the sidecar read from Sage. See Data/Entities/SageSalesTransaction.cs for what
// the mirror is for. The whole mirror is swapped, not merged: it always equals the sidecar's
// last read of its window, so a transaction Sage has since deleted or back-dated out of the
// window goes too, and the window's start moving on at New Year drops the oldest year.
public static class SageSalesMirror
{
    // ---- The contract with the sidecar ------------------------------------------------
    // PUT /api/sage/sidecar/sales. Field names match the sidecar's SalesSnapshot /
    // TransactionRow / InvoiceRow / InvoiceLine (SageSopWriter.cs) — change both together.

    public class Snapshot
    {
        public string? Machine { get; set; }
        public string? Company { get; set; }
        public DateTime? ReadAt { get; set; }
        public int ReadMs { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<TransactionIn> Transactions { get; set; } = [];
        public List<InvoiceIn> Invoices { get; set; } = [];
        public int HeadersScanned { get; set; }
        public int InvoicesScanned { get; set; }
        public List<string> Notes { get; set; } = [];
    }

    public class TransactionIn
    {
        public int UniqueRef { get; set; }
        public string? AccountRef { get; set; }
        public short Type { get; set; }
        public DateTime Date { get; set; }
        public string? InvRef { get; set; }
        public string? Details { get; set; }
        public double NetAmount { get; set; }
        public double TaxAmount { get; set; }
        public double ForeignNetAmount { get; set; }
        public short Currency { get; set; }
        public string? UserName { get; set; }
    }

    public class InvoiceIn
    {
        public int Number { get; set; }
        public DateTime Date { get; set; }
        public short TypeCode { get; set; }
        public bool IsCredit { get; set; }
        public string? AccountRef { get; set; }
        public string? Name { get; set; }
        public string? OrderNumber { get; set; }
        public string? CustOrderNumber { get; set; }
        public short Currency { get; set; }
        public double BaseTotNet { get; set; }
        public double BaseTotTax { get; set; }
        public double BaseCarrNet { get; set; }
        public double BaseNetValueDiscount { get; set; }
        public bool Posted { get; set; }
        public List<LineIn>? Lines { get; set; }
    }

    public class LineIn
    {
        public short ItemNumber { get; set; }
        public string? StockCode { get; set; }
        public string? Description { get; set; }
        public string? UnitOfSale { get; set; }
        public string? NominalCode { get; set; }
        public double Qty { get; set; }
        public double UnitPrice { get; set; }
        public double NetAmount { get; set; }
        public double BaseNet { get; set; }
        public double TaxAmount { get; set; }
        public short TaxCode { get; set; }
        public bool Service { get; set; }
    }

    // One transaction, so a report never adds up half a sync. Returns what the mirror now holds.
    public static async Task<(int Transactions, int Invoices, int Lines)> ReplaceAsync(
        GbEvergreenContext db, Snapshot snap, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var wasDetecting = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;      // tens of thousands of adds
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            await db.Set<SageInvoiceLine>().ExecuteDeleteAsync(ct);
            await db.Set<SageInvoice>().ExecuteDeleteAsync(ct);
            await db.Set<SageSalesTransaction>().ExecuteDeleteAsync(ct);

            var transactions = 0;
            foreach (var t in snap.Transactions)
            {
                var key = Key(t.AccountRef);
                if (key.Length == 0) continue;
                db.Add(new SageSalesTransaction
                {
                    UniqueRef = t.UniqueRef,
                    AccountRef = key,
                    Type = t.Type,
                    Date = t.Date.Date,
                    InvRef = T(t.InvRef, 30),
                    Details = T(t.Details, 60),
                    NetAmount = D(t.NetAmount),
                    TaxAmount = D(t.TaxAmount),
                    ForeignNetAmount = D(t.ForeignNetAmount),
                    Currency = t.Currency,
                    UserName = T(t.UserName, 32),
                    LastSeenUtc = now,
                });
                transactions++;
            }

            var invoices = 0;
            var lines = 0;
            var numbers = new HashSet<int>();
            foreach (var i in snap.Invoices)
            {
                var key = Key(i.AccountRef);
                // The unique index on Number insists; Sage does not repeat numbers, but a
                // snapshot must never fail the whole sync on one odd record.
                if (key.Length == 0 || i.Number <= 0 || !numbers.Add(i.Number)) continue;
                db.Add(new SageInvoice
                {
                    Number = i.Number,
                    Date = i.Date.Date,
                    TypeCode = i.TypeCode,
                    IsCredit = i.IsCredit,
                    AccountRef = key,
                    Name = T(i.Name, 60),
                    OrderNumber = T(i.OrderNumber, 10),
                    CustOrderNumber = T(i.CustOrderNumber, 30),
                    Currency = i.Currency,
                    NetAmount = D(i.BaseTotNet),
                    TaxAmount = D(i.BaseTotTax),
                    CarriageNet = D(i.BaseCarrNet),
                    Discount = D(i.BaseNetValueDiscount),
                    Posted = i.Posted,
                    LastSeenUtc = now,
                });
                invoices++;
                if (i.Lines == null) continue;
                foreach (var l in i.Lines)
                {
                    db.Add(new SageInvoiceLine
                    {
                        InvoiceNumber = i.Number,
                        ItemNumber = l.ItemNumber,
                        StockCode = T(l.StockCode, 30),
                        Description = T(l.Description, 60),
                        UnitOfSale = T(l.UnitOfSale, 10),
                        NominalCode = T(l.NominalCode, 8),
                        Qty = D4(l.Qty),
                        UnitPrice = D4(l.UnitPrice),
                        // BASE_NET is the line net in base currency; a base-currency document
                        // may leave it 0 and carry the figure in NET_AMOUNT only.
                        NetAmount = D(l.BaseNet != 0 ? l.BaseNet : l.NetAmount),
                        TaxAmount = D(l.TaxAmount),
                        TaxCode = l.TaxCode,
                        IsService = l.Service,
                    });
                    lines++;
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (transactions, invoices, lines);
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = wasDetecting;
        }
    }

    private static string Key(string? accountRef) => (accountRef ?? "").Trim().ToUpperInvariant();

    private static decimal D(double v) => (decimal)Math.Round(v, 2);
    private static decimal D4(double v) => (decimal)Math.Round(v, 4);

    private static string? T(string? s, int max)
    {
        if (s == null) return null;
        var v = s.Trim();
        if (v.Length == 0) return null;
        return v.Length <= max ? v : v[..max];
    }
}
