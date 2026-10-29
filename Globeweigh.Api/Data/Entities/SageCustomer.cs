using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// A customer as Sage 50 holds it — a read-only MIRROR of the Sage customer ledger, kept
// fresh by Globeweigh.SageSidecar, the only process that can read Sage (SageExportJob.cs
// has the why). Admin > Sage Link > Customers lists this table; the API never asks Sage
// anything itself. Replaced as a whole on every sync (Services/SageCustomerMirror.cs): an
// account no longer in Sage is deleted here too, FirstSeenUtc survives an update.
// Table created by Data/Migrations/AddSageCustomerTables.sql.
public class SageCustomer
{
    public int Id { get; set; }
    public string AccountRef { get; set; } = "";          // Sage A/C ref, upper-cased
    public string? Name { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? Address3 { get; set; }
    public string? Address4 { get; set; }
    public string? Address5 { get; set; }
    public string? Contact { get; set; }
    public string? Telephone { get; set; }
    public string? Telephone2 { get; set; }
    public string? Email { get; set; }
    public string? VatNumber { get; set; }
    public string? DelName { get; set; }
    public short DefTaxCode { get; set; }                  // Sage's T-code number (T20 = 20)
    public decimal Balance { get; set; }
    public bool OnHold { get; set; }
    public bool Inactive { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    public class Config : IEntityTypeConfiguration<SageCustomer>
    {
        public void Configure(EntityTypeBuilder<SageCustomer> b)
        {
            b.ToTable("SageCustomer");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.AccountRef).HasMaxLength(10);
            b.Property(e => e.Balance).HasColumnType("decimal(18,2)");
        }
    }
}
