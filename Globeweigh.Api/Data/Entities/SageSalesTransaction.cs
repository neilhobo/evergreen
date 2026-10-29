using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One posted sales invoice (SI) or credit note (SC) off Sage's audit trail — a read-only
// MIRROR, like SageCustomer, kept fresh by Globeweigh.SageSidecar and replaced as a whole
// for the window it covers (Services/SageSalesMirror.cs). This is the turnover truth the
// Reports pages add up: it is what Sage's own customer activity shows, and it survives an
// invoice being deleted from the invoice list after posting (SageInvoice does not).
// Table created by Data/Migrations/AddSageSalesMirror.sql.
public class SageSalesTransaction
{
    public int Id { get; set; }
    public int UniqueRef { get; set; }                     // Sage UNIQUE_REF on the header
    public string AccountRef { get; set; } = "";
    public short Type { get; set; }                        // 1 = SI, 2 = SC
    public DateTime Date { get; set; }
    public string? InvRef { get; set; }                    // the invoice/credit number as text
    public string? Details { get; set; }                   // first line's description
    public decimal NetAmount { get; set; }                 // base currency, positive on both types
    public decimal TaxAmount { get; set; }
    public decimal ForeignNetAmount { get; set; }
    public short Currency { get; set; }                    // 0 = base (GBP)
    public string? UserName { get; set; }
    public DateTime LastSeenUtc { get; set; }

    public const short Invoice = 1;
    public const short Credit = 2;

    // Signed: invoices add, credits take away.
    public decimal SignedNet => Type == Credit ? -NetAmount : NetAmount;

    public class Config : IEntityTypeConfiguration<SageSalesTransaction>
    {
        public void Configure(EntityTypeBuilder<SageSalesTransaction> b)
        {
            b.ToTable("SageSalesTransaction");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.AccountRef).HasMaxLength(10);
            b.Property(e => e.InvRef).HasMaxLength(30);
            b.Property(e => e.Details).HasMaxLength(60);
            b.Property(e => e.UserName).HasMaxLength(32);
            b.Property(e => e.NetAmount).HasColumnType("decimal(18,2)");
            b.Property(e => e.TaxAmount).HasColumnType("decimal(18,2)");
            b.Property(e => e.ForeignNetAmount).HasColumnType("decimal(18,2)");
            b.Ignore(e => e.SignedNet);
        }
    }
}
