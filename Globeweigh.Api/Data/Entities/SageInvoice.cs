using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// An invoice or credit-note record as Sage's invoice list holds it, with its item lines in
// SageInvoiceLine — the product detail behind the Reports pages. A read-only MIRROR the
// sidecar replaces for its window (Services/SageSalesMirror.cs). Amounts are goods only, in
// base currency, positive on credits too (IsCredit says which way they go). Quotes and
// pro-formas are never mirrored. A record Sage has deleted after posting disappears from
// here on the next sync; its SageSalesTransaction stays.
// Table created by Data/Migrations/AddSageSalesMirror.sql.
public class SageInvoice
{
    public int Id { get; set; }
    public int Number { get; set; }                        // INVOICE_NUMBER
    public DateTime Date { get; set; }
    public short TypeCode { get; set; }                    // 0/1/2 invoices, 3/4 credits
    public bool IsCredit { get; set; }
    public string AccountRef { get; set; } = "";
    public string? Name { get; set; }
    public string? OrderNumber { get; set; }               // the SOP order it was raised from
    public string? CustOrderNumber { get; set; }
    public short Currency { get; set; }
    public decimal NetAmount { get; set; }                 // BASE_TOT_NET
    public decimal TaxAmount { get; set; }                 // BASE_TOT_TAX
    public decimal CarriageNet { get; set; }               // BASE_CARR_NET
    public decimal Discount { get; set; }                  // BASE_NETVALUE_DISCOUNT
    public bool Posted { get; set; }
    public DateTime LastSeenUtc { get; set; }

    public decimal SignedNet => IsCredit ? -NetAmount : NetAmount;

    public class Config : IEntityTypeConfiguration<SageInvoice>
    {
        public void Configure(EntityTypeBuilder<SageInvoice> b)
        {
            b.ToTable("SageInvoice");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.AccountRef).HasMaxLength(10);
            b.Property(e => e.Name).HasMaxLength(60);
            b.Property(e => e.OrderNumber).HasMaxLength(10);
            b.Property(e => e.CustOrderNumber).HasMaxLength(30);
            b.Property(e => e.NetAmount).HasColumnType("decimal(18,2)");
            b.Property(e => e.TaxAmount).HasColumnType("decimal(18,2)");
            b.Property(e => e.CarriageNet).HasColumnType("decimal(18,2)");
            b.Property(e => e.Discount).HasColumnType("decimal(18,2)");
            b.Ignore(e => e.SignedNet);
        }
    }
}

// One item line of a mirrored SageInvoice. Quantities and nets are positive; the header's
// IsCredit gives the sign. Stock code is the Sage product code the line was sold under.
public class SageInvoiceLine
{
    public int Id { get; set; }
    public int InvoiceNumber { get; set; }
    public short ItemNumber { get; set; }
    public string? StockCode { get; set; }
    public string? Description { get; set; }
    public string? UnitOfSale { get; set; }
    public string? NominalCode { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal NetAmount { get; set; }                 // BASE_NET (line net after line discount)
    public decimal TaxAmount { get; set; }
    public short TaxCode { get; set; }
    public bool IsService { get; set; }

    public class Config : IEntityTypeConfiguration<SageInvoiceLine>
    {
        public void Configure(EntityTypeBuilder<SageInvoiceLine> b)
        {
            b.ToTable("SageInvoiceLine");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.StockCode).HasMaxLength(30);
            b.Property(e => e.Description).HasMaxLength(60);
            b.Property(e => e.UnitOfSale).HasMaxLength(10);
            b.Property(e => e.NominalCode).HasMaxLength(8);
            b.Property(e => e.Qty).HasColumnType("decimal(18,4)");
            b.Property(e => e.UnitPrice).HasColumnType("decimal(18,4)");
            b.Property(e => e.NetAmount).HasColumnType("decimal(18,2)");
            b.Property(e => e.TaxAmount).HasColumnType("decimal(18,2)");
        }
    }
}
