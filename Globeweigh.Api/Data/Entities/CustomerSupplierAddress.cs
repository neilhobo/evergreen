using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the CustomerSupplier_Address table — used for both reading
// the addresses panel and CRUD writes (the addresses are now editable).
public class CustomerSupplierAddress
{
    public int Id { get; set; }
    public int CustomerSupplierId { get; set; }
    public string? AddressName { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? Address3 { get; set; }
    public string? Address4 { get; set; }
    public string? Address5 { get; set; }
    public string? Telephone { get; set; }
    public string? Telephone2 { get; set; }
    public string? Email { get; set; }
    public string? ContactName { get; set; }
    // Reusable Delivery Note Info for this address (Edit Customer > Delivery Note Info).
    public string? DeliveryNote { get; set; }
    public string? SageAccountRef { get; set; }
    public int AddressSageRef { get; set; }
    public string? SageDescription { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateLastModified { get; set; }

    public class Config : IEntityTypeConfiguration<CustomerSupplierAddress>
    {
        public void Configure(EntityTypeBuilder<CustomerSupplierAddress> b)
        {
            b.ToTable("CustomerSupplier_Address");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
