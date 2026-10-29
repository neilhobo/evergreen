using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One entry from Sage's customer address book (SDO's CustomerAddresses collection): the
// registered, delivery and contact addresses of a SageCustomer, keyed by Sage's own
// per-account AddressNumber. Replaced wholesale on every sync — Sage renumbers these, so
// there is no identity worth preserving across runs. See SageCustomer.cs.
// Table created by Data/Migrations/AddSageCustomerTables.sql.
public class SageCustomerAddress
{
    public int Id { get; set; }
    public string AccountRef { get; set; } = "";
    public int AddressNumber { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? AddressType { get; set; }               // Registered | Delivery | Contact
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? Address3 { get; set; }
    public string? Address4 { get; set; }
    public string? Address5 { get; set; }
    public string? Telephone { get; set; }
    public string? Contact { get; set; }
    public string? Email { get; set; }
    public DateTime LastSeenUtc { get; set; }

    public const string Delivery = "Delivery";

    public class Config : IEntityTypeConfiguration<SageCustomerAddress>
    {
        public void Configure(EntityTypeBuilder<SageCustomerAddress> b)
        {
            b.ToTable("SageCustomerAddress");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.AccountRef).HasMaxLength(10);
            b.Property(e => e.AddressType).HasMaxLength(30);
        }
    }
}
